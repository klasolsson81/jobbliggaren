import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import { createTranslator, createFormatter } from "next-intl";
import svAktivitetsrapport from "../../../../messages/sv/aktivitetsrapport.json";
import type { ApiResult } from "@/lib/dto/_helpers";
import type { ActivityReportDto } from "@/lib/dto/activity-report";
import AktivitetsrapportPage from "./page";

const redirect = vi.fn();
const push = vi.fn();
const getServerSession = vi.fn();
const getActivityReport =
  vi.fn<() => Promise<ApiResult<ActivityReportDto>>>();

// The async server page resolves copy via `getTranslations("aktivitetsrapport")`
// and dates via `getFormatter()` from next-intl/server (unavailable in jsdom).
// Mock both to real next-intl instances over the Swedish catalog (source of
// truth). The client ActivityReportView resolves its OWN copy via the test
// render's NextIntlClientProvider (full sv catalog) — the marker text asserted
// below comes from there.
vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: "aktivitetsrapport") =>
    createTranslator({
      locale: "sv",
      messages: { aktivitetsrapport: svAktivitetsrapport },
      namespace,
    }),
  getFormatter: async () =>
    createFormatter({ locale: "sv", timeZone: "Europe/Stockholm" }),
}));

vi.mock("@/lib/auth/session", () => ({
  getServerSession: () => getServerSession(),
}));

vi.mock("@/lib/api/applications", () => ({
  getActivityReport: () => getActivityReport(),
}));

// Partial mock: keep the real module but override the server redirect the page
// calls AND useRouter (the client ActivityReportView calls it for month
// navigation; the real hook needs an AppRouterContext provider absent in jsdom).
vi.mock("next/navigation", async (importOriginal) => {
  const actual = await importOriginal<typeof import("next/navigation")>();
  return {
    ...actual,
    useRouter: () => ({ back: vi.fn(), push, replace: vi.fn() }),
    redirect: (url: string) => {
      redirect(url);
      throw new Error(`NEXT_REDIRECT:${url}`);
    },
  };
});

function report(
  applications: ActivityReportDto["applications"],
): ActivityReportDto {
  return { year: 2026, month: 5, applications };
}

async function renderPage() {
  const element = await AktivitetsrapportPage({
    searchParams: Promise.resolve({}),
  });
  return render(element);
}

// #892 (CTO R1/R5): the RSC page is the ONE line that wires the tested wire-DTO
// (`item.adStatus`) to the tested view prop (`row.adRemoved`) — the view test
// injects `adRemoved` directly, so this mapping (`item.adStatus === "Erased"`)
// is otherwise unproven glue. The projection is STRUCTURAL: keyed on the
// lifecycle status, never on matching the "[raderad]" literal (which never
// reaches the wire, R5).
describe("AktivitetsrapportPage adStatus→adRemoved mapping (#892)", () => {
  beforeEach(() => {
    redirect.mockReset();
    push.mockReset();
    getServerSession.mockReset();
    getActivityReport.mockReset();
    getServerSession.mockResolvedValue({ email: "a@b.se", roles: [] });
    Object.assign(navigator, {
      clipboard: { writeText: vi.fn().mockResolvedValue(undefined) },
    });
  });

  it("markerar ENDAST den raderade annonsens rad (Erased) — inte den levande", async () => {
    getActivityReport.mockResolvedValue({
      kind: "ok",
      data: report([
        {
          applicationId: "erased-1",
          appliedAt: "2026-05-10T08:00:00Z",
          employer: "Raderad AB",
          title: "Raderad roll",
          location: "Stockholm",
          source: "Platsbanken",
          url: null,
          adStatus: "Erased",
        },
        {
          applicationId: "live-1",
          appliedAt: "2026-05-11T08:00:00Z",
          employer: "Aktiv AB",
          title: "Aktiv roll",
          location: "Göteborg",
          source: "Platsbanken",
          url: null,
          adStatus: "Active",
        },
      ]),
    });

    await renderPage();

    // Exakt EN markör i hela rapporten — kills the "always append" mutation.
    expect(screen.getAllByText("Annonsen är borttagen")).toHaveLength(1);

    // …och den sitter på den RADERADE annonsens kort, inte den levande —
    // kills `=== "Active"` (markören skulle byta kort) och `=== "Erased"→false`.
    const erasedCard = screen
      .getByRole("heading", { name: "Raderad roll" })
      .closest("li")!;
    expect(
      within(erasedCard).getByText("Annonsen är borttagen"),
    ).toBeInTheDocument();

    const liveCard = screen
      .getByRole("heading", { name: "Aktiv roll" })
      .closest("li")!;
    expect(
      within(liveCard).queryByText("Annonsen är borttagen"),
    ).toBeNull();
  });

  it("en manuell rad (adStatus null) bär ingen markör (#805-3-idiomet, ingen livs-utsaga)", async () => {
    getActivityReport.mockResolvedValue({
      kind: "ok",
      data: report([
        {
          applicationId: "manual-1",
          appliedAt: "2026-05-12T08:00:00Z",
          employer: "Eget bolag",
          title: "Manuell roll",
          location: null,
          source: null,
          url: null,
          adStatus: null,
        },
      ]),
    });

    await renderPage();

    expect(screen.queryByText("Annonsen är borttagen")).toBeNull();
    // R5: den domän-interna sentinelen når aldrig wiren → aldrig DOM.
    expect(screen.queryByText("[raderad]")).toBeNull();
  });
});

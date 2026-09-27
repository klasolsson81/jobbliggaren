import { describe, it, expect, vi, beforeEach } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { createTranslator, createFormatter } from "next-intl";
import svPages from "../../../../../messages/sv/pages.json";
import type { ApiResult } from "@/lib/dto/_helpers";
import type {
  AdSnapshotDto,
  ApplicationDetailDto,
} from "@/lib/dto/applications";
import AnsokanDetailPage from "./page";

const getServerSession = vi.fn();
const getApplicationById =
  vi.fn<() => Promise<ApiResult<ApplicationDetailDto>>>();

// The async server page resolves copy via `getTranslations("pages")` and dates via
// `getFormatter()` from next-intl/server (unavailable in jsdom); both are real next-intl
// instances over the Swedish catalog. The client islands resolve their own copy through the
// test render's provider.
vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: "pages") =>
    createTranslator({ locale: "sv", messages: { pages: svPages }, namespace }),
  getFormatter: async () =>
    createFormatter({ locale: "sv", timeZone: "Europe/Stockholm" }),
}));

vi.mock("@/lib/auth/session", () => ({
  getServerSession: () => getServerSession(),
}));

vi.mock("@/lib/api/applications", () => ({
  getApplicationById: () => getApplicationById(),
}));

// DeleteApplicationButton calls useRouter; the real hook needs an AppRouterContext.
vi.mock("next/navigation", async (importOriginal) => {
  const actual = await importOriginal<typeof import("next/navigation")>();
  return {
    ...actual,
    useRouter: () => ({ back: vi.fn(), push: vi.fn(), replace: vi.fn() }),
    redirect: (url: string) => {
      throw new Error(`NEXT_REDIRECT:${url}`);
    },
    notFound: () => {
      throw new Error("NEXT_NOT_FOUND");
    },
  };
});

vi.mock("@/lib/actions/applications", () => ({
  transitionStatusAction: vi.fn().mockResolvedValue({ success: true }),
  addNoteAction: vi.fn().mockResolvedValue({ success: true }),
  addFollowUpAction: vi.fn().mockResolvedValue({ success: true }),
  recordFollowUpOutcomeAction: vi.fn().mockResolvedValue({ success: true }),
  logFollowUpAction: vi.fn().mockResolvedValue({ success: true }),
  deleteApplicationAction: vi.fn().mockResolvedValue({ success: true }),
}));

function makeDetail(
  overrides: Partial<ApplicationDetailDto> = {},
): ApplicationDetailDto {
  return {
    id: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
    jobSeekerId: "seeker-1",
    jobAdId: "ad-1",
    status: "Submitted",
    createdAt: "2026-05-01T08:00:00Z",
    updatedAt: "2026-05-10T08:00:00Z",
    jobAd: {
      jobAdId: "ad-1",
      title: "Backend-utvecklare",
      company: "Volvo",
      url: "https://example.com/ad",
      source: "Platsbanken",
      publishedAt: "2026-05-01",
      expiresAt: "2026-06-01",
      status: "Active",
    },
    coverLetter: null,
    followUps: [],
    notes: [],
    ...overrides,
  };
}

const snapshot: AdSnapshotDto = {
  title: "Backend-utvecklare",
  company: "Volvo",
  location: "Göteborg",
  url: null,
  source: "Platsbanken",
  publishedAt: "2026-04-10T08:00:00Z",
  expiresAt: null,
  description: "Vi söker en utvecklare.",
  contacts: [],
  capturedAt: "2026-04-12T08:00:00Z",
};

async function renderPage() {
  const element = await AnsokanDetailPage({
    params: Promise.resolve({ id: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee" }),
  });
  return render(element);
}

describe("/ansokningar/[id] — the full page renders the one detail body (#699)", () => {
  beforeEach(() => {
    getServerSession.mockReset();
    getApplicationById.mockReset();
    getServerSession.mockResolvedValue({ email: "a@b.se", roles: [] });
  });

  it("puts the shared header above the body: the ad's title as h1 and the company", async () => {
    getApplicationById.mockResolvedValue({ kind: "ok", data: makeDetail() });
    await renderPage();

    expect(
      screen.getByRole("heading", { level: 1, name: "Backend-utvecklare" }),
    ).toBeInTheDocument();
    expect(screen.getByText("Volvo")).toHaveClass("jp-modal__company");
  });

  it("frames the body in the page modifier of the modal panel, as its skeleton does", async () => {
    getApplicationById.mockResolvedValue({ kind: "ok", data: makeDetail() });
    const { container } = await renderPage();

    const panel = container.querySelector(".jp-container.jp-page .jp-modal");
    expect(panel).toHaveClass("jp-modal--page");
    expect(panel).not.toHaveAttribute("style");
  });

  it("renders the interactive body, not a separate status form", async () => {
    getApplicationById.mockResolvedValue({ kind: "ok", data: makeDetail() });
    const { container } = await renderPage();

    expect(container.querySelector(".jp-status-block__value")).toHaveTextContent(
      "Skickad",
    );
    expect(
      screen.getByRole("button", { name: "Flytta till Bekräftad" }),
    ).toBeInTheDocument();
    expect(container.querySelectorAll(".jp-steppicker__step")).toHaveLength(7);
    expect(screen.queryByRole("radiogroup")).not.toBeInTheDocument();
  });

  it("puts the open forms' headings one level below the h1", async () => {
    getApplicationById.mockResolvedValue({ kind: "ok", data: makeDetail() });
    await renderPage();
    fireEvent.click(screen.getByRole("button", { name: "Planera uppföljning" }));
    fireEvent.click(screen.getByRole("button", { name: "Lägg till anteckning" }));

    expect(
      screen.getByRole("heading", { level: 2, name: "Planera uppföljning" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { level: 2, name: "Lägg till anteckning" }),
    ).toBeInTheDocument();
  });

  it("keeps the back link above and puts delete and back in its foot", async () => {
    getApplicationById.mockResolvedValue({ kind: "ok", data: makeDetail() });
    const { container } = await renderPage();

    expect(
      screen.getByRole("link", { name: "Tillbaka till ansökningar" }),
    ).toHaveAttribute("href", "/ansokningar");
    const foot = container.querySelector<HTMLElement>(".jp-modal__foot");
    expect(foot).not.toBeNull();
    expect(
      within(foot!).getByRole("button", { name: "Radera ansökan" }),
    ).toBeInTheDocument();
    expect(within(foot!).getByRole("link", { name: "Tillbaka" })).toHaveAttribute(
      "href",
      "/ansokningar",
    );
  });

  it("marks an erased ad in the subtitle, as the modal does", async () => {
    getApplicationById.mockResolvedValue({
      kind: "ok",
      data: makeDetail({
        jobAd: { ...makeDetail().jobAd!, status: "Erased" },
        preservedAd: snapshot,
      }),
    });
    await renderPage();

    expect(
      screen.getByText("Volvo · Annonsen är borttagen"),
    ).toHaveClass("jp-modal__company");
  });

  it("renders the fallback title in sans when there is no ad row", async () => {
    getApplicationById.mockResolvedValue({
      kind: "ok",
      data: makeDetail({ jobAd: null, jobAdId: null, preservedAd: null }),
    });
    await renderPage();

    const heading = screen.getByRole("heading", {
      level: 1,
      name: "Ansökan #aaaaaaaa",
    });
    expect(heading).not.toHaveClass("jp-mono");
    expect(screen.getByText("Skapad 1 maj 2026")).toBeInTheDocument();
  });

  // #1827 M6: a failed read keeps the way back, and every failure renders the one error block.
  it("a rate-limited read shows the error block under the back link", async () => {
    getApplicationById.mockResolvedValue({
      kind: "rateLimited",
      retryAfterSeconds: 30,
    });
    await renderPage();

    expect(
      screen.getByRole("link", { name: "Tillbaka till ansökningar" }),
    ).toHaveAttribute("href", "/ansokningar");
    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent("För många förfrågningar");
    expect(alert).toHaveTextContent("Försök igen om 30 sekunder.");
  });

  it("a failed read shows the same error block under the back link", async () => {
    getApplicationById.mockResolvedValue({ kind: "error" });
    await renderPage();

    expect(
      screen.getByRole("link", { name: "Tillbaka till ansökningar" }),
    ).toHaveAttribute("href", "/ansokningar");
    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent("Kunde inte ladda ansökan");
    expect(alert).toHaveTextContent(
      "Ett tekniskt fel uppstod. Försök ladda om sidan om en stund.",
    );
  });
});

import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createTranslator } from "next-intl";
import svPages from "../../../../../../messages/sv/pages.json";
import svJobads from "../../../../../../messages/sv/jobads.json";
import type { JobAdDto } from "@/lib/dto/job-ads";
import NyaFollowedAdsPage from "./page";

/**
 * `/foretag/bevakade/nya` (#1576) — the destination the Översikt count links to.
 *
 * <para/> The load-bearing assertion here is the ACKNOWLEDGEMENT. `senior-cto-advisor` measured that
 * after this delta `JobSeeker.LastSeenFollowedAdsAt` has exactly ONE production writer left — this
 * page — and that the Testcontainers suite cannot see it: `FollowedCompanyAdRailTests` exercises the
 * endpoint, never the call site. If the call disappears, the Översikt notice becomes permanently
 * un-resettable and grows without bound, and every backend test stays green. So `after()` is mocked
 * to RUN its callback rather than swallow it (the sibling `jobb-results.test.tsx` swallows it, which
 * is why the same hole is open there).
 */

const getServerSession = vi.fn();
const getSessionId = vi.fn();
const getNewFollowedCompanyAds = vi.fn();
const markFollowedAdsSeen = vi.fn();

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: string) =>
    createTranslator({
      locale: "sv",
      messages: { pages: svPages, jobads: svJobads },
      namespace: namespace as "pages" | "jobads.companyWatches" | undefined,
    }),
}));

vi.mock("next/navigation", () => ({
  redirect: (url: string) => {
    throw new Error(`NEXT_REDIRECT:${url}`);
  },
}));

// Runs the callback, deliberately: this is the seam the acknowledgement rides.
vi.mock("next/server", () => ({ after: (cb: () => void) => cb() }));

vi.mock("@/lib/auth/session", () => ({
  getServerSession: () => getServerSession(),
  getSessionId: () => getSessionId(),
}));

vi.mock("@/lib/api/company-follows", () => ({
  getNewFollowedCompanyAds: () => getNewFollowedCompanyAds(),
  markFollowedAdsSeen: (...a: unknown[]) => markFollowedAdsSeen(...a),
}));

// Chrome with next-intl/router needs of its own; says nothing about what is measured here.
vi.mock("@/components/foretag/foretag-pagehero", () => ({
  ForetagPagehero: () => null,
}));
vi.mock("@/components/foretag/foretag-subnav", () => ({
  ForetagSubnav: () => null,
}));
vi.mock("@/components/common/info-dialog", () => ({ InfoDialog: () => null }));

function ad(id: string, title: string): JobAdDto {
  return {
    id,
    title,
    companyName: "Volvo AB",
    url: "https://example.test/ad",
    source: "Platsbanken",
    status: "Active",
    publishedAt: "2026-08-30T10:00:00Z",
    expiresAt: null,
    createdAt: "2026-08-30T10:00:00Z",
  };
}

const WINDOW = "2026-08-31T08:15:00.123456Z";

function okResult(
  rows: Array<{ ad: JobAdDto; matchesYou: boolean | null }>,
  overrides: Partial<{
    matchingAssessed: boolean;
    acknowledgedThrough: string | null;
    truncated: boolean;
  }> = {}
) {
  // Every field the server carries is an INPUT here, never re-derived from the rows. The default
  // for `matchingAssessed` matches what production produces (the flag and the rows agree by
  // construction), but a spec that needs them apart has to be able to say so - otherwise `some`
  // and `every` coincide in every fixture and the page could rebuild the flag client-side with
  // nothing failing.
  return {
    kind: "ok" as const,
    data: {
      rows,
      matchingAssessed: rows.every((r) => r.matchesYou !== null),
      acknowledgedThrough: WINDOW as string | null,
      truncated: false,
      ...overrides,
    },
  };
}

async function renderPage() {
  render(await NyaFollowedAdsPage());
}

describe("/foretag/bevakade/nya", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getServerSession.mockResolvedValue({ id: "u1" });
    getSessionId.mockResolvedValue("session-abc");
  });

  it("acknowledges the window the server computed, verbatim and with the session", async () => {
    getNewFollowedCompanyAds.mockResolvedValue(
      okResult([{ ad: ad("a1", "Systemutvecklare"), matchesYou: true }])
    );

    await renderPage();

    expect(markFollowedAdsSeen).toHaveBeenCalledTimes(1);
    // Verbatim: the ORIGINAL ISO string, full precision. Re-serialising through Date would truncate
    // the microseconds and hand back a value BELOW the hit it acknowledges.
    expect(markFollowedAdsSeen).toHaveBeenCalledWith(WINDOW, "session-abc");
  });

  it("does not acknowledge when the read failed", async () => {
    getNewFollowedCompanyAds.mockResolvedValue({ kind: "error" as const });

    await renderPage();

    // Without a coherent baseline the watermark must not move: a transient error that silently
    // zeroed the count would destroy the signal the user never got to see.
    expect(markFollowedAdsSeen).not.toHaveBeenCalled();
  });

  it("does not acknowledge when there is nothing to acknowledge", async () => {
    getNewFollowedCompanyAds.mockResolvedValue({
      kind: "ok" as const,
      data: { rows: [], matchingAssessed: false, acknowledgedThrough: null, truncated: false },
    });

    await renderPage();

    expect(markFollowedAdsSeen).not.toHaveBeenCalled();
  });

  it("defaults to the whole set, so a render without client JS shows every new ad", async () => {
    getNewFollowedCompanyAds.mockResolvedValue(
      okResult([
        { ad: ad("a1", "Systemutvecklare"), matchesYou: true },
        { ad: ad("a2", "Lagerarbetare"), matchesYou: false },
      ])
    );

    await renderPage();

    expect(screen.getByRole("heading", { name: "Systemutvecklare" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Lagerarbetare" })).toBeInTheDocument();
  });


  // UNREACHABLE STATE, declared. `matchingAssessed: false` beside a `matchesYou: true` row is a
  // shape src/ cannot produce: ListNewFollowedCompanyAdsQueryHandler writes
  // `resolution.Assessed ? matching.Contains(id) : null`, so the flag and the rows agree by
  // construction. Per AGENTS.md §5 Tests: a declared-unreachable state may assert only that the
  // READ side degrades safely - here, that the page believes the field the server carried rather
  // than second-guessing it from the rows. It asserts nothing about what production does.
  it("believes the carried assessability flag rather than re-deriving it from the rows", async () => {
    getNewFollowedCompanyAds.mockResolvedValue(
      okResult([{ ad: ad("a1", "Systemutvecklare"), matchesYou: true }], {
        matchingAssessed: false,
      })
    );

    await renderPage();

    expect(screen.getByText("1 ny annons sedan ditt senaste besök.")).toBeInTheDocument();
    expect(screen.getByText(/inte angett vilka yrken/)).toBeInTheDocument();
  });

  // The degenerate cap arm: rows on screen, but the server declined to name a window. Sending an
  // empty body here would let the backend fall back to clock-now and swallow every hit beyond the
  // cap for good - and the mutation that does it compiles and keeps every other spec green.
  it("does not acknowledge when the server named no window, even with rows on screen", async () => {
    getNewFollowedCompanyAds.mockResolvedValue(
      okResult([{ ad: ad("a1", "Systemutvecklare"), matchesYou: true }], {
        acknowledgedThrough: null,
        truncated: true,
      })
    );

    await renderPage();

    expect(screen.getByRole("heading", { name: "Systemutvecklare" })).toBeInTheDocument();
    expect(markFollowedAdsSeen).not.toHaveBeenCalled();
  });
  it("filters the matching arm in the browser, without a second read", async () => {
    getNewFollowedCompanyAds.mockResolvedValue(
      okResult([
        { ad: ad("a1", "Systemutvecklare"), matchesYou: true },
        { ad: ad("a2", "Lagerarbetare"), matchesYou: false },
      ])
    );

    await renderPage();
    await userEvent.click(screen.getByRole("radio", { name: "Matchande annonser" }));

    // The load-bearing half: arriving MOVED the watermark in `after()`, and the read is
    // `CreatedAt > lastSeen`. A navigation-driven arm would issue a SECOND read against the moved
    // watermark and land on the empty state, so the arm must never cost a read.
    expect(getNewFollowedCompanyAds).toHaveBeenCalledTimes(1);
    expect(screen.getByRole("heading", { name: "Systemutvecklare" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Lagerarbetare" })).not.toBeInTheDocument();
    // The filtered state is declared, never left to be inferred from a shorter list.
    expect(screen.getByText("Filtrerat: endast matchande annonser")).toBeInTheDocument();
  });

  it("states both numbers in the matching arm, over the whole set and not the view", async () => {
    getNewFollowedCompanyAds.mockResolvedValue(
      okResult([
        { ad: ad("a1", "Systemutvecklare"), matchesYou: true },
        { ad: ad("a2", "Lagerarbetare"), matchesYou: false },
      ])
    );

    await renderPage();
    await userEvent.click(screen.getByRole("radio", { name: "Matchande annonser" }));

    // Arriving here IS the acknowledgement, so the surface has to show what it acknowledged — the
    // filtered view shows one row, but the sentence must still say two.
    expect(
      screen.getByText("2 nya annonser sedan ditt senaste besök, varav 1 matchar dig.")
    ).toBeInTheDocument();
  });

  it("names its own empty matching arm rather than the list's absent filters", async () => {
    getNewFollowedCompanyAds.mockResolvedValue(
      okResult([{ ad: ad("a1", "Lagerarbetare"), matchesYou: false }])
    );

    await renderPage();
    await userEvent.click(screen.getByRole("radio", { name: "Matchande annonser" }));

    // `JobAdList`'s own empty body names filters and a search box this surface does not have.
    expect(screen.queryByText(/töm sökrutan/)).not.toBeInTheDocument();
    expect(
      screen.getByText("Ingen av de nya annonserna matchar din profil")
    ).toBeInTheDocument();

    // And the way back out of the arm is an affordance, not a hint.
    await userEvent.click(screen.getByRole("button", { name: "Visa alla nya annonser" }));
    expect(screen.getByRole("heading", { name: "Lagerarbetare" })).toBeInTheDocument();
  });

  it("claims no total when the read was truncated", async () => {
    getNewFollowedCompanyAds.mockResolvedValue(
      okResult(
        [
          { ad: ad("a1", "Systemutvecklare"), matchesYou: true },
          { ad: ad("a2", "Lagerarbetare"), matchesYou: false },
        ],
        { truncated: true }
      )
    );

    await renderPage();

    // `rows.length` is the CAPPED count. Rendering it as "N nya annonser sedan ditt senaste besök"
    // asserts a total the page never read, and Översikt's uncapped count would contradict it
    // (ADR 0120: a rendered number is true or absent).
    expect(
      screen.queryByText("2 nya annonser sedan ditt senaste besök, varav 1 matchar dig.")
    ).not.toBeInTheDocument();
    expect(
      screen.getByText("Visar 2 nya annonser, varav 1 matchar dig.")
    ).toBeInTheDocument();
  });

  it("discloses the consumption while the ads are still on screen", async () => {
    getNewFollowedCompanyAds.mockResolvedValue(
      okResult([{ ad: ad("a1", "Systemutvecklare"), matchesYou: true }])
    );

    await renderPage();

    // The set is consumed by arriving, so a reload shows the empty state. Discovering that AFTER
    // the ads are gone is the failure this sentence exists to prevent.
    expect(
      screen.getByText(/Nästa gång visas bara det som tillkommit sedan dess/)
    ).toBeInTheDocument();
  });

  it("says matching is not assessed rather than fabricating a zero", async () => {
    getNewFollowedCompanyAds.mockResolvedValue(
      okResult([{ ad: ad("a1", "Systemutvecklare"), matchesYou: null }])
    );

    await renderPage();

    expect(
      screen.getByText("1 ny annons sedan ditt senaste besök.")
    ).toBeInTheDocument();
    expect(screen.getByText(/inte angett vilka yrken/)).toBeInTheDocument();
    // No matching arm to offer when the predicate is inert.
    expect(screen.queryByRole("radiogroup")).not.toBeInTheDocument();
  });
});

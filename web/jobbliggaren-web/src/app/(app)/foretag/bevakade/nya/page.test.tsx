import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
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

function okResult(rows: Array<{ ad: JobAdDto; matchesYou: boolean | null }>) {
  // The server carries assessability as a page-global fact, so the fixture does too rather than
  // re-deriving it from the rows - deriving it here would hide the very ambiguity the field closes.
  const matchingAssessed = rows.every((r) => r.matchesYou !== null);
  return {
    kind: "ok" as const,
    data: { rows, matchingAssessed, acknowledgedThrough: WINDOW, truncated: false },
  };
}

async function renderPage(params: Record<string, string> = {}) {
  render(
    await NyaFollowedAdsPage({ searchParams: Promise.resolve(params) })
  );
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

  it("filters the matching arm from rows already fetched, without a second read", async () => {
    getNewFollowedCompanyAds.mockResolvedValue(
      okResult([
        { ad: ad("a1", "Systemutvecklare"), matchesYou: true },
        { ad: ad("a2", "Lagerarbetare"), matchesYou: false },
      ])
    );

    await renderPage({ matchande: "on" });

    expect(getNewFollowedCompanyAds).toHaveBeenCalledTimes(1);
    expect(screen.getByRole("heading", { name: "Systemutvecklare" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Lagerarbetare" })).not.toBeInTheDocument();
  });

  it("states both numbers in the matching arm, over the whole set and not the view", async () => {
    getNewFollowedCompanyAds.mockResolvedValue(
      okResult([
        { ad: ad("a1", "Systemutvecklare"), matchesYou: true },
        { ad: ad("a2", "Lagerarbetare"), matchesYou: false },
      ])
    );

    await renderPage({ matchande: "on" });

    // Arriving here IS the acknowledgement, so the surface has to show what it acknowledged — the
    // filtered view shows one row, but the sentence must still say two.
    expect(
      screen.getByText("2 nya annonser sedan ditt senaste besök, varav 1 matchar dig.")
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
    expect(
      screen.queryByRole("link", { name: "Visa bara de som matchar mig" })
    ).not.toBeInTheDocument();
  });
});

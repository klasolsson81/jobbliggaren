import { describe, it, expect, vi, beforeEach } from "vitest";
import { render } from "@testing-library/react";
import { createTranslator, createFormatter } from "next-intl";
import svJobads from "../../../messages/sv/jobads.json";
import { Announcer } from "@/components/common/announcer";
import type { JobAdDto } from "@/lib/dto/job-ads";

/**
 * #1505 — the END of the load, announced. Sibling of `/foretag/sok`'s block in
 * `foretag-sok-results.test.tsx` (#1092), against the same criterion.
 *
 * These render inside `Announcer`, which is how `page.tsx` composes them. Without the wrapper
 * `Announce` is inert by design, so the assertions below would pass vacuously against a results
 * tree that announced nothing — the wrapper is the production shape, not test scaffolding.
 *
 * FOUR cases because `JobbResults` has four branches that render, and PR #1504's review found that
 * closing only the happy ones breaks the PR's own rule: a start that is never closed leaves a
 * screen reader waiting on a load that has in fact finished. `unauthorized` is the fifth branch and
 * is deliberately absent — it `redirect()`s before rendering, so it presents no status message for
 * 4.1.3 to bite on.
 */

const getJobAds = vi.fn();
const getJobAdStatusBatch = vi.fn();
const getJobAdMatchTags = vi.fn();
const getEmployerApplicationCounts = vi.fn();
const getFollowedJobAdIds = vi.fn();
const getMyProfile = vi.fn();
const getJobsWatermark = vi.fn();
const markJobsSeen = vi.fn();
const resolveTaxonomyLabels = vi.fn();
const getSessionId = vi.fn();

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: string) =>
    createTranslator({
      locale: "sv",
      messages: { jobads: svJobads },
      namespace: namespace as "jobads.ui" | undefined,
    }),
  // The real formatter for `sv`, so the announced number carries the same non-breaking group
  // separator production emits. A stub returning `String(n)` would let a formatting regression
  // through the one assertion written to catch it.
  getFormatter: async () => createFormatter({ locale: "sv" }),
}));

// `after()` schedules the watermark write; it is irrelevant to what is announced, and running it
// would pull a fetch into the assertion path.
vi.mock("next/server", () => ({ after: () => undefined }));
vi.mock("next/navigation", () => ({
  redirect: (url: string) => {
    throw new Error(`NEXT_REDIRECT:${url}`);
  },
}));

vi.mock("@/lib/auth/session", () => ({ getSessionId: () => getSessionId() }));
vi.mock("@/lib/api/job-ads", () => ({ getJobAds: (...a: unknown[]) => getJobAds(...a) }));
vi.mock("@/lib/api/job-ad-status", () => ({
  getJobAdStatusBatch: (...a: unknown[]) => getJobAdStatusBatch(...a),
}));
vi.mock("@/lib/api/job-ad-match", () => ({
  getJobAdMatchTags: (...a: unknown[]) => getJobAdMatchTags(...a),
}));
vi.mock("@/lib/api/employer-application-counts", () => ({
  getEmployerApplicationCounts: (...a: unknown[]) => getEmployerApplicationCounts(...a),
}));
vi.mock("@/lib/api/company-follows", () => ({
  getFollowedJobAdIds: (...a: unknown[]) => getFollowedJobAdIds(...a),
}));
vi.mock("@/lib/api/me", () => ({ getMyProfile: () => getMyProfile() }));
vi.mock("@/lib/api/me-jobs", () => ({
  getJobsWatermark: () => getJobsWatermark(),
  markJobsSeen: (...a: unknown[]) => markJobsSeen(...a),
}));
vi.mock("@/lib/api/taxonomy", () => ({
  resolveTaxonomyLabels: (...a: unknown[]) => resolveTaxonomyLabels(...a),
}));

// The toolbar is a client island needing router context, and it renders the count VISUALLY. What
// is measured here is the ANNOUNCED sentence, which `jobb-results.tsx` owns — so the island is
// stubbed rather than mounted. Its own visual rendering is covered by `jobb-results-toolbar.test.tsx`.
vi.mock("@/components/job-ads/jobb-results-toolbar", () => ({
  JobbResultsToolbar: () => null,
}));
vi.mock("@/components/job-ads/job-ad-list", () => ({ JobAdList: () => null }));
vi.mock("@/components/job-ads/job-ad-pagination", () => ({ JobAdPagination: () => null }));

const { JobbResults } = await import("./jobb-results");

const sampleAd = (id: string): JobAdDto => ({
  id,
  title: `Annons ${id}`,
  companyName: "Acme AB",
  url: `https://example.com/jobb/${id}`,
  source: "Platsbanken",
  status: "Active",
  publishedAt: "2026-04-01T08:00:00Z",
  expiresAt: null,
  createdAt: "2026-04-01T08:01:00Z",
});

const announced = () =>
  document.querySelector('p[role="status"][aria-live="polite"]');

const renderHosted = async () =>
  render(
    <Announcer>
      {await JobbResults({
        page: 1,
        pageSize: 20,
        sortBy: "PublishedAtDesc",
        occupationGroup: [],
        region: [],
        municipality: [],
        remote: false,
        employmentType: [],
        worktimeExtent: [],
        matchGrades: [],
        matchningOff: false,
        includeRelated: false,
        hideApplied: false,
        onlyMatched: false,
        employer: undefined,
        q: "",
        commit: false,
        rawParams: {},
      })}
    </Announcer>,
  );

describe("JobbResults — the load's completion reaches the surface region", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getMyProfile.mockResolvedValue({ kind: "error" });
    getJobsWatermark.mockResolvedValue({ kind: "error" });
    resolveTaxonomyLabels.mockResolvedValue({ kind: "error" });
    // Each stub returns the shape its REAL adapter emits on the degraded path — `EMPTY_BATCH` in
    // `job-ad-status.ts` / `employer-application-counts.ts`, `{ entries: {} }` in `job-ad-match.ts`,
    // `[]` in `company-follows.ts`. A convenience shape production never produces would make every
    // assertion below rest on a premise the app cannot reach (§5 `Tests:`), and `{}` for the
    // count batch did exactly that — it threw in `Object.entries` on the real read.
    getJobAdStatusBatch.mockResolvedValue({ savedIds: [], appliedIds: [] });
    getJobAdMatchTags.mockResolvedValue({ entries: {} });
    getEmployerApplicationCounts.mockResolvedValue({ countsByJobAdId: {} });
    getFollowedJobAdIds.mockResolvedValue([]);
    getSessionId.mockResolvedValue(null);
  });

  it("a SEARCH announces the count — W3C's '18 results returned'", async () => {
    getJobAds.mockResolvedValue({
      kind: "ok",
      data: { items: [sampleAd("a1")], page: 1, pageSize: 20, totalCount: 1234 },
    });
    await renderHosted();

    // The grouped form, not "1234": the announced number is formatted by the same helper the
    // toolbar renders with, so the two can never diverge.
    expect(announced()).toHaveTextContent("1 234 träffar");
  });

  it("ZERO matches announce the empty statement — W3C's 'No results returned'", async () => {
    getJobAds.mockResolvedValue({
      kind: "ok",
      data: { items: [], page: 1, pageSize: 20, totalCount: 0 },
    });
    await renderHosted();

    // Title AND body — `design-reviewer` Major 1: the title states the dead end, only the body
    // gives the way out, and the two error branches in this same switch already announce both.
    expect(announced()).toHaveTextContent("Inga jobb hittades");
    expect(announced()).toHaveTextContent(
      "Justera filtren eller töm sökrutan",
    );
    // Not a stray zero: the empty arm never announces "0 träffar".
    expect(announced()).not.toHaveTextContent(/\d/);
  });

  it("a RATE LIMIT announces cause AND remedy, not cause alone", async () => {
    getJobAds.mockResolvedValue({ kind: "rateLimited", retryAfterSeconds: 42 });
    await renderHosted();

    // The title alone says a load failed; only the body says what to do about it, and on a rate
    // limit the natural guess — retry now — extends the block. Both halves or neither.
    expect(announced()).toHaveTextContent("För många förfrågningar");
    expect(announced()).toHaveTextContent("42 sekunder");
    // `design-reviewer` Major 3 — the card carries NO `role="alert"`. It used to, and the two
    // channels then said the same sentence twice, one of them interrupting: an alert node inserted
    // into a live DOM with its text already in place is the case AT does announce. `Announce` is
    // the single path. Parity with `foretag-sok-results.test.tsx`, and measured there: without
    // this assertion, restoring the role kills no test.
    expect(document.querySelector('[role="alert"]')).toBeNull();
  });

  it("a TECHNICAL ERROR announces too, where it previously reached a screen reader through nothing", async () => {
    // This card carries no role at all — not even the rate-limit branch's `role="alert"`.
    getJobAds.mockResolvedValue({ kind: "error" });
    await renderHosted();

    expect(announced()).toHaveTextContent("Kunde inte ladda jobbannonser");
    expect(announced()).toHaveTextContent("Försök ladda om sidan");
  });
});

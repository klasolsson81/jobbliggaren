import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { createTranslator } from "next-intl";
import svPages from "../../../../messages/sv/pages.json";
import { JobAdList } from "@/components/job-ads/job-ad-list";
import type { JobAdDto } from "@/lib/dto/job-ads";
import JobbPage from "./page";

/**
 * /jobb — the heading outline (#1395, sibling of #1383 on `/cv`).
 *
 * `heading-order` is an axe BEST-PRACTICE rule, not `wcag2a`/`wcag2aa`, so a WCAG-tagged run
 * cannot fail on it and reported 0 violations the whole time the skip was live. The pin has
 * to be a test.
 *
 * ⚠ SCOPE, and the two seams it needs.
 *
 * `JobbResults` is an async Server Component: React refuses to render one into a client root,
 * so the page cannot be mounted with it in the tree. It is replaced here by a sync component
 * rendering the REAL `JobAdList` with real `JobAdDto`s — the same component production renders
 * inside that same section, from `getJobAds`'s items. The seam does drop two of that branch's
 * children, `JobbResultsToolbar` and `JobAdPagination`; what makes it faithful on the axis
 * measured here is that neither emits a heading, so `JobAdCard` is the whole results subtree's
 * only heading producer either way. The h3 levels below are therefore the production card's
 * own, and the wrong-fix mutation (promoting `.jp-job__title` to h2) is caught by the very
 * assertions that catch the missing h2.
 *
 * The hero islands are mocked because they are `"use client"` and need router context that
 * says nothing about the outline. That they render no heading is not assumed here — it is a
 * DOCUMENT-level property, measured live by an axe `best-practice` run on the authed page,
 * the same division of labour `/cv`'s suite draws. The live document also carries the site
 * footer's h2s AFTER the cards; a jump back up is never a skip, so the two readings agree.
 */

const redirect = vi.fn();
const getServerSession = vi.fn();
const getMyProfile = vi.fn();
const getRecentSearches = vi.fn();
const getSavedJobAds = vi.fn();
const getTaxonomyTree = vi.fn();

vi.mock("next-intl/server", () => ({
  getTranslations: async (namespace?: string) =>
    createTranslator({
      locale: "sv",
      messages: { pages: svPages },
      namespace: namespace as "pages" | undefined,
    }),
}));

vi.mock("next/navigation", () => ({
  redirect: (url: string) => {
    redirect(url);
    throw new Error(`NEXT_REDIRECT:${url}`);
  },
}));

vi.mock("@/lib/auth/session", () => ({
  getServerSession: () => getServerSession(),
}));
vi.mock("@/lib/api/me", () => ({ getMyProfile: () => getMyProfile() }));
vi.mock("@/lib/api/recent-searches", () => ({
  getRecentSearches: () => getRecentSearches(),
}));
vi.mock("@/lib/api/saved-job-ads", () => ({
  getSavedJobAds: () => getSavedJobAds(),
}));
vi.mock("@/lib/api/taxonomy", () => ({
  getTaxonomyTree: () => getTaxonomyTree(),
}));

// Hero furniture: client islands with router/context needs, no heading of their own.
vi.mock("@/components/job-ads/jobb-hero-search", () => ({
  JobbHeroSearch: () => null,
}));
vi.mock("@/components/job-ads/jobb-hero-filters", () => ({
  JobbHeroFilters: () => null,
}));
vi.mock("@/components/recent-searches/recent-searches-hero-chip", () => ({
  RecentSearchesHeroChip: () => null,
}));
vi.mock("@/components/saved-job-ads/saved-job-ads-hero-chip", () => ({
  SavedJobAdsHeroChip: () => null,
}));
vi.mock("@/components/job-ads/strip-commit-param", () => ({
  StripCommitParam: () => null,
}));

// The async-RSC seam described above. `ads` is set per test.
let ads: JobAdDto[] = [];
vi.mock("@/components/job-ads/jobb-results", () => ({
  JobbResults: () => <JobAdList jobAds={ads} />,
}));

// The h3's accessible name is the title and nothing else, which is what `getByRole("heading",
// { name })` below depends on: `JobAdList` is rendered with no `newIdSet`/`savedIdSet`/
// `appliedIdSet`/`followedIdSet`/`matchGradeById`, so `JobTags` returns null outright and no
// `MatchChip` renders. `publishedAt` is inert here — it renders in `.jp-job__meta`, outside the
// h3. (The freshness tag that used to make the date load-bearing was removed 2026-07-21,
// #1000-review; `job-ad-list.test.tsx` carried the same stale note and is corrected with this.)
const sampleAd = (id: string, title: string): JobAdDto => ({
  id,
  title,
  companyName: "Acme AB",
  url: `https://example.com/jobb/${id}`,
  source: "Platsbanken",
  status: "Active",
  publishedAt: "2026-04-01T08:00:00Z",
  expiresAt: null,
  createdAt: "2026-04-01T08:01:00Z",
});

/** Tag-derived levels in DOM order. An ARIA-only heading would yield NaN and pass every
 *  comparison in `firstSkip` silently, so this fails loud rather than measuring nothing. */
function outline(): number[] {
  return screen.getAllByRole("heading").map((el) => {
    const level = Number(el.tagName.slice(1));
    if (!Number.isInteger(level)) {
      throw new Error(`outline(): <${el.tagName}> has no tag-derived heading level`);
    }
    return level;
  });
}

/** The first skipped level, as a readable string — or null when the outline is sound.
 *  A fold, not an index walk: `noUncheckedIndexedAccess` types `levels[i]` as possibly
 *  undefined, and the obvious per-pair guard skips the comparison rather than making it. */
function firstSkip(levels: number[]): string | null {
  const [first, ...rest] = levels;
  if (first === undefined) return null;
  let prev = first;
  for (const [i, here] of rest.entries()) {
    if (here > prev + 1) return `h${prev} -> h${here} at position ${i + 1}`;
    prev = here;
  }
  return null;
}

const renderPage = async (params: Record<string, string> = {}) =>
  render(await JobbPage({ searchParams: Promise.resolve(params) }));

describe("/jobb — the heading outline skips no level (WCAG 1.3.1, #1395)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    ads = [];
    getServerSession.mockResolvedValue({ id: "u1", email: "u@example.test" });
    getMyProfile.mockResolvedValue({
      kind: "ok",
      data: { hasStatedDesiredOccupation: false },
    });
    getRecentSearches.mockResolvedValue({ kind: "ok", data: [] });
    getSavedJobAds.mockResolvedValue({ kind: "ok", data: [] });
    getTaxonomyTree.mockResolvedValue({ kind: "ok", data: null });
  });

  it("goes h1 -> h2 -> h3 when result cards render, and the h2 is the region's own", async () => {
    ads = [sampleAd("a1", "Backend-utvecklare"), sampleAd("a2", "Frontend-utvecklare")];

    await renderPage();

    expect(firstSkip(outline())).toBeNull();
    // The levels are NAMED, not merely counted. A bare skip check also passes the WRONG fix,
    // where the card title is promoted to h2 and the outline reads [1,2,2] with no skip in it.
    expect(screen.getByRole("heading", { level: 1, name: "Sök jobb" })).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { level: 2, name: "Lediga jobb" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { level: 3, name: "Backend-utvecklare" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { level: 3, name: "Frontend-utvecklare" }),
    ).toBeInTheDocument();
  });

  it("names the results region, so the h2 is a landmark label and not loose text", async () => {
    ads = [sampleAd("a1", "Backend-utvecklare")];

    await renderPage();

    // By ROLE AND NAME, never by the attribute: a selector for `[aria-labelledby="x"]` passes
    // even when the id it points at is gone — and that is exactly the state where the region
    // loses its name and stops being a landmark.
    expect(screen.getByRole("region", { name: "Lediga jobb" })).toBeInTheDocument();
  });

  it("keeps the region labelled when the search returns nothing", async () => {
    // The empty state replaces the CARDS, not the region: the count line and the sort control
    // still render, so the heading that names them must not disappear with the results.
    // (`.jp-empty__title` stays a div — visually a heading, but the class is shared across
    // every empty state in the tree, so promoting it here would be a fix in one place out of N.)
    ads = [];

    await renderPage();

    expect(firstSkip(outline())).toBeNull();
    // Positive first: `firstSkip([])` is null, so a page rendering no headings at all would
    // pass that line on its own.
    expect(screen.getByRole("heading", { level: 1, name: "Sök jobb" })).toBeInTheDocument();
    expect(
      screen.getByRole("heading", { level: 2, name: "Lediga jobb" }),
    ).toBeInTheDocument();
    expect(screen.getByText("Inga jobb hittades")).toBeInTheDocument();
  });

  it("states the heading INVARIANTLY — never the count, never the search term", async () => {
    // `foretag-sok-results.tsx`'s written rule, and here it is also what lets the heading sit
    // OUTSIDE the Suspense boundary: a count is data-bound and could not render before the
    // results land. A heading that grew a number would silently move back inside, and the
    // band `loading.tsx` reserves would stop matching.
    //
    // TWO axes, because only one of them has a reachable mutant. The result set is the axis
    // the rule is written about, but the heading receives no result data at all, so nothing
    // short of moving it inside the boundary can make it vary there. `searchParams` IS in
    // scope here, which makes "Lediga jobb för sjuksköterska" the regression this surface can
    // actually grow — so the term is varied too, and neither reading may move.
    ads = [sampleAd("a1", "Backend-utvecklare"), sampleAd("a2", "Frontend-utvecklare")];
    const { unmount: unmountA } = await renderPage();
    const withResults = screen.getByRole("heading", { level: 2 }).textContent;
    unmountA();

    ads = [];
    const { unmount: unmountB } = await renderPage();
    const withNone = screen.getByRole("heading", { level: 2 }).textContent;
    unmountB();

    ads = [sampleAd("a1", "Backend-utvecklare")];
    await renderPage({ q: "sjuksköterska" });
    const withTerm = screen.getByRole("heading", { level: 2 }).textContent;

    expect(withResults).toBe("Lediga jobb");
    // Every reading is kept: one alone cannot tell "invariant" from "measured the same state
    // twice", which is the same reason the CLS check captures each state separately.
    expect(withNone).toBe(withResults);
    expect(withTerm).toBe(withResults);
  });
});

/**
 * #1505 — the CALL SITE of the live region.
 *
 * `announcer.test.tsx` proves the mechanism works. It cannot prove this page mounts it, and
 * `Announce` is inert without a provider BY DESIGN — so deleting `<Announcer>` from `page.tsx`
 * leaves no type error, no runtime error and every other unit test green while `/jobb` announces
 * nothing at all. `code-reviewer` Major 3 on PR #1504 named this inversion on the sibling surface.
 *
 * What is asserted is PRESENCE and EMPTINESS, not placement relative to the boundary: `<Suspense>`
 * emits no DOM nodes of its own when it does not suspend, so a placement assertion would pass
 * either way and measure nothing (the vacuous pin retracted from PR #1504).
 */
describe("/jobb — the page mounts the live region itself", () => {
  it("renders the region, and renders it EMPTY", async () => {
    ads = [sampleAd("a1", "Backend-utvecklare")];
    const { container } = await renderPage();

    const live = container.querySelector('p[role="status"][aria-live="polite"]');
    expect(live).not.toBeNull();
    expect(live).toHaveAttribute("aria-atomic", "true");
    // Empty at first paint is the half ARIA22 actually requires; a region that arrives holding
    // its sentence is the defect #1505 was filed for.
    expect(live).toHaveTextContent("");
  });
});

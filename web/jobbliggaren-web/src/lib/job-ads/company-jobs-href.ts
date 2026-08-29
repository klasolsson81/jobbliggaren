import { WATCH_MATCHING_GRADES } from "@/lib/dto/job-ad-match";
import { DEFAULT_SORT_BY, buildJobbHref } from "./search-params";

/** Which of a watched company's ads the link should show. */
export type CompanyJobsScope = "all" | "matching";

/**
 * Builds the `/jobb` href that shows one watched company's ads (#1547). Sibling of
 * {@link buildRecentSearchHref} — one source of truth for "a watched company → /jobb URL",
 * so the two links a watch row renders cannot drift from each other or from the counts
 * above them.
 *
 * This is the ONLY originator of an `?employer=` value in the app. `search-params.ts`
 * recorded (2026-08-19) that there were none since `company-lookup.tsx` was deleted in
 * `aca39970`, which is why the `IsProtectedIdentity` gate it describes was guarding an
 * empty set. The gate is live again, and it lives at the CALLER: this function takes a
 * plain org.nr and cannot tell a masked one from a legal-entity one.
 *
 * `scope: "matching"` carries the grade subset, never `?baraMatchade=on`. The two are not
 * interchangeable: `baraMatchade` maps to `onlyMatched`, which
 * `ListJobAdsQueryHandler.cs:122-125` expands to the whole filterable band
 * `[Basic, Related, Good, Strong]` — WIDER than the `[Good, Strong]` the row's count is
 * computed at, so a user clicking "9 matchande" would land on more than nine. The deleted
 * `company-lookup.tsx` linked exactly that way; this half of the precedent is not revived.
 *
 * Residual, and it is the same staleness every count on the page has: the caller gates the
 * matching link on `matchingAdCount !== null`, which is a SERVER-rendered answer to "has
 * this user stated an occupation". Clear the occupations in another tab and the backend
 * ignores the grade subset entirely (`ListJobAdsQueryHandler.cs:110`) and answers with the
 * unfiltered employer list. Not closable without a client round-trip.
 *
 * Every other axis is deliberately empty: no ort, no yrke, no Klass-2 dimension, default
 * sort, and specifically no `matchning=off` — that one would filter the list while hiding
 * every visual trace of the filter (the grade chips render only when matching is active).
 */
export function buildCompanyJobsHref(
  organizationNumber: string,
  scope: CompanyJobsScope
): string {
  return buildJobbHref({
    q: "",
    occupationGroup: [],
    region: [],
    municipality: [],
    employmentType: [],
    worktimeExtent: [],
    matchGrades: scope === "matching" ? WATCH_MATCHING_GRADES : [],
    remote: false,
    employer: organizationNumber,
    sortBy: DEFAULT_SORT_BY,
  });
}

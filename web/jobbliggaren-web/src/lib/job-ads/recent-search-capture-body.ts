import { parseQParam, type JobbUrlState } from "./search-params";

/** The search a capture is asked to store: the axes of a {@link JobbUrlState} that make up its identity. */
export type RecentSearchCaptureInput = Pick<
  JobbUrlState,
  | "q"
  | "occupationGroup"
  | "occupationName"
  | "region"
  | "municipality"
  | "remote"
  | "junior"
  | "employmentType"
  | "worktimeExtent"
  | "employer"
  | "sortBy"
>;

/**
 * #2285 (ADR 0060 Amendment 2026-11-22) — the body of `POST /api/v1/me/recent-searches` for a search: its identity axes
 * and nothing else, in the command's own names (`juniorIt`, `sort`). The list reads the same state through the URL
 * (`buildJobbHref`, then the page's parse), and `recent-search-capture-body.test.ts` pins that the two agree on every
 * axis the capture gate reads, `junior` included.
 */
export function recentSearchCaptureBody(search: RecentSearchCaptureInput) {
  return {
    q: parseQParam(search.q) ?? null,
    occupationGroup: [...search.occupationGroup],
    occupationName: [...search.occupationName],
    region: [...search.region],
    municipality: [...search.municipality],
    employmentType: [...search.employmentType],
    worktimeExtent: [...search.worktimeExtent],
    employer: [...(search.employer ?? [])],
    remote: search.remote,
    juniorIt: search.junior,
    sort: search.sortBy,
  };
}

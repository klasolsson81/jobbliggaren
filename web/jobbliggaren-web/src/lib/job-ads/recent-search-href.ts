import { buildJobbHref } from "./search-params";
import type { RecentJobSearchDto } from "@/lib/dto/recent-searches";

/**
 * Builds the `/jobb` href that re-runs a captured RecentJobSearch (replay).
 * Shared by the `/sokningar` row "Kör igen" action and the Översikt saved-search
 * notice (#294) so both replay the exact same way — one source of truth for
 * "recent search → /jobb URL".
 *
 * Klass 2 (ADR 0067 B2): replay carries employmentType + worktimeExtent so the
 * re-run does not silently drop those filters. #1407 closed the same gap on the
 * distans axis — `remote` now rides along, so the row's count and the list its
 * link produces rest on the same criterion. matchGrades is runtime view-state,
 * NOT a saved-search concern (Klas) — a replay therefore never carries a grade
 * filter (empty list). #1471 closed the same gap on the employer axis: `employerList`
 * rides along too, so every axis the count is computed on is one the click reproduces.
 * What that list may carry is decided server-side (`EmployerAxisGate`), never here.
 */
export function buildRecentSearchHref(item: RecentJobSearchDto): string {
  return buildJobbHref({
    q: item.q ?? "",
    occupationGroup: item.occupationGroupList,
    region: item.regionList,
    municipality: item.municipalityList,
    employmentType: item.employmentTypeList,
    worktimeExtent: item.worktimeExtentList,
    employer: item.employerList,
    matchGrades: [],
    remote: item.remote,
    sortBy: item.sortBy,
  });
}

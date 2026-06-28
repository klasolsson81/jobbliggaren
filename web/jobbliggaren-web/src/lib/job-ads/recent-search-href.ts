import { buildJobbHref } from "./search-params";
import type { RecentJobSearchDto } from "@/lib/dto/recent-searches";

/**
 * Builds the `/jobb` href that re-runs a captured RecentJobSearch (replay).
 * Shared by the `/sokningar` row "Kör igen" action and the Översikt saved-search
 * notice (#294) so both replay the exact same way — one source of truth for
 * "recent search → /jobb URL".
 *
 * Klass 2 (ADR 0067 B2): replay carries employmentType + worktimeExtent so the
 * re-run does not silently drop those filters. matchGrades is runtime view-state,
 * NOT a saved-search concern (Klas) — a replay therefore never carries a grade
 * filter (empty list).
 */
export function buildRecentSearchHref(item: RecentJobSearchDto): string {
  return buildJobbHref({
    q: item.q ?? "",
    occupationGroup: item.occupationGroupList,
    region: item.regionList,
    municipality: item.municipalityList,
    employmentType: item.employmentTypeList,
    worktimeExtent: item.worktimeExtentList,
    matchGrades: [],
    sortBy: item.sortBy,
  });
}

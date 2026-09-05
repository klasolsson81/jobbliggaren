/** Which of a criterion's ads the page should show (#1656 (b)). */
export type CriterionAdsScope = "all" | "matching";

const AXIS = "visa";
const MATCHING_VALUE = "matchande";

/**
 * Reads the one axis `/foretag/smarta-bevakningar/[id]/annonser` has. Absence and every unrecognised
 * value mean "all ads" — a filter nobody asked for must never appear.
 *
 * The value is deliberately NOT `baraMatchade`: on `/jobb` that name maps to `onlyMatched`, which
 * `ListJobAdsQueryHandler` expands to the whole filterable band (Grund and Relaterat included) --
 * WIDER than the `>= Good` this route's count is computed at. One word meaning two different sets is
 * how "9 matchande" ends up landing on more than nine, which `company-jobs-href.ts` records at
 * length.
 */
export function parseCriterionAdsScope(
  raw: string | string[] | undefined,
): CriterionAdsScope {
  return raw === MATCHING_VALUE ? "matching" : "all";
}

/**
 * Builds an ads-page href. One builder and one reader for one axis, so no caller can drop it: page 2
 * without the axis would silently show more ads than page 1 promised, and the metadata read would
 * stop being the same request as the page read.
 */
export function buildCriterionAdsHref(
  criterionId: string,
  page: number,
  scope: CriterionAdsScope,
): string {
  const base = `/foretag/smarta-bevakningar/${criterionId}/annonser`;
  const params = new URLSearchParams();
  if (page > 1) params.set("page", String(page));
  if (scope === "matching") params.set(AXIS, MATCHING_VALUE);
  const query = params.toString();
  return query.length > 0 ? `${base}?${query}` : base;
}

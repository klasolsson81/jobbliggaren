import type { JobAdSortBy } from "@/lib/dto/job-ads";

/**
 * Centraliserad searchParams-builder för /jobb (F4). Hero-filter-popovers
 * och result-toolbarens sort-dropdown bygger URL:en HÄR — symmetriskt
 * param-bevarande (samma lärdom som F3 B-FIX: två ytor som skriver samma
 * URL får inte radera varandras params).
 *
 * Kontrakt (ADR 0042 Beslut B, OFÖRÄNDRAT):
 * - `occupationGroup` / `region` = upprepade query-params (conceptId
 *   string[]). `occupationGroup` = ssyk-level-4/yrkesgrupp (ADR 0067 Fas
 *   E2a nivå-skifte; backend tog bort `?ssyk=` i C2).
 * - `q` = hero-sökordet (ägs av hero-GET-formuläret; bärs vidare här så
 *   en filter-/sort-ändring aldrig tappar användarens sökterm).
 * - `sortBy` utelämnas när = default (PublishedAtDesc).
 * - `pageSize` bevaras om explicit satt.
 * - `page` utelämnas ALLTID: filter-/sort-ändring → tillbaka till sida 1
 *   (annars riskerar användaren en sida som inte längre finns).
 */
export interface JobbUrlState {
  q: string;
  occupationGroup: ReadonlyArray<string>;
  region: ReadonlyArray<string>;
  sortBy: JobAdSortBy;
  pageSize?: string;
}

export const DEFAULT_SORT_BY: JobAdSortBy = "PublishedAtDesc";

export function buildJobbHref(state: JobbUrlState): string {
  const params = new URLSearchParams();
  for (const v of state.occupationGroup)
    params.append("occupationGroup", v);
  for (const v of state.region) params.append("region", v);
  const q = state.q.trim();
  if (q.length > 0) params.set("q", q);
  if (state.sortBy !== DEFAULT_SORT_BY) params.set("sortBy", state.sortBy);
  if (state.pageSize) params.set("pageSize", state.pageSize);
  const qs = params.toString();
  return qs.length > 0 ? `/jobb?${qs}` : "/jobb";
}

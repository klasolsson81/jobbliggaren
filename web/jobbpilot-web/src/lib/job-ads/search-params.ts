import type { JobAdSortBy } from "@/lib/dto/job-ads";

/**
 * Centraliserad searchParams-builder för /jobb (F4). Hero-filter-popovers
 * och result-toolbarens sort-dropdown bygger URL:en HÄR — symmetriskt
 * param-bevarande (samma lärdom som F3 B-FIX: två ytor som skriver samma
 * URL får inte radera varandras params).
 *
 * Kontrakt (ADR 0042 Beslut B, OFÖRÄNDRAT):
 * - `occupationGroup` / `region` / `municipality` = upprepade query-params
 *   (conceptId string[]). `occupationGroup` = ssyk-level-4/yrkesgrupp (ADR
 *   0067 Fas E2a nivå-skifte). `municipality` = kommun (Fas E2b — backend
 *   kombinerar region∪municipality som union, ADR 0067 impl-notat E2b).
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
  municipality: ReadonlyArray<string>;
  sortBy: JobAdSortBy;
  pageSize?: string;
}

export const DEFAULT_SORT_BY: JobAdSortBy = "PublishedAtDesc";

/**
 * Fas E2j (ADR 0060 amendment 2026-06-12) — commit-intent-signalen.
 * `commit` är en TRANSIENT signal-param, INTE ett tillstånd: den ingår
 * ALDRIG i `JobbUrlState`, `sameUrlState`, `buildJobbHref` eller
 * `serializeSearchText` (annars bryts spegel-fältets own-roundtrip-detektor
 * + förorenar delningsbara URL:er). Den adderas endast som suffix på
 * commit-punkternas navigering (Enter/Sök/förslags-val/toolbar) och strippas
 * efter mount. Backend (`ICapturesRecentSearch.Commit`) gatar auto-capturen
 * på den.
 */
export const COMMIT_PARAM = "commit";

/** Adderar commit-intent-suffixet på en redan byggd href (utanför state). */
export function withCommitFlag(href: string): string {
  return href.includes("?")
    ? `${href}&${COMMIT_PARAM}=1`
    : `${href}?${COMMIT_PARAM}=1`;
}

export function buildJobbHref(state: JobbUrlState): string {
  const params = new URLSearchParams();
  for (const v of state.occupationGroup)
    params.append("occupationGroup", v);
  for (const v of state.region) params.append("region", v);
  for (const v of state.municipality) params.append("municipality", v);
  const q = state.q.trim();
  if (q.length > 0) params.set("q", q);
  if (state.sortBy !== DEFAULT_SORT_BY) params.set("sortBy", state.sortBy);
  if (state.pageSize) params.set("pageSize", state.pageSize);
  const qs = params.toString();
  return qs.length > 0 ? `/jobb?${qs}` : "/jobb";
}

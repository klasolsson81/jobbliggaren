import "server-only";
import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import {
  jobAdDtoSchema,
  listJobAdsResultSchema,
  suggestJobAdTermsResultSchema,
  facetCountsSchema,
  type JobAdDto,
  type ListJobAdsResult,
  type SuggestJobAdTermsResult,
  type JobAdSortBy,
  type FacetDimension,
  type FacetCounts,
} from "@/lib/dto/job-ads";
import { responseToResult, type ApiResult } from "@/lib/dto/_helpers";

export interface ListJobAdsQuery {
  page: number;
  pageSize: number;
  sortBy: JobAdSortBy;
  // ADR 0042 Beslut B — multi: skickas som upprepad query-string
  // (?occupationGroup=a&occupationGroup=b). ASP.NET Core minimal API binder
  // till string[]. ADR 0067 Fas E2a: yrke-filtret är yrkesgrupp (ssyk-
  // level-4). Fas E2b: ?municipality= (kommun) — backend unionerar
  // region∪municipality (Ort = en dimension, ADR 0067 impl-notat E2b).
  occupationGroup?: ReadonlyArray<string>;
  region?: ReadonlyArray<string>;
  municipality?: ReadonlyArray<string>;
  // Klass 2 (2026-06-13) — anställningsform + omfattning. Upprepad query-
  // string (?employmentType=a&employmentType=b), string[]-bindning backend
  // (B2/#60). worktimeExtent bär 0–1 element (radio-single i panelen).
  employmentType?: ReadonlyArray<string>;
  worktimeExtent?: ReadonlyArray<string>;
  // STEG 5 (grade-filter, 2026-06-23) — matchningsgrad-filter. Upprepad
  // query-string (?matchGrades=Strong&matchGrades=Good) som bär ENUM-NAMN
  // (`Basic` | `Good` | `Strong` — ALDRIG `Top`; backend-validatorn avvisar
  // det). Tom/utelämnad = inget grad-filter ("Matchning av", hela listan).
  matchGrades?: ReadonlyArray<string>;
  // #300 PR-5 (ADR 0084) — "Visa relaterade också"-toggle:n. true ⇒
  // ?includeRelated=true skickas och backend tar med yrken som LIKNAR de valda
  // (graderade `Related`, rung mellan Basic och Good) i listan. Default false
  // (behaviour-inert: ren exakt-match-lista). Master-switch härledd ur
  // `?relaterade=on` uppströms.
  includeRelated?: boolean;
  // #383 → förenklat 2026-06-30 — "Dölj ansökta". true ⇒ ?hideApplied=true skickas;
  // backend EXISTS-filtrerar bort annonser den inloggade seekern redan sökt. Default
  // false (ingen status-gallring). Härledd ur ?doljAnsokta. (savedOnly/appliedOnly
  // borttagna med "Visa sparade"/"Visa bara ansökta" — backend behåller fälten.)
  hideApplied?: boolean;
  // #419 punkt 1 (CTO Approach A) — "Visa bara matchade". true ⇒ ?onlyMatched=true
  // skickas; backend visar ENDAST annonser med en positiv matchningsgrad för seekern
  // (rank > 0). Default false (hela listan). Härledd ur ?baraMatchade uppströms; gatad
  // på matchnings-axeln aktiv (jobb-results.tsx) — som includeRelated.
  onlyMatched?: boolean;
  q?: string;
  // ADR 0060 amendment 2026-06-12 (Fas E2j) — commit-intent: true ⇒ ?commit=1
  // skickas och backend auto-capturerar sökningen till Senaste sökningar.
  // Default (live-förhandsvisning) fångas EJ. Transient signal, ej filter.
  commit?: boolean;
}

function authHeaders(sessionId: string): HeadersInit {
  return {
    Authorization: `Bearer ${sessionId}`,
    "Content-Type": "application/json",
  };
}

function buildQuery(query: ListJobAdsQuery): string {
  const params = new URLSearchParams();
  params.set("page", String(query.page));
  params.set("pageSize", String(query.pageSize));
  params.set("sortBy", query.sortBy);
  // append (ej set) — upprepad nyckel per element (ADR 0042 Beslut B).
  for (const v of query.occupationGroup ?? [])
    params.append("occupationGroup", v);
  for (const v of query.region ?? []) params.append("region", v);
  for (const v of query.municipality ?? []) params.append("municipality", v);
  // Klass 2 — upprepad nyckel per element (samma som dimensionerna ovan).
  for (const v of query.employmentType ?? [])
    params.append("employmentType", v);
  for (const v of query.worktimeExtent ?? [])
    params.append("worktimeExtent", v);
  // STEG 5 — matchningsgrad (enum-namn), upprepad nyckel per element. Tom
  // lista = inget param = "Matchning av" (backend returnerar hela listan).
  for (const v of query.matchGrades ?? []) params.append("matchGrades", v);
  // #300 PR-5 — "Visa relaterade också". Skriv BARA ut när true (default false =
  // ren lista). Värdet är "true" (ASP.NET bool-binding tar inte "1").
  if (query.includeRelated) params.set("includeRelated", "true");
  // #383 → förenklat — "Dölj ansökta". Skriv BARA ut när true (default false =
  // ingen gallring). Värdet är "true" (ASP.NET bool-binding tar inte "1").
  if (query.hideApplied) params.set("hideApplied", "true");
  // #419 pt1 — "Visa bara matchade". Skriv BARA ut när true (default false = hela
  // listan). Värdet är "true" (ASP.NET bool-binding tar inte "1").
  if (query.onlyMatched) params.set("onlyMatched", "true");
  if (query.q) params.set("q", query.q);
  // E2j — commit-intent gatar backend-auto-capture (ADR 0060 amend). Värdet
  // är "true" (ASP.NET bool-binding tar inte "1" — skulle 400:a list-queryn).
  if (query.commit) params.set("commit", "true");
  return params.toString();
}

/**
 * Hämtar paginerad JobAd-lista med valfria filter (occupationGroup[],
 * region[], q) och sort. Konsumerar `GET /api/v1/job-ads` (auth-gated, rate-limit
 * 60/min per UserId via backend ListReadPolicy — F2-P9 TD-70-leverans
 * 2026-05-13).
 *
 * 429-svar mappas till `{ kind: "rateLimited", retryAfterSeconds }` per
 * ADR 0030 amendment 2026-05-13. Konsumenten renderar konkret retry-tid.
 */
export async function getJobAds(
  query: ListJobAdsQuery
): Promise<ApiResult<ListJobAdsResult>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/job-ads?${buildQuery(query)}`,
      { headers: authHeaders(sessionId), cache: "no-store" }
    );
    return await responseToResult(
      res,
      listJobAdsResultSchema,
      "GET /api/v1/job-ads"
    );
  } catch {
    return { kind: "error" };
  }
}

/**
 * Hämtar en enskild JobAd via id. Konsumerar
 * `GET /api/v1/job-ads/{id:guid}` (auth-gated). 404 (annonsen finns inte)
 * mappas till `{ kind: "notFound" }` — anropas av `/jobb/[id]`-routen som
 * översätter det till Next `notFound()`. Speglar `getJobAds` Result/fel-
 * mönster (unauthorized / rateLimited / notFound / error) så konsumenten
 * får samma uttömmande switch (ADR 0030).
 *
 * `includeNotFound: true` krävs — till skillnad från list-endpointen kan
 * detalj-endpointen runtime-faktiskt returnera 404 (okänt id).
 */
export async function getJobAd(
  id: string
): Promise<ApiResult<JobAdDto>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/job-ads/${encodeURIComponent(id)}`,
      { headers: authHeaders(sessionId), cache: "no-store" }
    );
    return await responseToResult(
      res,
      jobAdDtoSchema,
      "GET /api/v1/job-ads/{id}",
      { includeNotFound: true }
    );
  } catch {
    return { kind: "error" };
  }
}

/**
 * ADR 0042 Beslut C — typeahead. Hämtar distinkta aktiva titel-prefix-
 * förslag. Konsumerar `GET /api/v1/job-ads/suggest` (auth-gated, egen
 * SuggestPolicy rate-limit 30/10s — typeahead = 1 req/keystroke).
 *
 * Anropas av en route-handler (`/api/jobb/suggest`) som Client-typeahead-
 * komponenten pollar. `prefix` förväntas ≥2 tecken (caller gatar; backend
 * 400 är sista barriär). 429 → `rateLimited` (caller renderar civilt).
 */
export async function suggestJobAdTerms(
  prefix: string,
  limit = 10
): Promise<ApiResult<SuggestJobAdTermsResult>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  const params = new URLSearchParams({
    prefix,
    limit: String(limit),
  });

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/job-ads/suggest?${params.toString()}`,
      { headers: authHeaders(sessionId), cache: "no-store" }
    );
    return await responseToResult(
      res,
      suggestJobAdTermsResultSchema,
      "GET /api/v1/job-ads/suggest"
    );
  } catch {
    return { kind: "error" };
  }
}

export interface FacetCountsFilter {
  occupationGroup?: ReadonlyArray<string>;
  municipality?: ReadonlyArray<string>;
  region?: ReadonlyArray<string>;
  // ADR 0067 PR-3 — Klass 2-filterkontext för facetten (backend exkluderar
  // den facetterade dimensionen själv ur WHERE).
  employmentType?: ReadonlyArray<string>;
  worktimeExtent?: ReadonlyArray<string>;
  q?: string;
}

/**
 * ADR 0067 Beslut 4 (Fas E2c) — per-option facet-counts för EN dimension
 * givet aktuellt filterval. Konsumerar `GET /api/v1/job-ads/facet-counts`
 * (auth-gated, egen FacetCountsPolicy 30/10s). Backend exkluderar den
 * facetterade dimensionen ur WHERE (ort-facetterna exkluderar HELA
 * ort-dimensionen — CTO VAL 4). Svar: rå dict concept-id → count.
 *
 * Anropas av route-handlern (`/api/jobb/facet-counts`) som popover-
 * klienten pollar debouncat (≥300 ms). 429 → `rateLimited` (klienten
 * degraderar tyst — counts försvinner, popovern förblir användbar).
 */
export async function getFacetCounts(
  dimension: FacetDimension,
  filter: FacetCountsFilter
): Promise<ApiResult<FacetCounts>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  const params = new URLSearchParams({ dimension });
  for (const v of filter.occupationGroup ?? [])
    params.append("occupationGroup", v);
  for (const v of filter.municipality ?? []) params.append("municipality", v);
  for (const v of filter.region ?? []) params.append("region", v);
  for (const v of filter.employmentType ?? [])
    params.append("employmentType", v);
  for (const v of filter.worktimeExtent ?? [])
    params.append("worktimeExtent", v);
  if (filter.q) params.set("q", filter.q);

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/job-ads/facet-counts?${params.toString()}`,
      { headers: authHeaders(sessionId), cache: "no-store" }
    );
    return await responseToResult(
      res,
      facetCountsSchema,
      "GET /api/v1/job-ads/facet-counts"
    );
  } catch {
    return { kind: "error" };
  }
}

import "server-only";
import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import {
  listJobAdsResultSchema,
  suggestJobAdTermsResultSchema,
  type ListJobAdsResult,
  type SuggestJobAdTermsResult,
  type JobAdSortBy,
} from "@/lib/dto/job-ads";
import { responseToResult, type ApiResult } from "@/lib/dto/_helpers";

export interface ListJobAdsQuery {
  page: number;
  pageSize: number;
  sortBy: JobAdSortBy;
  // ADR 0042 Beslut B — multi: skickas som upprepad query-string
  // (?ssyk=a&ssyk=b). ASP.NET Core minimal API binder till string[].
  ssyk?: ReadonlyArray<string>;
  region?: ReadonlyArray<string>;
  q?: string;
  // ADR 0042 Beslut E — "ny sedan"-fönster (ISO 8601). Driver JobAdDto.isNew.
  since?: string;
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
  for (const v of query.ssyk ?? []) params.append("ssyk", v);
  for (const v of query.region ?? []) params.append("region", v);
  if (query.q) params.set("q", query.q);
  if (query.since) params.set("since", query.since);
  return params.toString();
}

/**
 * Hämtar paginerad JobAd-lista med valfria filter (ssyk[], region[], q,
 * since) och sort. Konsumerar `GET /api/v1/job-ads` (auth-gated, rate-limit
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

import "server-only";
import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import {
  listJobAdsResultSchema,
  type ListJobAdsResult,
  type JobAdSortBy,
} from "@/lib/dto/job-ads";
import { responseToResult, type ApiResult } from "@/lib/dto/_helpers";

export interface ListJobAdsQuery {
  page: number;
  pageSize: number;
  sortBy: JobAdSortBy;
  ssyk?: string;
  region?: string;
  q?: string;
}

function authHeaders(sessionId: string): HeadersInit {
  return {
    Authorization: `Bearer ${sessionId}`,
    "Content-Type": "application/json",
  };
}

function buildQuery(query: ListJobAdsQuery): string {
  const params = new URLSearchParams({
    page: String(query.page),
    pageSize: String(query.pageSize),
    sortBy: query.sortBy,
  });
  if (query.ssyk) params.set("ssyk", query.ssyk);
  if (query.region) params.set("region", query.region);
  if (query.q) params.set("q", query.q);
  return params.toString();
}

/**
 * Hämtar paginerad JobAd-lista med valfria filter (ssyk, region, q) och sort.
 * Konsumerar `GET /api/v1/job-ads` (auth-gated, rate-limit 60/min per UserId
 * via backend ListReadPolicy — F2-P9 TD-70-leverans 2026-05-13).
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

import "server-only";

import type { AdminJuniorTab } from "@/lib/admin/junior-jobs";
import { getSessionId } from "@/lib/auth/session";
import { responseToResult, type ApiResult } from "@/lib/dto/_helpers";
import { juniorJobAdQueueSchema, wireJuniorView, type JuniorJobAdQueueDto } from "@/lib/dto/admin-junior-job-ads";
import { authedFetch } from "@/lib/http/authed-fetch";

const BASE_PATH = "/api/v1/admin/junior-job-ads";

/** The path one ad's decision is posted to. */
export function juniorDecisionPath(id: string): string {
  return `${BASE_PATH}/${encodeURIComponent(id)}/decision`;
}

/** The list's query string: the tab by its backend name and the page. Neither names a person. */
export function juniorQueueQuery(tab: AdminJuniorTab, page: number): string {
  return new URLSearchParams({ view: wireJuniorView(tab), page: String(page) }).toString();
}

/** One page of a tab, newest first, with the count of each tab (#2264). */
export async function listJuniorJobAds(tab: AdminJuniorTab, page: number): Promise<ApiResult<JuniorJobAdQueueDto>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await authedFetch(sessionId, `${BASE_PATH}?${juniorQueueQuery(tab, page)}`);
    return await responseToResult(res, juniorJobAdQueueSchema, "GET /api/v1/admin/junior-job-ads");
  } catch {
    return { kind: "error" };
  }
}

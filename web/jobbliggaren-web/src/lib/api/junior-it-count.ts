import "server-only";
import { getSessionId } from "@/lib/auth/session";
import { authedFetch } from "@/lib/http/authed-fetch";
import { juniorItCountSchema, type JuniorItCount } from "@/lib/dto/junior-it-count";
import { responseToResult, type ApiResult } from "@/lib/dto/_helpers";

// senior-cto-advisor B3 (#2265): the link is a decoration in the hero's Promise.all, so it may not set the hero's
// worst case. Past this the link is hidden.
const TIMEOUT_MS = 2_000;

/**
 * ADR 0188 (#2265) — the junior quick link's count. Read per request: `authedFetch` forces `no-store`, and there is no
 * `cache()` or `revalidate` here (ADR 0166 trigger 22, security-auditor m3). It never throws, because the hero's
 * `Promise.all` must not reject: every failure is an error result, and the caller hides the link.
 */
export async function getJuniorItCount(): Promise<ApiResult<JuniorItCount>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await authedFetch(sessionId, "/api/v1/job-ads/junior-it-count", {
      signal: AbortSignal.timeout(TIMEOUT_MS),
    });
    return await responseToResult(res, juniorItCountSchema, "GET /api/v1/job-ads/junior-it-count");
  } catch {
    return { kind: "error" };
  }
}

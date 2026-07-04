import "server-only";
import { getSessionId } from "@/lib/auth/session";
import { authedFetch } from "@/lib/http/authed-fetch";
import {
  applicationHistoryResultSchema,
  type ApplicationHistoryResult,
} from "@/lib/dto/application-history";
import { responseToResult, type ApiResult } from "@/lib/dto/_helpers";

/**
 * #311 #448 (ADR 0087 D2 / ADR 0090 D1) — the caller's OWN application history grouped by employer,
 * for the `/foretag` "Ansökningshistorik" section. Auth-gated (`GET /api/v1/me/application-history`,
 * parity `getCompanyWatches`). The backend owner-scopes on JobSeekerId (never the wire), masks any
 * personnummer-shaped org.nr before it reaches the wire (ADR 0087 D8(c)) and never logs it. List
 * semantics: a 404 collapses to `error` (no `notFound` for a collection endpoint — ADR 0030).
 */
export async function getApplicationHistory(): Promise<ApiResult<ApplicationHistoryResult>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await authedFetch(sessionId, "/api/v1/me/application-history");
    return await responseToResult(
      res,
      applicationHistoryResultSchema,
      "GET /api/v1/me/application-history"
    );
  } catch {
    return { kind: "error" };
  }
}

import "server-only";
import { getSessionId } from "@/lib/auth/session";
import { authedFetch } from "@/lib/http/authed-fetch";
import { responseToResult, type ApiResult } from "@/lib/dto/_helpers";
import { notificationFeedSchema, type NotificationFeed } from "@/lib/dto/notification-feed";

/**
 * #1666 — the signed-in user's notification bell, `GET /api/v1/me/notification-feed` (auth-gated,
 * its own per-user rate-limit bucket). Read-only: acknowledging goes through the sources' existing
 * `/seen` endpoints, never through this read. `signal` lets the BFF route abandon the backend call
 * when the browser does.
 */
export async function getNotificationFeed(signal?: AbortSignal): Promise<ApiResult<NotificationFeed>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await authedFetch(sessionId, "/api/v1/me/notification-feed", { signal });
    return await responseToResult(res, notificationFeedSchema, "GET /api/v1/me/notification-feed");
  } catch {
    return { kind: "error" };
  }
}

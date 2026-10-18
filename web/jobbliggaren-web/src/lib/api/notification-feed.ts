import "server-only";
import { getSessionId } from "@/lib/auth/session";
import { authedFetch } from "@/lib/http/authed-fetch";
import { parseRetryAfter, responseToResult, type ApiResult } from "@/lib/dto/_helpers";
import { isValidId } from "@/lib/validation/guid";
import {
  notificationFeedSchema,
  type NotificationFeed,
  type NotificationFeedSourceKind,
} from "@/lib/dto/notification-feed";

/**
 * #1666 — the signed-in user's notification bell, `GET /api/v1/me/notification-feed` (auth-gated,
 * its own per-user rate-limit bucket). Read-only: a read never acknowledges anything, and since #2130
 * the bell's writes go to `markNotificationRead` and `markNotificationsReadThrough` below, never to a
 * source's `/seen` endpoint. `signal` lets the BFF route abandon the backend call when the browser does.
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

/**
 * The path segment of each source in the API's routes. A closed map, never the wire value spliced into a path:
 * a Server Action is a public endpoint, and whatever a caller sends as `kind` must not decide where the
 * request goes (security-auditor V8, path traversal).
 */
const SOURCE_SEGMENT: Record<NotificationFeedSourceKind, string> = {
  Match: "matches",
  FollowedCompanyAd: "followed-company-ads",
  CompanyWatchCriterionAd: "company-watch-criterion-ads",
};

/**
 * #2130 — marks ONE notification read in the bell: `POST /api/v1/me/notification-feed/{source}/{jobAdId}/read`
 * (auth-gated, its own write bucket). The owner is the session's, never the wire's. The API answers 204
 * whether or not the row exists, belongs to the caller or is still listed, so the status is the whole truth.
 * It sets the bell's own flag only: no watermark moves, so Översikt and the lists keep their numbers.
 */
export async function markNotificationRead(
  kind: NotificationFeedSourceKind,
  jobAdId: string,
): Promise<ApiResult<void>> {
  const segment = SOURCE_SEGMENT[kind];
  if (segment === undefined || !isValidId(jobAdId)) return { kind: "error" };
  return postBellWrite(`/api/v1/me/notification-feed/${segment}/${encodeURIComponent(jobAdId)}/read`);
}

/**
 * #2151 — the X: takes ONE notification out of the bell, `POST /api/v1/me/notification-feed/{source}/{jobAdId}/dismiss`
 * (auth-gated, the same write bucket as the read marks). A POST and not a DELETE: nothing is deleted, the bell's
 * own flags are set. The API answers 204 whether or not the row exists or belongs to the caller. Nothing another surface
 * reads changes: Översikt, `/matchningar` and `/foretag/bevakade/nya` keep their rows and numbers.
 */
export async function dismissNotification(
  kind: NotificationFeedSourceKind,
  jobAdId: string,
): Promise<ApiResult<void>> {
  const segment = SOURCE_SEGMENT[kind];
  if (segment === undefined || !isValidId(jobAdId)) return { kind: "error" };
  return postBellWrite(`/api/v1/me/notification-feed/${segment}/${encodeURIComponent(jobAdId)}/dismiss`);
}

/**
 * #2130 — "Markera alla som lästa" for ONE source: flags every row of the source that is new since its
 * watermark and no newer than `throughToken`, the token the feed returned for it (sent as the original
 * string: the API stores microseconds). Never moves a watermark.
 */
export async function markNotificationsReadThrough(
  kind: NotificationFeedSourceKind,
  throughToken: string,
): Promise<ApiResult<void>> {
  const segment = SOURCE_SEGMENT[kind];
  if (segment === undefined) return { kind: "error" };
  return postBellWrite(`/api/v1/me/notification-feed/${segment}/read`, { throughToken });
}

async function postBellWrite(path: string, body?: Record<string, string>): Promise<ApiResult<void>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await authedFetch(sessionId, path, {
      method: "POST",
      ...(body ? { body: JSON.stringify(body) } : {}),
    });
    if (res.status === 204) return { kind: "ok", data: undefined };
    if (res.status === 401) return { kind: "unauthorized" };
    if (res.status === 403) return { kind: "forbidden" };
    if (res.status === 429) {
      return { kind: "rateLimited", retryAfterSeconds: parseRetryAfter(res.headers.get("Retry-After")) };
    }
    return { kind: "error" };
  } catch {
    return { kind: "error" };
  }
}

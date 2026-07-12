import "server-only";

import { cache } from "react";
import { getSessionId } from "@/lib/auth/session";
import { authedFetch } from "@/lib/http/authed-fetch";
import {
  jobSeekerProfileSchema,
  type DigestCadence,
  type JobSeekerProfileDto,
} from "@/lib/dto/me";
import {
  parseRetryAfter,
  responseToResult,
  type ApiResult,
} from "@/lib/dto/_helpers";

export const getMyProfile = cache(
  async (): Promise<ApiResult<JobSeekerProfileDto>> => {
    const sessionId = await getSessionId();
    if (!sessionId) return { kind: "unauthorized" };

    try {
      const res = await authedFetch(sessionId, "/api/v1/me/profile");
      return await responseToResult(
        res,
        jobSeekerProfileSchema,
        "GET /api/v1/me/profile"
      );
    } catch {
      return { kind: "error" };
    }
  }
);

/**
 * ADR 0080 Vag 4 PR-6 — sets the current user's background-match notification
 * consent: the opt-in toggle (`enabled`, GDPR Art. 6(1)(a)/7 — default OFF per
 * PR-1) and the digest cadence for accumulated Strong matches. Mirrors
 * `markMatchesSeen`'s non-GET write shape: `PUT /api/v1/me/notification-consent`
 * with a JSON body, idempotent full-replace, 204 on success / Problem 400 on
 * failure (status-code is the whole truth — the body is NEVER read, TD-10).
 *
 * The current state is READ via `getMyProfile()` (the flag + cadence ride the
 * JobSeekerProfileDto projection) — there is no dedicated read endpoint.
 * Returns `ApiResult<void>`: 204 → ok; 401/403 mapped; everything else → error.
 * Server-only (the Bearer session never reaches the client).
 */
export async function updateNotificationConsent(input: {
  enabled: boolean;
  cadence: DigestCadence;
}): Promise<ApiResult<void>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await authedFetch(
      sessionId,
      "/api/v1/me/notification-consent",
      {
        method: "PUT",
        body: JSON.stringify({
          enabled: input.enabled,
          cadence: input.cadence,
        }),
      }
    );

    if (res.status === 204) return { kind: "ok", data: undefined };
    if (res.status === 401) return { kind: "unauthorized" };
    if (res.status === 403) return { kind: "forbidden" };
    if (res.status === 429) {
      return {
        kind: "rateLimited",
        retryAfterSeconds: parseRetryAfter(res.headers.get("Retry-After")),
      };
    }
    return { kind: "error" };
  } catch {
    return { kind: "error" };
  }
}

/**
 * Bevakning F4 (#803, CTO RF-12=12C) — sets the current user's consent for the
 * followed-company email digest (GDPR Art. 6(1)(a)/7, default OFF; turning it
 * off is the Art. 7(3) withdrawal). A DISTINCT purpose from the background-match
 * consent above, hence its own endpoint.
 *
 * The body carries ONLY `{ enabled }`: the digest cadence is SHARED between the
 * two notification purposes (ADR 0087 D2) and is written through
 * `updateNotificationConsent` — sending a cadence here would imply a second,
 * independent cadence that does not exist.
 *
 * Mirrors the sibling write exactly: `PUT` + JSON body, idempotent full-replace,
 * 204 on success / Problem 400 on failure (the status code is the whole truth —
 * the body is NEVER read, TD-10). The current state is READ via `getMyProfile()`
 * (`followedCompanyNotificationsEnabled` rides the JobSeekerProfileDto
 * projection); there is no dedicated read endpoint. Server-only.
 */
export async function updateFollowedCompanyNotificationConsent(input: {
  enabled: boolean;
}): Promise<ApiResult<void>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await authedFetch(
      sessionId,
      "/api/v1/me/followed-company-notification-consent",
      {
        method: "PUT",
        body: JSON.stringify({ enabled: input.enabled }),
      }
    );

    if (res.status === 204) return { kind: "ok", data: undefined };
    if (res.status === 401) return { kind: "unauthorized" };
    if (res.status === 403) return { kind: "forbidden" };
    if (res.status === 429) {
      return {
        kind: "rateLimited",
        retryAfterSeconds: parseRetryAfter(res.headers.get("Retry-After")),
      };
    }
    return { kind: "error" };
  } catch {
    return { kind: "error" };
  }
}

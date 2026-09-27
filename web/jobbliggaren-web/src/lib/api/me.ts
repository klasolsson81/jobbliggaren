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

// The notification-settings writes share one wire shape: `PUT` + JSON body, idempotent set, 204 on
// success (the status code is the whole truth — the body is NEVER read,
// TD-10). The current state is READ via `getMyProfile()`; there is no dedicated read endpoint.
// Server-only (the Bearer session never reaches the client).
async function putNotificationSetting(
  path: string,
  body: Record<string, unknown>
): Promise<ApiResult<void>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await authedFetch(sessionId, path, {
      method: "PUT",
      body: JSON.stringify(body),
    });

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
 * ADR 0080 Vag 4 PR-6 — sets the current user's background-match notification
 * consent (GDPR Art. 6(1)(a)/7, default OFF; turning it off is the Art. 7(3)
 * withdrawal). The body carries only `{ enabled }`: the digest cadence has its
 * own write, `updateDigestCadence`.
 */
export function updateNotificationConsent(input: {
  enabled: boolean;
}): Promise<ApiResult<void>> {
  return putNotificationSetting(
    "/api/v1/me/background-match-notification-consent",
    { enabled: input.enabled }
  );
}

/**
 * Bevakning F4 (#803, CTO RF-12=12C) — sets the current user's consent for the
 * followed-company email digest (GDPR Art. 6(1)(a)/7, default OFF; turning it
 * off is the Art. 7(3) withdrawal). A DISTINCT purpose from the background-match
 * consent above, hence its own endpoint. The body carries only `{ enabled }`.
 */
export function updateFollowedCompanyNotificationConsent(input: {
  enabled: boolean;
}): Promise<ApiResult<void>> {
  return putNotificationSetting(
    "/api/v1/me/followed-company-notification-consent",
    { enabled: input.enabled }
  );
}

/**
 * ADR 0087 D2 — sets the digest cadence the two notification consents share and
 * neither owns. The body carries only `{ cadence }`, never a consent value.
 */
export function updateDigestCadence(input: {
  cadence: DigestCadence;
}): Promise<ApiResult<void>> {
  return putNotificationSetting("/api/v1/me/digest-cadence", {
    cadence: input.cadence,
  });
}

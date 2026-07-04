import "server-only";
import { getSessionId } from "@/lib/auth/session";
import { authedFetch } from "@/lib/http/authed-fetch";
import {
  companyWatchStatusBatchSchema,
  followCompanyResultSchema,
  listCompanyWatchesResultSchema,
  type CompanyFollowState,
  type ListCompanyWatchesResult,
} from "@/lib/dto/company-follows";
import { responseToResult, type ApiResult } from "@/lib/dto/_helpers";
import { isValidId } from "@/lib/validation/guid";

const BASE = "/api/v1/me/company-watches";

/**
 * #455 (ADR 0087 D8(c), Approach A) — follow a job ad's employer. The FE passes the non-PII JobAdId;
 * the backend resolves the org.nr server-side (it never crosses the wire). Returns the CompanyWatchId so
 * the toggle can later unfollow by opaque id. `POST /api/v1/me/company-watches/by-job-ad/{jobAdId}`.
 */
export async function followCompanyFromJobAd(
  jobAdId: string
): Promise<ApiResult<{ companyWatchId: string }>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };
  // Allowlist-guard: reject a non-GUID before it reaches the backend URL (SSRF barrier + path-injection).
  if (!isValidId(jobAdId)) return { kind: "notFound" };

  try {
    const res = await authedFetch(sessionId, `${BASE}/by-job-ad/${encodeURIComponent(jobAdId)}`, {
      method: "POST",
    });
    if (res.status === 201) {
      const parsed = followCompanyResultSchema.safeParse(await res.json());
      return parsed.success
        ? { kind: "ok", data: { companyWatchId: parsed.data.id } }
        : { kind: "error" };
    }
    if (res.status === 401) return { kind: "unauthorized" };
    if (res.status === 404) return { kind: "notFound" };
    // 400 = the ad carries no employer org.nr (B2). The FE hides the affordance when !followable, so
    // this is a stale-FE backstop → generic error.
    return { kind: "error" };
  } catch {
    return { kind: "error" };
  }
}

/**
 * #454 (ADR 0088) — follow an employer DIRECTLY by org.nr (the lookup card's "bevaka" for a
 * company with zero ads in our feed — the #455 by-job-ad path cannot reach those). Reuses the
 * EXISTING `POST /api/v1/me/company-watches` (org.nr in the BODY per D8(c) — never a URL; the
 * endpoint predates this first FE consumer). The caller passes an already-normalised 10-digit
 * value; the backend validator + VO are the last barrier (400 → error).
 */
export async function followCompany(
  orgNr: string
): Promise<ApiResult<{ companyWatchId: string }>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await authedFetch(sessionId, BASE, {
      method: "POST",
      body: JSON.stringify({ organizationNumber: orgNr }),
    });
    if (res.status === 201) {
      const parsed = followCompanyResultSchema.safeParse(await res.json());
      return parsed.success
        ? { kind: "ok", data: { companyWatchId: parsed.data.id } }
        : { kind: "error" };
    }
    if (res.status === 401) return { kind: "unauthorized" };
    return { kind: "error" };
  } catch {
    return { kind: "error" };
  }
}

/**
 * #455 — stop following, by the opaque CompanyWatchId (never the org.nr, per D8(c)). Idempotent
 * (already-unfollowed → 204). `DELETE /api/v1/me/company-watches/{companyWatchId}`.
 */
export async function unfollowCompany(companyWatchId: string): Promise<ApiResult<void>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };
  if (!isValidId(companyWatchId)) return { kind: "notFound" };

  try {
    const res = await authedFetch(sessionId, `${BASE}/${encodeURIComponent(companyWatchId)}`, {
      method: "DELETE",
    });
    if (res.status === 204) return { kind: "ok", data: undefined };
    if (res.status === 401) return { kind: "unauthorized" };
    return { kind: "error" };
  } catch {
    return { kind: "error" };
  }
}

/**
 * #455 — single-ad follow-state for the detail footer, resolved via the auth-gated batch endpoint with a
 * one-element id list (parity with how `isJobAdSaved` reuses the list read for a single check). Any
 * failure / anon → not-followable, not-following (the toggle then hides; civic-utility no-teater).
 * `POST /api/v1/me/company-watches/status`.
 */
export async function getCompanyWatchStatus(jobAdId: string): Promise<CompanyFollowState> {
  const fallback: CompanyFollowState = { companyWatchId: null, followable: false };

  const sessionId = await getSessionId();
  if (!sessionId) return fallback;
  if (!isValidId(jobAdId)) return fallback;

  try {
    const res = await authedFetch(sessionId, `${BASE}/status`, {
      method: "POST",
      body: JSON.stringify({ jobAdIds: [jobAdId] }),
    });
    if (!res.ok) return fallback;
    const parsed = companyWatchStatusBatchSchema.safeParse(await res.json());
    if (!parsed.success) return fallback;
    const status = parsed.data.statuses.find((s) => s.jobAdId === jobAdId);
    return status
      ? { companyWatchId: status.companyWatchId, followable: status.followable }
      : fallback;
  } catch {
    return fallback;
  }
}

/**
 * #453 (cross-channel dedup) — mark this ad SEEN in-app so the company-follow digest suppresses the
 * redundant email ("aldrig mejla något jag sett i appen"). Fire-and-forget: the caller ignores the
 * outcome and this never throws (a rate-limit / network error just leaves the hit un-stamped → the
 * digest may still send, which is the safe direction). Owner-scoped server-side (the UserId comes from
 * the session, never the wire — IDOR-safe). `POST /api/v1/me/company-watches/ad-hits/{jobAdId}/seen`.
 */
export async function markFollowedCompanyAdSeen(jobAdId: string): Promise<ApiResult<void>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };
  // Allowlist-guard: reject a non-GUID before it reaches the backend URL (path-injection barrier).
  if (!isValidId(jobAdId)) return { kind: "notFound" };

  try {
    const res = await authedFetch(
      sessionId,
      `${BASE}/ad-hits/${encodeURIComponent(jobAdId)}/seen`,
      {
        method: "POST",
      }
    );
    if (res.status === 204) return { kind: "ok", data: undefined };
    if (res.status === 401) return { kind: "unauthorized" };
    return { kind: "error" };
  } catch {
    return { kind: "error" };
  }
}

/**
 * #448 — list the current user's followed companies for the `/foretag` page. Auth-gated
 * (`GET /api/v1/me/company-watches`, parity with `getSavedJobAds`). The backend masks any
 * personnummer-shaped org.nr before it reaches the wire (ADR 0087 D8(c)) and never logs it. List
 * semantics: a 404 collapses to `error` (no `notFound` for a collection endpoint — ADR 0030).
 */
export async function getCompanyWatches(): Promise<ApiResult<ListCompanyWatchesResult>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await authedFetch(sessionId, BASE);
    return await responseToResult(
      res,
      listCompanyWatchesResultSchema,
      "GET /api/v1/me/company-watches"
    );
  } catch {
    return { kind: "error" };
  }
}

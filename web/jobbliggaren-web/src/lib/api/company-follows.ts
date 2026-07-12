import "server-only";
import { getSessionId } from "@/lib/auth/session";
import { authedFetch } from "@/lib/http/authed-fetch";
import {
  companyWatchStatusBatchSchema,
  followCompanyResultSchema,
  listCompanyWatchesResultSchema,
  newFollowedCompanyAdCountSchema,
  type CompanyFollowState,
  type ListCompanyWatchesResult,
  type NewFollowedCompanyAdCount,
} from "@/lib/dto/company-follows";
import {
  parseRetryAfter,
  responseToResult,
  type ApiResult,
} from "@/lib/dto/_helpers";
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
 * Bevakning F4b (#803) — replace ONE watch's notification filter. `PUT /me/company-watches/{id}/filter`.
 *
 * Full-replace, and an ALL-EMPTY selection is how the user clears the filter (the backend maps it to the
 * canonical NULL — there is no separate DELETE route to get out of sync with). The two geo axes are
 * DISJOINT namespaces and are sent exactly as picked: a whole-län selection travels as a län concept-id
 * and is never expanded into its municipalities, because an ad tagged at län granularity carries no
 * municipality and would silently stop notifying the user.
 *
 * 204 on success; the body is never read (TD-10). A cross-user id yields 404, never 403.
 */
export async function setWatchFilter(
  companyWatchId: string,
  filter: {
    municipalities: ReadonlyArray<string>;
    regions: ReadonlyArray<string>;
    onlyMatched: boolean;
  }
): Promise<ApiResult<void>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };
  if (!isValidId(companyWatchId)) return { kind: "notFound" };

  try {
    const res = await authedFetch(
      sessionId,
      `${BASE}/${encodeURIComponent(companyWatchId)}/filter`,
      {
        method: "PUT",
        body: JSON.stringify({
          municipalities: filter.municipalities,
          regions: filter.regions,
          onlyMatched: filter.onlyMatched,
        }),
      }
    );

    if (res.status === 204) return { kind: "ok", data: undefined };
    if (res.status === 401) return { kind: "unauthorized" };
    if (res.status === 403) return { kind: "forbidden" };
    if (res.status === 404) return { kind: "notFound" };
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
 * Bevakning F2 (#801, RF-6=6B) — the count of new ads from followed employers NEW since the user last
 * visited /foretag, for the Översikt "Nya annonser från bevakade företag"-row. Consumes
 * `GET /api/v1/me/followed-company-ads/new-count` (auth-gated, MeListReadPolicy). Mirrors
 * `getNewMatchCount`'s Result/error shape (ADR 0030). `count === 0` is honest (no active follows /
 * nothing new) — the row renders 0, never a mock number.
 */
export async function getNewFollowedCompanyAdCount(): Promise<
  ApiResult<NewFollowedCompanyAdCount>
> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await authedFetch(sessionId, "/api/v1/me/followed-company-ads/new-count");
    return await responseToResult(
      res,
      newFollowedCompanyAdCountSchema,
      "GET /api/v1/me/followed-company-ads/new-count"
    );
  } catch {
    return { kind: "error" };
  }
}

/**
 * Bevakning F2 (#801, RF-6=6B) — advance the follow-rail watermark (reset the Översikt count).
 * Called when the user VISITS the follows hub (/foretag) — the sibling of `markMatchesSeen` (Klas
 * decision: advance on visiting the surface). `POST /api/v1/me/followed-company-ads/seen` → 204
 * (auth-gated, MeWritePolicy). No `seenThrough` sent (the hub renders no individual hits to preserve)
 * → the backend advances to clock-now (the documented safe fallback). Idempotent and NON-critical: a
 * failure must NEVER block the page render (the count just does not reset this time) — degrades
 * civilly like `markMatchesSeen`. 204 → ok; anything else → error-kind.
 */
export async function markFollowedAdsSeen(seenThrough?: string): Promise<ApiResult<void>> {
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "unauthorized" };

  try {
    const res = await authedFetch(sessionId, "/api/v1/me/followed-company-ads/seen", {
      method: "POST",
      ...(seenThrough ? { body: JSON.stringify({ seenThrough }) } : {}),
    });
    if (res.status === 204) return { kind: "ok", data: undefined };
    if (res.status === 401) return { kind: "unauthorized" };
    if (res.status === 403) return { kind: "forbidden" };
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

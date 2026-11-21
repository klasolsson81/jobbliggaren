import type { NextRequest, NextResponse } from "next/server";
import {
  PERSISTENT_MAX_AGE_SECONDS,
  REFRESH_AFTER_COOKIE_NAME,
  SESSION_COOKIE_NAME,
} from "./cookie-names";
import { env } from "@/lib/env";
import { isPrefetch } from "@/lib/http/fetch-metadata";
import { pickForwardedHeaders } from "@/lib/http/forwarded-headers";

/**
 * The persistent-session refresh and rotation (PR2b-3b, epic #481), which the proxy drives: an authenticated request
 * calls `POST /auth/refresh` at most once per {@link REFRESH_THROTTLE_SECONDS}. A Persistent session (its id rotates
 * every 24 h) returns `rotated: true` and a new id, which the caller re-sets as a persistent cookie and forwards to the
 * downstream render; a Session-scoped one only slides. The refresh is best-effort: only an exact 401 says anything about
 * the session (#1893), and every other failure is reported as one, so navigation never breaks.
 *
 * {@link refreshSession} decides and calls; {@link writeRefreshCookies} writes what the outcome asks of a response. The
 * caller chooses the response itself.
 */

// Throttle: drive a refresh at most once per 15 min. Chosen well below the backend's
// 24h rotation interval (so a rotation window is never missed) and well above
// per-request (so ordinary browsing does not hammer /auth/refresh). The sliding
// touch this drives is cheap; 15 min keeps the session fresh without amplifying load.
const REFRESH_THROTTLE_SECONDS = 15 * 60;

// On a failed/slow refresh, back off only briefly (not the full window) before the next
// attempt, so a degraded backend is not re-hit on every navigation while recovery stays
// prompt.
const REFRESH_ERROR_BACKOFF_SECONDS = 30;

// Bound the refresh round-trip: a stalled (accepted-but-not-answering) backend must never
// hang a navigation. undici's default headersTimeout is ~300s, far too long for a
// hot-path nav; on timeout the fetch rejects with AbortError, which the catch below swallows
// into a failed outcome (§2.5 hot-path; navigation must never break here).
const REFRESH_TIMEOUT_MS = 2500;

// The __Host- attribute set shared by every cookie this driver writes. Never weaken
// these: __Host- requires Secure + Path=/ + no Domain, and the session/companion
// cookies stay HttpOnly + SameSite=Strict.
const HOST_COOKIE_ATTRS = {
  httpOnly: true,
  secure: true,
  sameSite: "strict",
  path: "/",
} as const;

/** What one pass of the driver found. */
export type SessionRefresh =
  /** A speculative prefetch, or the throttle says the next refresh is not due: nothing was called. */
  | { readonly kind: "notDue" }
  /** A timeout, a network error, a status other than 401, or a malformed body: it says nothing about the session. */
  | { readonly kind: "failed" }
  /** Exactly 401: the server no longer knows the session the cookie names (#1893). */
  | { readonly kind: "unknownSession" }
  /** The session slid (or is Session-scoped); its id stays. */
  | { readonly kind: "slid" }
  /** The session was Persistent and rotated to a new id. */
  | { readonly kind: "rotated"; readonly sessionId: string };

export const FAILED_REFRESH: SessionRefresh = { kind: "failed" };
export const UNKNOWN_SESSION: SessionRefresh = { kind: "unknownSession" };

/**
 * One pass of the driver for the session `sessionId` names, at `nowSeconds`. Never rotates on a speculative prefetch:
 * it is not a real navigation, and rotating the id for a page the user may never open would churn the session.
 */
export async function refreshSession(
  request: NextRequest,
  sessionId: string,
  nowSeconds: number
): Promise<SessionRefresh> {
  if (isPrefetch(request.headers)) return { kind: "notDue" };

  // Throttle via the non-secret companion cookie (epoch seconds after which the next
  // refresh is due). Absent or elapsed → due; otherwise skip without hitting the BE.
  const refreshAfterRaw = request.cookies.get(REFRESH_AFTER_COOKIE_NAME)?.value;
  const refreshAfter = refreshAfterRaw ? Number.parseInt(refreshAfterRaw, 10) : Number.NaN;
  const refreshDue = !Number.isFinite(refreshAfter) || nowSeconds >= refreshAfter;
  if (!refreshDue) return { kind: "notDue" };

  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/auth/refresh`, {
      method: "POST",
      // pickForwardedHeaders, not forwardedHeaders(): middleware has no `next/headers`
      // request scope, but it does have the NextRequest, which carries the same inbound
      // headers. Refresh is rate-limited per client IP like every other auth route (#1202).
      headers: {
        ...pickForwardedHeaders(request.headers),
        Authorization: `Bearer ${sessionId}`,
      },
      cache: "no-store",
      signal: AbortSignal.timeout(REFRESH_TIMEOUT_MS),
    });
    if (res.ok) {
      const refresh = parseRefresh(await res.json());
      if (!refresh) return FAILED_REFRESH;
      return refresh.rotated && refresh.sessionId
        ? { kind: "rotated", sessionId: refresh.sessionId }
        : { kind: "slid" };
    }
    return res.status === 401 ? { kind: "unknownSession" } : FAILED_REFRESH;
  } catch {
    // Swallow — a failure says nothing about the session, and navigation must never break here.
    return FAILED_REFRESH;
  }
}

/**
 * Writes onto `response` what the outcome asks, and marks it `no-store` unless nothing was written:
 * - `notDue`: nothing;
 * - `failed`: the throttle, advanced by a short backoff only, so a degraded backend is not re-hit on every navigation
 *   while recovery stays prompt; the session cookie is left untouched;
 * - `slid`: the throttle, advanced by the full window; the session cookie is left untouched;
 * - `rotated`: the new id as a persistent cookie (rotated ⟹ the session was Persistent), and the throttle;
 * - `unknownSession`: both cookies cleared, with their `__Host-` attributes, so the next request does not ask again.
 */
export function writeRefreshCookies(
  response: NextResponse,
  outcome: SessionRefresh,
  nowSeconds: number
): NextResponse {
  switch (outcome.kind) {
    case "notDue":
      return response;
    case "failed":
      setThrottleCookie(response, nowSeconds + REFRESH_ERROR_BACKOFF_SECONDS, REFRESH_ERROR_BACKOFF_SECONDS);
      break;
    case "slid":
      setThrottleCookie(response, nowSeconds + REFRESH_THROTTLE_SECONDS, REFRESH_THROTTLE_SECONDS);
      break;
    case "rotated":
      response.cookies.set(SESSION_COOKIE_NAME, outcome.sessionId, {
        ...HOST_COOKIE_ATTRS,
        maxAge: PERSISTENT_MAX_AGE_SECONDS,
      });
      setThrottleCookie(response, nowSeconds + REFRESH_THROTTLE_SECONDS, REFRESH_THROTTLE_SECONDS);
      break;
    case "unknownSession":
      response.cookies.set(SESSION_COOKIE_NAME, "", { ...HOST_COOKIE_ATTRS, maxAge: 0 });
      response.cookies.set(REFRESH_AFTER_COOKIE_NAME, "", { ...HOST_COOKIE_ATTRS, maxAge: 0 });
      break;
  }
  // A Set-Cookie must never be cached.
  response.headers.set("Cache-Control", "no-store");
  return response;
}

/**
 * The request headers for a render after a rotation: the session cookie carries the NEW id, so the downstream
 * Server-Component render (getServerSession → /me) reads it, not the retired one. Per Next issue #57655 the modified
 * headers MUST be forwarded explicitly via NextResponse.next({ request: { headers } }) or the SC sees a stale value.
 * (The backend keeps the old id valid for a 60s grace window, so this is belt-and-suspenders — but it must be correct.)
 */
export function forwardedWithSession(forwarded: Headers, cookieHeader: string | null, sessionId: string): Headers {
  const headers = new Headers(forwarded);
  headers.set("cookie", rewriteCookieHeader(cookieHeader, SESSION_COOKIE_NAME, sessionId));
  return headers;
}

// Writes the throttle companion cookie: its VALUE is the epoch second the next refresh is
// due, and its own Max-Age matches that window so it expires exactly when it becomes due.
function setThrottleCookie(response: NextResponse, dueAtEpochSeconds: number, maxAgeSeconds: number): void {
  response.cookies.set(REFRESH_AFTER_COOKIE_NAME, String(dueAtEpochSeconds), {
    ...HOST_COOKIE_ATTRS,
    maxAge: maxAgeSeconds,
  });
}

interface RefreshResult {
  rotated: boolean;
  sessionId: string | null;
}

/**
 * Narrows the untyped /auth/refresh JSON body to a RefreshResult, or null if the
 * shape is unexpected (treated as a failed refresh — no rotation, no throttle).
 */
function parseRefresh(body: unknown): RefreshResult | null {
  if (typeof body !== "object" || body === null) {
    return null;
  }
  const record = body as Record<string, unknown>;
  if (typeof record.rotated !== "boolean") {
    return null;
  }
  return {
    rotated: record.rotated,
    sessionId: typeof record.sessionId === "string" ? record.sessionId : null,
  };
}

/**
 * Replaces just the session cookie's value inside a raw Cookie header string,
 * leaving every other cookie intact. Appends the pair if the cookie is absent.
 */
function rewriteCookieHeader(cookieHeader: string | null, name: string, value: string): string {
  const pair = `${name}=${value}`;
  if (!cookieHeader) {
    return pair;
  }
  let found = false;
  // Split on ';' with optional whitespace — browsers always send "; ", but tolerate a
  // non-standard separator so the old value is replaced (not appended alongside).
  const rewritten = cookieHeader.split(/;\s*/).map((part) => {
    const eq = part.indexOf("=");
    const key = (eq === -1 ? part : part.slice(0, eq)).trim();
    if (key === name) {
      found = true;
      return pair;
    }
    return part;
  });
  if (!found) {
    rewritten.push(pair);
  }
  return rewritten.join("; ");
}

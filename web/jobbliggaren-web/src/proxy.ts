import { type NextRequest, NextResponse } from "next/server";
import { isProtectedPath } from "@/lib/auth/protected-routes";
import {
  PERSISTENT_MAX_AGE_SECONDS,
  REFRESH_AFTER_COOKIE_NAME,
  SESSION_COOKIE_NAME,
} from "@/lib/auth/cookie-names";
import { env } from "@/lib/env";

// Defense-in-depth (ADR 0017): this proxy (Next 16's renamed middleware; runs on
// the nodejs runtime) blocks unauthenticated noise before it reaches the BE; the
// layout/page re-verifies via getServerSession(). The PROTECTED_PREFIXES list
// mirrors the `(app)` route group — invariant frozen by protected-routes.test.ts
// (security-auditor M-2, 2026-05-24). Matching is segment-boundary-aware (#583) so
// an authed prefix never swallows a public sibling (`/cv` must not gate
// `/cv-granskning`).
//
// It ALSO drives the persistent-session refresh/rotation (PR2b-3b, epic #481): for
// an authenticated request on a protected path it calls POST /auth/refresh at most
// once per REFRESH_THROTTLE_SECONDS. A Persistent session (id rotates every 24h)
// returns `rotated:true` + a new id, which we re-set as a persistent cookie AND
// forward to the downstream render; a Session-scoped one only slides. The refresh
// is best-effort: any failure is swallowed so navigation never breaks.

// Throttle: drive a refresh at most once per 15 min. Chosen well below the backend's
// 24h rotation interval (so a rotation window is never missed) and well above
// per-request (so ordinary browsing does not hammer /auth/refresh). The sliding
// touch this drives is cheap; 15 min keeps the session fresh without amplifying load.
const REFRESH_THROTTLE_SECONDS = 15 * 60;

// On a failed/slow refresh, back off only briefly (not the full window) before the next
// attempt, so a degraded backend is not re-hit on every navigation while recovery stays
// prompt. A truly-gone session self-heals: getServerSession() returns null downstream and
// the render redirects to /logga-in regardless.
const REFRESH_ERROR_BACKOFF_SECONDS = 30;

// Bound the refresh round-trip: a stalled (accepted-but-not-answering) backend must never
// hang a protected navigation. undici's default headersTimeout is ~300s, far too long for a
// hot-path nav; on timeout the fetch rejects with AbortError, which the catch below swallows
// into a plain passthrough (§2.5 hot-path; navigation must never break here).
const REFRESH_TIMEOUT_MS = 2500;

// The __Host- attribute set shared by every cookie this proxy writes. Never weaken
// these: __Host- requires Secure + Path=/ + no Domain, and the session/companion
// cookies stay HttpOnly + SameSite=Strict.
const HOST_COOKIE_ATTRS = {
  httpOnly: true,
  secure: true,
  sameSite: "strict",
  path: "/",
} as const;

interface RefreshResult {
  rotated: boolean;
  sessionId: string | null;
}

export async function proxy(request: NextRequest): Promise<NextResponse> {
  const { pathname } = request.nextUrl;

  // Gated: the refresh driver only runs on protected paths. Public/non-protected
  // paths pass straight through, never touching the backend.
  if (!isProtectedPath(pathname)) {
    return NextResponse.next();
  }

  // Cheap cookie-presence check — actual session validation happens in the Server
  // Component. Per ADR 0017 §defense-in-depth: the proxy blocks unauthenticated
  // noise; the layout re-verifies. Unauthenticated → redirect (unchanged behavior).
  const sessionId = request.cookies.get(SESSION_COOKIE_NAME)?.value;
  if (!sessionId) {
    const loginUrl = new URL("/logga-in", request.url);
    loginUrl.searchParams.set("next", pathname);
    return NextResponse.redirect(loginUrl);
  }

  // Never rotate on a speculative prefetch: it is not a real navigation, and
  // rotating the id for a page the user may never open would churn the session.
  const isPrefetch =
    request.headers.get("next-router-prefetch") !== null ||
    request.headers.get("purpose") === "prefetch";
  if (isPrefetch) {
    return NextResponse.next();
  }

  // Throttle via the non-secret companion cookie (epoch seconds after which the next
  // refresh is due). Absent or elapsed → due; otherwise skip without hitting the BE.
  const nowSeconds = Math.floor(Date.now() / 1000);
  const refreshAfterRaw = request.cookies.get(REFRESH_AFTER_COOKIE_NAME)?.value;
  const refreshAfter = refreshAfterRaw
    ? Number.parseInt(refreshAfterRaw, 10)
    : Number.NaN;
  const refreshDue = !Number.isFinite(refreshAfter) || nowSeconds >= refreshAfter;
  if (!refreshDue) {
    return NextResponse.next();
  }

  // Best-effort refresh/rotation. A network error or non-OK status (e.g. a 401 for
  // an already-expired session) is swallowed: navigation must never break here, and
  // the Server Component re-verifies auth via getServerSession() and redirects if
  // the session is truly gone. On failure we do NOT advance the throttle, so the
  // next real navigation retries and the session recovers promptly.
  let refresh: RefreshResult | null = null;
  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/auth/refresh`, {
      method: "POST",
      headers: { Authorization: `Bearer ${sessionId}` },
      cache: "no-store",
      signal: AbortSignal.timeout(REFRESH_TIMEOUT_MS),
    });
    if (res.ok) {
      refresh = parseRefresh(await res.json());
    }
  } catch {
    // Swallow — see comment above.
  }

  if (!refresh) {
    // Refresh failed (timeout / network error / non-OK / malformed body). Advance the
    // throttle by a short backoff so a degraded backend is not re-hit on every navigation,
    // while recovery stays prompt (a truly-expired session self-heals to /logga-in on the
    // downstream render). The session cookie is left untouched.
    const response = NextResponse.next();
    setThrottleCookie(
      response,
      nowSeconds + REFRESH_ERROR_BACKOFF_SECONDS,
      REFRESH_ERROR_BACKOFF_SECONDS
    );
    response.headers.set("Cache-Control", "no-store");
    return response;
  }

  const nextRefreshAfter = nowSeconds + REFRESH_THROTTLE_SECONDS;

  if (refresh.rotated && refresh.sessionId) {
    const newId = refresh.sessionId;

    // Rewrite the REQUEST cookie so the downstream Server-Component render
    // (getServerSession → /me) reads the NEW id, not the retired one. Per Next
    // issue #57655 the modified headers MUST be forwarded explicitly via
    // NextResponse.next({ request: { headers } }) or the SC sees a stale value.
    // (The backend keeps the old id valid for a 60s grace window, so this is
    // belt-and-suspenders — but it must be correct.)
    const requestHeaders = new Headers(request.headers);
    requestHeaders.set(
      "cookie",
      rewriteCookieHeader(request.headers.get("cookie"), SESSION_COOKIE_NAME, newId)
    );
    const response = NextResponse.next({ request: { headers: requestHeaders } });

    // rotated ⟹ the session was Persistent ⟹ always a persistent cookie.
    response.cookies.set(SESSION_COOKIE_NAME, newId, {
      ...HOST_COOKIE_ATTRS,
      maxAge: PERSISTENT_MAX_AGE_SECONDS,
    });
    setThrottleCookie(response, nextRefreshAfter, REFRESH_THROTTLE_SECONDS);
    // A rotated Set-Cookie must never be cached.
    response.headers.set("Cache-Control", "no-store");
    return response;
  }

  // rotated:false — the session only slid (or was Session-scoped). Advance the
  // throttle and leave the session cookie untouched.
  const response = NextResponse.next();
  setThrottleCookie(response, nextRefreshAfter, REFRESH_THROTTLE_SECONDS);
  response.headers.set("Cache-Control", "no-store");
  return response;
}

// Writes the throttle companion cookie: its VALUE is the epoch second the next refresh is
// due, and its own Max-Age matches that window so it expires exactly when it becomes due.
function setThrottleCookie(
  response: NextResponse,
  dueAtEpochSeconds: number,
  maxAgeSeconds: number
): void {
  response.cookies.set(REFRESH_AFTER_COOKIE_NAME, String(dueAtEpochSeconds), {
    ...HOST_COOKIE_ATTRS,
    maxAge: maxAgeSeconds,
  });
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
function rewriteCookieHeader(
  cookieHeader: string | null,
  name: string,
  value: string
): string {
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

export const config = {
  matcher: [
    "/((?!_next/static|_next/image|favicon.ico|api/).*)",
  ],
};

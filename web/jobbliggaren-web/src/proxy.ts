import { type NextRequest, NextResponse } from "next/server";
import { isProtectedPath } from "@/lib/auth/protected-routes";
import { ADMIN_RETURN_HEADER, adminReturnPath } from "@/lib/auth/admin-return";
import { returnPathFor } from "@/lib/auth/return-path";
import { strictCookieBounce } from "@/lib/auth/strict-cookie-bounce";
import { publicJobAdRoute } from "@/lib/auth/public-job-ad-route";
import { juniorItSignedInRoute } from "@/lib/auth/junior-it-signed-in-route";
import { SESSION_COOKIE_NAME } from "@/lib/auth/cookie-names";
import {
  FAILED_REFRESH,
  UNKNOWN_SESSION,
  forwardedWithSession,
  refreshSession,
  writeRefreshCookies,
} from "@/lib/auth/session-refresh";
import {
  parseNamn,
  buildOrgNrRefusedHref,
  normalizeCodes,
  parseCodeAxis,
  FORETAG_SOK_ROUTE,
  MAX_SNI_CODES,
  MAX_MUNICIPALITY_CODES,
} from "@/lib/company-search/search-params";
import {
  NONCE_REQUEST_HEADER,
  buildContentSecurityPolicy,
  generateNonce,
} from "@/lib/security/security-headers";

// Defense-in-depth (ADR 0017): this proxy (Next 16's renamed middleware; runs on
// the nodejs runtime) blocks unauthenticated noise before it reaches the BE; the
// layout/page re-verifies via getServerSession(). The PROTECTED_PREFIXES list
// mirrors the `(app)` route group — invariant frozen by protected-routes.test.ts
// (security-auditor M-2, 2026-05-24). Matching is segment-boundary-aware (#583) so
// an authed prefix never swallows a public sibling (`/cv` must not gate
// `/cv-granskning`).
//
// It ALSO drives the persistent-session refresh/rotation (PR2b-3b, epic #481) for an
// authenticated request on a protected path, through `lib/auth/session-refresh.ts`: at most
// one POST /auth/refresh per throttle window, a rotated id re-set as a persistent cookie AND
// forwarded to the downstream render, and every failure but a GET's 401 swallowed so
// navigation never breaks. The open junior list's branch (#2284) is its one other caller.
//
// It is ALSO the one writer of the page Content-Security-Policy (ADR 0163): a fresh nonce per
// request goes into the CSP and into an `x-nonce` request header, on the REQUEST so Next stamps the
// nonce on its inline scripts while rendering, and on the RESPONSE so the browser enforces it.
// `proxy()` below does that around `route()`, so no branch of the routing logic can forget it.

export async function proxy(request: NextRequest): Promise<NextResponse> {
  const nonce = generateNonce();
  const csp = buildContentSecurityPolicy(
    process.env.NODE_ENV === "development",
    nonce
  );

  // `set`, never `append`: a Content-Security-Policy or x-nonce the CLIENT sent must not survive
  // into the render, where Next would read its nonce back out of it.
  const forwarded = new Headers(request.headers);
  forwarded.set(NONCE_REQUEST_HEADER, nonce);
  forwarded.set("Content-Security-Policy", csp);

  const response = await route(request, forwarded);
  // Every response, redirects included: one writer, one place. The cookie-bounce document (#2094)
  // is the 200 that needs it; a 3xx simply carries a header nothing reads.
  response.headers.set("Content-Security-Policy", csp);
  return response;
}

/** Pass the request on to the render, carrying the nonce headers `proxy()` prepared. */
function next(forwarded: Headers): NextResponse {
  return NextResponse.next({ request: { headers: forwarded } });
}

async function route(
  request: NextRequest,
  forwarded: Headers
): Promise<NextResponse> {
  const { pathname } = request.nextUrl;
  const sessionId = request.cookies.get(SESSION_COOKIE_NAME)?.value;

  // #2094: a link into a gated page that another site opened arrives without the Strict session cookie. Above the
  // admin branch, which has no cookie gate of its own; `strict-cookie-bounce.ts` carries the rule.
  const bounce = strictCookieBounce(request, Boolean(sessionId));
  if (bounce !== null) return bounce;

  // #2284: the open junior list. With the session cookie a request is sent on to /jobb with the junior filter, after the
  // refresh driver when it is due; without it, a cross-site arrival is handed the cookie first. Disjoint from the bounce
  // by path; `junior-it-signed-in-route.ts` carries the rule.
  const junior = await juniorItSignedInRoute(
    request,
    forwarded,
    sessionId ? (nowSeconds) => refreshSession(request, sessionId, nowSeconds) : null
  );
  if (junior !== null) return junior;

  // ADR 0165: the anonymous twin of `/jobb/<uuid>`. After the bounce, so a cross-site arrival is still handed to the
  // cookie first; before the login redirect below, which a cookieless request to it would otherwise meet.
  const publicJobAd = await publicJobAdRoute(request, forwarded, Boolean(sessionId));
  if (publicJobAd !== null) return publicJobAd;

  // #1979: the (admin) layout has no URL of its own, so the path its login redirect returns to travels in a
  // request header. Written on every /admin request, above the protected-path gate that /admin sits outside,
  // so a value a client sent never reaches the layout.
  const adminReturn = adminReturnPath(request.nextUrl);
  if (adminReturn !== null) {
    const requestHeaders = new Headers(forwarded);
    requestHeaders.set(ADMIN_RETURN_HEADER, adminReturn);
    return next(requestHeaders);
  }

  // Gated: the refresh driver only runs on protected paths. Public/non-protected
  // paths pass straight through, never touching the backend — with one exception, `/junior`
  // with the session cookie, answered above (#2284).
  if (!isProtectedPath(pathname)) {
    return next(forwarded);
  }

  // Cheap cookie-presence check — actual session validation happens in the Server
  // Component. Per ADR 0017 §defense-in-depth: the proxy blocks unauthenticated
  // noise; the layout re-verifies. Unauthenticated → redirect (unchanged behavior).
  if (!sessionId) {
    return NextResponse.redirect(loginUrlFor(request));
  }

  // ADR 0087 D8(c) — the org.nr wash, at the EARLIEST layer that can still refuse to SERVE the
  // refused URL at all. Placed above the prefetch branch on purpose: a speculative prefetch is a
  // request that carries the value just as a navigation does.
  //
  // Why here and not only in the page (measured 2026-07-26, Chromium, JS disabled): a page-level
  // `redirect()` runs after the `(app)` layout has begun streaming, so Next cannot answer 3xx — it
  // answers **200 and serves a document** carrying a meta refresh. That document loads subresources,
  // and `Referrer-Policy: strict-origin-when-cross-origin` puts the refused URL in the `Referer` of
  // SIX requests, including the log line for the washed URL itself. A genuine 307 here serves no
  // document at all.
  //
  // The page-level gate STAYS, and both are load-bearing on different properties: this one keeps the
  // URL from being served; that one keeps the value out of `body.name` should a render ever be
  // reached without passing here — Next's own docs warn that a `matcher` change can silently remove
  // proxy coverage, and rewrites resolve after this layer. That is not two rules — the RULE lives in
  // `search-params.ts` and neither call site may ever grow an inline predicate of its own.
  //
  // BELOW the auth branch deliberately: for an unauthenticated request the login redirect discards
  // the ENTIRE query string, which is a stronger wash than this one (every axis, not just `namn`).
  // That makes the ordering load-bearing rather than incidental — building `next` from `pathname +
  // search`, an ordinary change so a user returns to their search after login, would put the value
  // into `?next=` and from there into the login page's DOM. Pinned in both directions in
  // `proxy.test.ts`.
  if (pathname === FORETAG_SOK_ROUTE) {
    const params = request.nextUrl.searchParams;
    if (parseNamn(params.getAll("namn")).kind === "orgNrShaped") {
      const washed = buildOrgNrRefusedHref({
        // `parseCodeAxis`, not raw `getAll`, so this marshals byte-identically to the page gate —
        // otherwise `?sni=&sni=62010` would yield two different wash targets from the one rule.
        // Reference-free (dedupe + cap only): the proxy has no SCB reference, and the
        // reference-based drop-unknown applies on the render that follows the redirect.
        sni: normalizeCodes(parseCodeAxis(params.getAll("sni")), MAX_SNI_CODES),
        kommun: normalizeCodes(
          parseCodeAxis(params.getAll("kommun")),
          MAX_MUNICIPALITY_CODES,
        ),
      });
      const response = NextResponse.redirect(new URL(washed, request.url));
      // A refusal must never be served from a cache — the value is in the request line.
      response.headers.set("Cache-Control", "no-store");
      return response;
    }
  }

  // The refresh driver (`lib/auth/session-refresh.ts`): a prefetch or a refresh not yet due
  // passes straight through; anything else calls the backend at most once per window.
  const nowSeconds = Math.floor(Date.now() / 1000);
  const refresh = await refreshSession(request, sessionId, nowSeconds);
  switch (refresh.kind) {
    case "notDue":
      return next(forwarded);
    case "unknownSession":
      // A GET answered 401 goes to the login page with its path (#1893, `expiredSessionRedirect`).
      // A Server Action POST must never be answered with a redirect, so every other method is
      // treated as any other failure: the Server Component re-verifies auth via getServerSession().
      if (request.method === "GET") return expiredSessionRedirect(request, nowSeconds);
      return writeRefreshCookies(next(forwarded), FAILED_REFRESH, nowSeconds);
    case "rotated":
      return writeRefreshCookies(
        next(forwardedWithSession(forwarded, request.headers.get("cookie"), refresh.sessionId)),
        refresh,
        nowSeconds
      );
    case "failed":
    case "slid":
      return writeRefreshCookies(next(forwarded), refresh, nowSeconds);
  }
}

function loginUrlFor(request: NextRequest): URL {
  const loginUrl = new URL("/logga-in", request.url);
  const next = returnPathFor(request.nextUrl);
  if (next !== null) loginUrl.searchParams.set("next", next);
  return loginUrl;
}

/**
 * #1893: the server no longer knows the session the cookie names. Only an exact 401 on a GET gets here; a 429, a
 * 503 or a timeout says nothing about the session, and a Server Action POST must never be answered with a
 * redirect. The cookie goes on the way, so the next request does not ask again, and the login returns to the page.
 */
function expiredSessionRedirect(request: NextRequest, nowSeconds: number): NextResponse {
  return writeRefreshCookies(NextResponse.redirect(loginUrlFor(request)), UNKNOWN_SESSION, nowSeconds);
}

// Only Next's own static assets are excluded, anchored so that a look-alike (`/favicon.icoX`,
// `/_next/imageX`) is not, and `/api/` (its policy is the static one in next.config.ts, ADR 0163).
// NOT excluded: any path by file extension — a dynamic route answers to `/jobb/abc.png` too, and an
// HTML document on a path the visitor chose must carry the policy. Nor Next's `missing:` prefetch
// filter: the refresh driver has its own prefetch rule and skipping the proxy for a prefetch would
// skip the auth gate.
export const config = {
  matcher: ["/((?!_next/static/|_next/image$|favicon\\.ico$|api/).*)"],
};

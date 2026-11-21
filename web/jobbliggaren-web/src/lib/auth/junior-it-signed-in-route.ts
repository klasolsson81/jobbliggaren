import { NextResponse, type NextRequest } from "next/server";
import { juniorItHandover, type JuniorItHandover } from "@/lib/job-ads/junior-it-handover";
import { isCrossSiteDocumentNavigation, isPrefetch } from "@/lib/http/fetch-metadata";
import { continuationAnswer } from "./continuation-answer";
import { BOUNCE_MARKER } from "./bounce-marker";
import { forwardedWithoutSession, writeRefreshCookies, type SessionRefresh } from "./session-refresh";

/** One pass of the shared refresh driver, bound to the request's session cookie by the proxy. */
export type BoundSessionRefresh = (nowSeconds: number) => Promise<SessionRefresh>;

/**
 * The proxy's one branch for the open junior list, `/junior` (#2284, Klas 2026-11-21): a visitor who is signed in is
 * sent on to the search page with the junior filter, also when another site opened the link. The page itself is
 * unchanged and reads no session (ADR 0186); this branch decides which requests reach it.
 *
 * It acts only on a query the page accepts (`juniorItHandover`), and sends on only validated values:
 * - **With the session cookie,** a GET or HEAD gets a `307` to `/jobb` with the junior filter, the kommun and the
 *   order. The shared refresh driver runs first when it is due, and its cookies ride the redirect. On an exact 401 the
 *   session is gone: both cookies are cleared and the request is answered as one without them.
 * - **Without it,** a cross-site document navigation gets the continuation document, since the browser withholds the
 *   `SameSite=Strict` cookie there. Its hop carries the marker. The hop that comes back without the cookie is sent to
 *   the list's clean address, but only when it is a navigation that is not cross-site, the exact complement of the
 *   bounce's own condition. A cross-site hop, from a sandboxed popup with an opaque origin, keeps the marker and is
 *   served where it is, so neither the bounce nor the strip can repeat. A request without Fetch Metadata, a link
 *   previewer's included, is served the page as before.
 *
 * `refresh` is null when no session cookie arrived. The branch never reads a cookie itself: the proxy binds the driver
 * to the cookie, and the driver writes and clears it.
 *
 * Null when the request is anything else, and the proxy carries on as before.
 */
export async function juniorItSignedInRoute(
  request: NextRequest,
  forwarded: Headers,
  refresh: BoundSessionRefresh | null
): Promise<NextResponse | null> {
  const handover = juniorItHandover(request.nextUrl);
  if (handover === null) return null;

  if (refresh === null) return answerWithoutSession(request, handover);

  if (request.method !== "GET" && request.method !== "HEAD") return null;

  const nowSeconds = Math.floor(Date.now() / 1000);
  const outcome = await refresh(nowSeconds);
  if (outcome.kind !== "unknownSession") {
    return writeRefreshCookies(redirectTo(handover.jobb, request), outcome, nowSeconds);
  }

  // Exactly 401 (#1893): the cookie names a session the server no longer knows. The render that may follow never
  // carries the dead id.
  const answer =
    stripMarker(request, handover) ?? NextResponse.next({ request: { headers: forwardedWithoutSession(forwarded) } });
  return writeRefreshCookies(answer, outcome, nowSeconds);
}

function answerWithoutSession(request: NextRequest, handover: JuniorItHandover): NextResponse | null {
  if (request.method !== "GET") return null;
  if (request.nextUrl.searchParams.has(BOUNCE_MARKER)) return stripMarker(request, handover);
  return isCrossSiteDocumentNavigation(request.headers) ? continuationAnswer(request, handover.junior) : null;
}

/** The hop back without a session, as a browser navigation that is not cross-site: on to the list's clean address. */
function stripMarker(request: NextRequest, handover: JuniorItHandover): NextResponse | null {
  if (request.method !== "GET" || !request.nextUrl.searchParams.has(BOUNCE_MARKER)) return null;
  const { headers } = request;
  const site = headers.get("sec-fetch-site");
  const notCrossSite = site === "same-origin" || site === "same-site" || site === "none";
  const navigation = headers.get("sec-fetch-mode") === "navigate" && headers.get("sec-fetch-dest") === "document";
  return notCrossSite && navigation && !isPrefetch(headers) ? redirectTo(handover.junior, request) : null;
}

/** A same-origin `307` that no cache may keep: where it leads depends on the session. */
function redirectTo(path: string, request: NextRequest): NextResponse {
  const response = NextResponse.redirect(new URL(path, request.url));
  response.headers.set("Cache-Control", "no-store");
  return response;
}

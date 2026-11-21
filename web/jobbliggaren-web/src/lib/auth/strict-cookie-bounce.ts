import { NextResponse, type NextRequest } from "next/server";
import { isCrossSiteDocumentNavigation } from "@/lib/http/fetch-metadata";
import { continuationAnswer, isSameSitePath } from "./continuation-answer";
import { returnPathFor } from "./return-path";
import { BOUNCE_MARKER } from "./bounce-marker";

/**
 * The answer to a link into a gated page that another site opened, a notification mail's link in webmail above
 * all (#2094, ADR 0018 Amendment 2026-10-10).
 *
 * The session cookie is `SameSite=Strict`, so the browser withholds it on a navigation another site started, and
 * a signed-in user would be sent to the login page. A cross-site top-level GET on a protected or admin path that
 * arrives without the cookie is therefore answered with the continuation document. Its refresh starts on this
 * origin, so the next request carries the cookie if the browser has one; if it has none, that request meets the
 * ordinary login redirect. The cookie itself stays Strict: no subresource, frame, fetch or POST gains it.
 *
 * The target is the return path, never the request's own query, so a search value is neither served in the
 * document nor replayed onto the page. The hop carries {@link BOUNCE_MARKER}: a hop that comes back cross-site
 * again, from a sandboxed popup with an opaque origin, is never bounced twice, and a hop that comes back with the
 * cookie is sent on to the clean URL.
 *
 * Null when the request is anything else, and the proxy carries on as before.
 */
export function strictCookieBounce(request: NextRequest, hasSession: boolean): NextResponse | null {
  if (request.method !== "GET") return null;

  const target = returnPathFor(request.nextUrl);
  if (target === null || !isSameSitePath(target)) return null;

  if (request.nextUrl.searchParams.has(BOUNCE_MARKER)) {
    if (!hasSession) return null;
    const response = NextResponse.redirect(new URL(target, request.url));
    response.headers.set("Cache-Control", "no-store");
    return response;
  }

  if (hasSession || !isCrossSiteDocumentNavigation(request.headers)) return null;

  return continuationAnswer(request, target);
}

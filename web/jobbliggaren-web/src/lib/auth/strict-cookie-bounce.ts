import { NextResponse, type NextRequest } from "next/server";
import enMetadata from "../../../messages/en/metadata.json";
import enPages from "../../../messages/en/pages.json";
import svMetadata from "../../../messages/sv/metadata.json";
import svPages from "../../../messages/sv/pages.json";
import { defaultLocale, isLocale, LOCALE_COOKIE, type Locale } from "@/i18n/routing";
import { isCrossSiteDocumentNavigation } from "@/lib/http/fetch-metadata";
import { buildContinuationDocument } from "./continuation-document";
import { hasControlCharacterOrBackslash } from "./safe-redirect";
import { returnPathFor } from "./return-path";

/**
 * The query key the bounce's own hop carries, so a request that already came back is never bounced again. Its
 * value is a constant and names nothing.
 */
export const BOUNCE_MARKER = "vidare";

const PARSE_BASE = new URL("https://jobbliggaren.invalid");

const MAX_TARGET_LENGTH = 2048;

// Read from the catalogues directly: the proxy has no request scope for next-intl.
const STRINGS: Readonly<Record<Locale, { readonly heading: string; readonly continueLabel: string }>> = {
  sv: { heading: svMetadata.applicationName, continueLabel: svPages.auth.passwordless.external.continue },
  en: { heading: enMetadata.applicationName, continueLabel: enPages.auth.passwordless.external.continue },
};

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

  const locale = localeOf(request);
  const { heading, continueLabel } = STRINGS[locale];
  return new NextResponse(
    buildContinuationDocument({ lang: locale, title: heading, heading, continueLabel, target: hopTarget(target) }),
    {
      status: 200,
      headers: {
        "Content-Type": "text/html; charset=utf-8",
        "Cache-Control": "no-store",
        "Referrer-Policy": "no-referrer",
        "X-Robots-Tag": "noindex",
        // The page this hop leads to now depends on the session. The opener's handle on the window would read
        // that difference; as the window's first document, this one moves it into a group of its own first.
        "Cross-Origin-Opener-Policy": "same-origin",
      },
    }
  );
}

/** One slash, then a path on this origin; no backslash or control character, and nothing a browser reads as a host. */
function isSameSitePath(target: string): boolean {
  if (target.length > MAX_TARGET_LENGTH || !target.startsWith("/") || target.startsWith("//")) return false;
  if (hasControlCharacterOrBackslash(target)) return false;
  return new URL(target, PARSE_BASE).origin === PARSE_BASE.origin;
}

/** The hop's URL: the return path with {@link BOUNCE_MARKER}. Exported for the edge-log inventory. */
export function hopTarget(target: string): string {
  const url = new URL(target, PARSE_BASE);
  url.searchParams.set(BOUNCE_MARKER, "1");
  return `${url.pathname}${url.search}`;
}

/** `NEXT_LOCALE` is `SameSite=Lax`, so it rides the cross-site navigation the session cookie does not. */
function localeOf(request: NextRequest): Locale {
  const value = request.cookies.get(LOCALE_COOKIE)?.value;
  return isLocale(value) ? value : defaultLocale;
}

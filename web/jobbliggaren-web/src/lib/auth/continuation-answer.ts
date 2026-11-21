import { NextResponse, type NextRequest } from "next/server";
import enMetadata from "../../../messages/en/metadata.json";
import enPages from "../../../messages/en/pages.json";
import svMetadata from "../../../messages/sv/metadata.json";
import svPages from "../../../messages/sv/pages.json";
import { defaultLocale, isLocale, LOCALE_COOKIE, type Locale } from "@/i18n/routing";
import { buildContinuationDocument } from "./continuation-document";
import { BOUNCE_MARKER } from "./bounce-marker";

const PARSE_BASE = new URL("https://jobbliggaren.invalid");

// Read from the catalogues directly: the proxy has no request scope for next-intl.
const STRINGS: Readonly<Record<Locale, { readonly heading: string; readonly continueLabel: string }>> = {
  sv: { heading: svMetadata.applicationName, continueLabel: svPages.auth.passwordless.external.continue },
  en: { heading: enMetadata.applicationName, continueLabel: enPages.auth.passwordless.external.continue },
};

/**
 * The 200 that answers a top-level navigation another site started without the `SameSite=Strict` session cookie
 * (ADR 0018, Amendment 2026-10-10): the continuation document, whose refresh starts on this origin and so carries the
 * cookie if the browser holds one. The caller decides when to answer with it (`strict-cookie-bounce.ts`) and writes
 * neither the document nor its headers itself.
 *
 * `target` is a same-site path the caller built. The refresh goes to {@link hopTarget}`(target)`, so the hop always
 * carries {@link BOUNCE_MARKER}: a request that carries it is never answered with this document again.
 */
export function continuationAnswer(request: NextRequest, target: string): NextResponse {
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

/** The hop's URL: `target` with {@link BOUNCE_MARKER}. Exported for the edge-log inventory. */
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

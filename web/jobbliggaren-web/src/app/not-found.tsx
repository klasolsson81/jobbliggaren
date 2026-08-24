import Link from "next/link";
import { NextIntlClientProvider } from "next-intl";
import { getLocale, getMessages, getTranslations } from "next-intl/server";
import { pickClientMessages } from "@/i18n/client-messages";
import { SiteHeader } from "@/components/site/site-header";
import { SiteFooter } from "@/components/site/site-footer";

/**
 * Root not-found — the boundary for UNMATCHED URLs across the whole site
 * (Next file convention: the root app/not-found handles every URL no route
 * matches, e.g. a guessed /cvmall). notFound() calls INSIDE the signed-in app
 * are caught earlier by (app)/not-found.tsx, which keeps the app shell, and the
 * guest mirrors by (guest)/gast/not-found.tsx; this file is the last line.
 *
 * It carries the public site frame — SiteHeader, SiteFooter and a way back —
 * because a visitor who mistypes a URL must not land outside it (#1477).
 *
 * It renders its OWN NextIntlClientProvider, and that is the load-bearing part:
 * SiteHeader mounts the client-side <LanguageSwitcher/>, which reads `common`.
 * The root layout's payload is deliberately EMPTY (#737, ADR 0045 Beslut 6) and
 * is paid by every document in the app, so charging `common` there to feed one
 * 404 would re-inflate all of them. A provider HERE is a boundary of exactly
 * this file, so only the 404 document pays — the same shape global-error.tsx
 * uses, and client-namespace-payload.test.ts verifies the declaration against
 * the import graph.
 */
export default async function RootNotFound() {
  const t = await getTranslations("fallback");
  const locale = await getLocale();
  const messages = pickClientMessages(await getMessages(), ["common"]);

  return (
    <NextIntlClientProvider locale={locale} messages={messages}>
      <div className="flex min-h-screen flex-col bg-surface-primary text-text-primary">
        <SiteHeader />
        {/* SiteHeader renders the surface's skip link; this is its target. */}
        <main
          id="main"
          tabIndex={-1}
          className="jp-container jp-page flex flex-1 flex-col justify-center gap-4 focus:outline-none"
        >
          <h1 className="jp-h1">{t("notFound.title")}</h1>
          <p className="jp-lede">{t("notFound.body")}</p>
          <div>
            <Link href="/" className="jp-btn jp-btn--secondary">
              {t("notFound.toStart")}
            </Link>
          </div>
        </main>
        <SiteFooter />
      </div>
    </NextIntlClientProvider>
  );
}

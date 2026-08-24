import Link from "next/link";
import { ChevronLeft } from "lucide-react";
import { NextIntlClientProvider } from "next-intl";
import { getLocale, getMessages, getTranslations } from "next-intl/server";
import { pickClientMessages } from "@/i18n/client-messages";
import { SiteHeader } from "@/components/site/site-header";
import { SiteFooter } from "@/components/site/site-footer";

/**
 * Auth layout — wraps every route in `(auth)` (/logga-in, /registrera,
 * /glomt-losenord, /aterstall-losenord, /bekrafta-epost, /bekrafta-konto) in
 * SiteHeader (brand link to /) + SiteFooter. Klas-direktiv 2026-05-24: these
 * pages get the same "vanliga layout" as the marketing pages so a visitor can
 * always navigate back.
 *
 * The form is centred in a max-w-sm block, with a back link above it: the brand
 * logo is a way home that only an experienced visitor recognises as one, and
 * these pages must not need the browser's back button (Klas 2026-08-23, #1477).
 * The link sits in the LAYOUT, not on each page, so (auth)/error.tsx keeps it
 * on screen too — which is why that boundary offers only a retry.
 *
 * SiteHeader hides its "Logga in" action here (`showLogin={false}` — a link to
 * the page you are on is not an action).
 *
 * SiteHeader (LP-5a / #258) renders the first skip link to `#main`; this
 * layout's `<main>` carries that target (`id="main"`, programmatically
 * focusable).
 */
export default async function AuthLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  // #737 — see client-messages.ts: this boundary declares the namespaces its
  // own client subtree reads (login form + shared site chrome). Root carries
  // nothing, and a nested provider replaces context rather than merging, so the
  // set must be complete for the subtree. Verified for EQUALITY against the
  // import graph by client-namespace-payload.test.ts.
  const locale = await getLocale();
  const messages = pickClientMessages(await getMessages(), ["common", "fallback", "pages"]);
  const t = await getTranslations("pages");

  return (
    <NextIntlClientProvider locale={locale} messages={messages}>
      <div className="flex min-h-screen flex-col bg-surface-secondary text-text-primary">
        <SiteHeader showLogin={false} />
        <main
          id="main"
          tabIndex={-1}
          className="flex flex-1 items-center justify-center px-6 py-12 focus:outline-none"
        >
          <div className="w-full max-w-sm">
            <Link href="/" className="jp-backlink mb-4">
              <ChevronLeft size={16} aria-hidden="true" />
              <span>{t("auth.backToStart")}</span>
            </Link>
            {children}
          </div>
        </main>
        <SiteFooter />
      </div>
    </NextIntlClientProvider>
  );
}

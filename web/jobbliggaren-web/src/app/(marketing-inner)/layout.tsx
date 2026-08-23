import type { ReactNode } from "react";
import { NextIntlClientProvider } from "next-intl";
import { getLocale, getMessages } from "next-intl/server";
import { pickClientMessages } from "@/i18n/client-messages";
import { SiteHeader } from "@/components/site/site-header";
import { SiteFooter } from "@/components/site/site-footer";

/**
 * Layout för inre marketing-sidor (/vantelista, /villkor, /cookies). Delar
 * SiteHeader (brand-länk + login) och SiteFooter (den delade djupgröna
 * sidfoten, LP-3/#256) så navigering tillbaka till landing alltid är möjlig.
 * Klas-direktiv 2026-05-24 efter Steg 5-svans visual-verify: "vanliga
 * layouten" på inre sidor.
 *
 * Landing-routen (`/`) sitter i (marketing)-grupp och monterar samma SiteHeader
 * med stats (LP-4 / #257) — inte i denna layout.
 *
 * SiteHeader (LP-5a / #258) renderar en första skip-länk till `#main`.
 * Skip-målet `#main` bär nu varje sidas eget `<main>`-landmärke (#284) — varje
 * marketing-inner-sida exponerar ett `<main id="main">` som omsluter både sin
 * page-hero (en `<section>`-region, inte längre en andra banner) och innehållet,
 * i paritet med app/admin/landning. Wrappern nedan är bara en flex-spacer.
 */
export default async function MarketingInnerLayout({
  children,
}: {
  children: ReactNode;
}) {
  // #737 — the marketing-inner pages render their copy SERVER-side (the whole
  // `content-*` family via getTranslations), so their client payload is just
  // the shared site chrome: one namespace instead of the 13 every document used
  // to carry. Verified for EQUALITY against the import graph by
  // client-namespace-payload.test.ts.
  const locale = await getLocale();
  const messages = pickClientMessages(await getMessages(), ["common"]);

  return (
    <NextIntlClientProvider locale={locale} messages={messages}>
      <div className="flex min-h-screen flex-col bg-surface-primary text-text-primary">
        <SiteHeader />
        <div className="flex-1">{children}</div>
        <SiteFooter />
      </div>
    </NextIntlClientProvider>
  );
}

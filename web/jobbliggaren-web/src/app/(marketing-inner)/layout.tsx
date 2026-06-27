import type { ReactNode } from "react";
import { SiteHeader } from "@/components/site/site-header";
import { SiteFooter } from "@/components/site/site-footer";

/**
 * Layout för inre marketing-sidor (/vantelista, /villkor, /cookies). Delar
 * SiteHeader (brand-länk + login) och SiteFooter (den delade djupgröna
 * sidfoten, LP-3/#256) så navigering tillbaka till landing alltid är möjlig.
 * Klas-direktiv 2026-05-24 efter Steg 5-svans visual-verify: "vanliga
 * layouten" på inre sidor.
 *
 * Landing-routen (`/`) sitter i (marketing)-grupp och har egen LandingHeader
 * med stats (LP-4 / #257) — inte i denna layout.
 *
 * SiteHeader (LP-5a / #258) renderar en första skip-länk till `#main`.
 * Skip-målet `#main` bär nu varje sidas eget `<main>`-landmärke (#284) — varje
 * marketing-inner-sida exponerar ett `<main id="main">` som omsluter både sin
 * page-hero (en `<section>`-region, inte längre en andra banner) och innehållet,
 * i paritet med app/admin/landning. Wrappern nedan är bara en flex-spacer.
 */
export default function MarketingInnerLayout({
  children,
}: {
  children: ReactNode;
}) {
  return (
    <div className="flex min-h-screen flex-col bg-surface-primary text-text-primary">
      <SiteHeader />
      <div className="flex-1">{children}</div>
      <SiteFooter />
    </div>
  );
}

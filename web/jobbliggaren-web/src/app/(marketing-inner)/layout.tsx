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
 * SiteHeader (LP-5a / #258) renderar en första skip-länk till `#main`;
 * innehållswrappern nedan bär det målet (`id="main"`). Sidorna behåller sina
 * egna `<main>`-landmärken innanför wrappern.
 */
export default function MarketingInnerLayout({
  children,
}: {
  children: ReactNode;
}) {
  return (
    <div className="flex min-h-screen flex-col bg-surface-primary text-text-primary">
      <SiteHeader />
      <div id="main" tabIndex={-1} className="flex-1 focus:outline-none">
        {children}
      </div>
      <SiteFooter />
    </div>
  );
}

import type { ReactNode } from "react";
import { SiteHeader } from "@/components/site/site-header";
import { SiteFooter } from "@/components/site/site-footer";

/**
 * Layout för inre marketing-sidor (/vantelista, /villkor, /cookies). Delar
 * SiteHeader (brand-länk + login) och SiteFooter (basic-länkrad) så
 * navigering tillbaka till landing alltid är möjlig. Klas-direktiv 2026-05-24
 * efter Steg 5-svans visual-verify: "vanliga layouten" på inre sidor.
 *
 * Landing-routen (`/`) sitter i (marketing)-grupp och har egen LandingTopbar
 * med stats — inte i denna layout.
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

import { LandingTopbar } from "@/components/landing/landing-topbar";
import { LandingHeroSection } from "@/components/landing/landing-hero-section";
import { LandingFeatures } from "@/components/landing/landing-features";
import { LandingFooter } from "@/components/landing/landing-footer";
import { getLandingStats } from "@/components/landing/landing-stats";

/**
 * Landing-routen (`/`) — v3-refactor (F6 Prompt 1, ADR 0056) + live-stats
 * (F6 P5 Punkt 3, ADR 0064).
 *
 * Ren async server-component-shell. Komponenterna delar uppgifter:
 *  - LandingTopbar (RSC): vit header med brand + live-stats (prop-driven)
 *  - LandingHeroSection (client): navy hero + copy + AuthCard (mode-state)
 *  - LandingFeatures (RSC): "Funktioner"-sektion (4 mono-key/text-rader)
 *  - LandingFooter (RSC-komposit): länkrad + ThemeToggle + LangToggle
 *
 * Live-stats hämtas server-side via `getLandingStats()` (ADR 0064 — anropar
 * publik `GET /api/v1/landing/stats`, pre-computed Redis-cache via Worker-cron).
 * Vid fetch-fail returneras floor-värden — sidan renderar alltid räknor.
 *
 * v2-implementationens header med auth/theme/lang-knappar, "Version 2"-
 * kicker, "Drift"-indikator i footer och "Så funkar det"-sektion är BORT
 * per HANDOVER-v3 §7.1 "Bort:" + §0 punkt 6+7 + §6.4.
 *
 * Kvarvarande FAS-DEFERRALs (ej blocker):
 *  - OAuth: knappar finns, fullt flöde i senare fas.
 *  - Glömt-lösenord: ingår i LoginForm (befintlig markup).
 */
export default async function LandingPage() {
  const stats = await getLandingStats();
  return (
    <div className="flex min-h-screen flex-col bg-surface-primary text-text-primary">
      <LandingTopbar stats={stats} />
      <main className="flex-1">
        <LandingHeroSection />
        <LandingFeatures />
      </main>
      <LandingFooter />
    </div>
  );
}

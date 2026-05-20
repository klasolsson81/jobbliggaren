import { LandingTopbar } from "@/components/landing/landing-topbar";
import { LandingHeroSection } from "@/components/landing/landing-hero-section";
import { LandingFeatures } from "@/components/landing/landing-features";
import { LandingFooter } from "@/components/landing/landing-footer";

/**
 * Landing-routen (`/`) — v3-refactor (F6 Prompt 1, ADR 0056).
 *
 * Ren server-component-shell. Komponenterna delar uppgifter:
 *  - LandingTopbar (RSC): vit header med brand + live-stats
 *  - LandingHeroSection (client): navy hero + copy + AuthCard (mode-state)
 *  - LandingFeatures (RSC): "Funktioner"-sektion (4 mono-key/text-rader)
 *  - LandingFooter (RSC-komposit): länkrad + ThemeToggle + LangToggle
 *
 * v2-implementationens header med auth/theme/lang-knappar, "Version 2"-
 * kicker, "Drift"-indikator i footer och "Så funkar det"-sektion är BORT
 * per HANDOVER-v3 §7.1 "Bort:" + §0 punkt 6+7 + §6.4.
 *
 * Klas pre-F6 Prompt 1 FAS-DEFERRALs (ej blocker):
 *  - Live-stats: hårdkodad konstant (`getLandingStats()` placeholder).
 *  - OAuth: knappar finns, fullt flöde i senare fas.
 *  - Glömt-lösenord: ingår i LoginForm (befintlig markup).
 */
export default function LandingPage() {
  return (
    <div className="flex min-h-screen flex-col bg-surface-primary text-text-primary">
      <LandingTopbar />
      <main className="flex-1">
        <LandingHeroSection />
        <LandingFeatures />
      </main>
      <LandingFooter />
    </div>
  );
}

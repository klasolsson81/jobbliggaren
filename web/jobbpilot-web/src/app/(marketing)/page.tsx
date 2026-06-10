import { LandingTopbar } from "@/components/landing/landing-topbar";
import { LandingHeroSection } from "@/components/landing/landing-hero-section";
import { LandingFeatures } from "@/components/landing/landing-features";
import { LandingFooter } from "@/components/landing/landing-footer";
import { getLandingStats } from "@/components/landing/landing-stats";
import { getServerSession } from "@/lib/auth/session";

/**
 * Landing-routen (`/`) — v3-refactor (F6 Prompt 1, ADR 0056) + live-stats
 * (F6 P5 Punkt 3, ADR 0064).
 *
 * Ren async server-component-shell. Komponenterna delar uppgifter:
 *  - LandingTopbar (RSC): vit header med brand + live-stats + "Logga in"-länk
 *  - LandingHeroSection (client): ljus produkt-forward hero + copy + CTA +
 *    statisk produkt-peek (G4-redesign, CTO Riktning A)
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
 * G4-redesign: AuthCard (inline login + OAuth-stubs) borttagen ur hero —
 * inloggning sker nu via "Logga in"-länken i topbar → `/logga-in` (den fulla
 * auth-routen där LoginForm + OAuth bor). Hero är ren produkt-orientering.
 */
export default async function LandingPage() {
  // Parallell fetch: stats är publik (anonym), session är cookie-baserad — båda
  // krävs för render-grenarna nedan. Independent → Promise.all (Klas-direktiv
  // perf-disciplin). F-Pre Punkt 5 (CTO Beslut 2): inloggad-state styr CTA-
  // texten + target ("Till översikt" vs "Utforska som gäst").
  const [stats, user] = await Promise.all([getLandingStats(), getServerSession()]);
  return (
    <div className="flex min-h-screen flex-col bg-surface-primary text-text-primary">
      <LandingTopbar stats={stats} />
      <main className="flex-1">
        <LandingHeroSection isAuthenticated={user !== null} />
        <LandingFeatures />
      </main>
      <LandingFooter />
    </div>
  );
}

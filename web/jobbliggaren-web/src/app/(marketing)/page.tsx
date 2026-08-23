import { LandingHeroSection } from "@/components/landing/landing-hero-section";
import { LandingFeatures } from "@/components/landing/landing-features";
import { SiteHeader } from "@/components/site/site-header";
import { SiteFooter } from "@/components/site/site-footer";
import { getLandingStats } from "@/components/landing/landing-stats";

/**
 * Landing route (`/`) — "Liggaren" redesign (epic #267, LP-4 / #257). The
 * (marketing) layout owns ONLY the client i18n payload (#737) and deliberately
 * renders no chrome, so the landing mounts the shared header and footer here.
 * Moving chrome into that layout would double the header/footer.
 *
 * Async RSC shell composing:
 *  - <SiteHeader/> (`.jp-head`): brand + live Platsbanken stats. It renders the
 *    surface's own <SkipLink/> as its first child, so this page must not render
 *    a second one (#1476 merged the former LandingHeader in; the skip link came
 *    with it).
 *  - <LandingHeroSection/>: the ledger hero with the inline Suspense-wrapped
 *    <AuthCard/> + a guest link
 *  - <LandingFeatures/>: the six feature cells
 *  - <SiteFooter/> (LP-3, #256): the one shared deep-green footer
 *
 * `showLogin={false}` is not a leftover: AuthCard's own "Logga in" tab is on
 * this page, and two controls carrying that label with different behaviour —
 * one navigating, one switching a panel in place — is the defect, not the
 * missing header action. It turns on in the wave that removes the card.
 *
 * Live stats are fetched server-side via `getLandingStats()` (ADR 0064, public
 * Redis-cached endpoint). An unmeasured count is `null` and the header renders
 * the whole stats group as absence — never a floor value (CTO-bind 2026-07-13).
 */
export default async function LandingPage() {
  const stats = await getLandingStats();
  return (
    <div className="flex min-h-screen flex-col bg-surface-primary text-text-primary">
      <SiteHeader stats={stats} showLogin={false} />
      <main id="main" tabIndex={-1} className="flex-1 focus:outline-none">
        <LandingHeroSection />
        <LandingFeatures />
      </main>
      <SiteFooter />
    </div>
  );
}

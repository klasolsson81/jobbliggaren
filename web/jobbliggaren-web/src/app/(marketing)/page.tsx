import { useTranslations } from "next-intl";
import { LandingHeader } from "@/components/landing/landing-header";
import { LandingHeroSection } from "@/components/landing/landing-hero-section";
import { LandingFeatures } from "@/components/landing/landing-features";
import { SiteFooter } from "@/components/site/site-footer";
import { getLandingStats } from "@/components/landing/landing-stats";

/**
 * Skip link to the main landmark. A tiny sync sub-component so the async page
 * shell does not need the `getTranslations` server API for one static string —
 * `useTranslations` resolves synchronously in this RSC and stays renderable in
 * jsdom tests via the shared next-intl provider. Mirrors the shells' first-
 * focusable skip-link pattern (`#main` target, `sr-only focus:` reveal).
 */
function LandingSkipLink() {
  const t = useTranslations("landing");
  return (
    <a
      href="#main"
      className="sr-only focus:not-sr-only focus:absolute focus:top-2 focus:left-2 focus:z-50 focus:rounded-sm focus:bg-surface-secondary focus:px-3 focus:py-2 focus:text-body-sm focus:text-text-primary focus:outline-2 focus:outline-offset-2 focus:outline-ring"
    >
      {t("common.skipToContent")}
    </a>
  );
}

/**
 * Landing route (`/`) — "Liggaren" redesign (epic #267, LP-4 / #257). The
 * (marketing) group has no layout.tsx, so the landing mounts its own header and
 * the shared footer here (LP-4 is the sole owner of the landing header/footer
 * mount; LP-3/LP-5a never touch the landing surface).
 *
 * Async RSC shell composing:
 *  - a skip link to `#main` (the (marketing) group has no layout to carry it)
 *  - <LandingHeader/> (`.jp-head`): brand + live Platsbanken stats, no login link
 *  - <LandingHeroSection/>: the ledger hero with the inline Suspense-wrapped
 *    <AuthCard/> (the single account action) + a guest link
 *  - <LandingFeatures/>: the four mono-key feature rows
 *  - <SiteFooter/> (LP-3, #256): the one shared deep-green footer
 *
 * Live stats are fetched server-side via `getLandingStats()` (ADR 0064, public
 * Redis-cached endpoint); on fetch failure floor values render, so the count is
 * always present. Registration is OPEN (Klas 2026-06-27): the AuthCard mounts
 * with no posture prop, default tab "Skapa konto" is a live signup.
 */
export default async function LandingPage() {
  const stats = await getLandingStats();
  return (
    <div className="flex min-h-screen flex-col bg-surface-primary text-text-primary">
      <LandingSkipLink />
      <LandingHeader stats={stats} />
      <main id="main" tabIndex={-1} className="flex-1 focus:outline-none">
        <LandingHeroSection />
        <LandingFeatures />
      </main>
      <SiteFooter />
    </div>
  );
}

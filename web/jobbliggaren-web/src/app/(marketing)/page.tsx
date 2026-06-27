import { getTranslations } from "next-intl/server";
import { LandingHeader } from "@/components/landing/landing-header";
import { LandingHeroSection } from "@/components/landing/landing-hero-section";
import { LandingFeatures } from "@/components/landing/landing-features";
import { SiteFooter } from "@/components/site/site-footer";
import { SkipLink } from "@/components/site/skip-link";
import { getLandingStats } from "@/components/landing/landing-stats";

/**
 * Landing route (`/`) — "Liggaren" redesign (epic #267, LP-4 / #257). The
 * (marketing) group has no layout.tsx, so the landing mounts its own header and
 * the shared footer here (LP-4 is the sole owner of the landing header/footer
 * mount; LP-3/LP-5a never touch the landing surface).
 *
 * Async RSC shell composing:
 *  - the shared <SkipLink/> to `#main`, rendered first-focusable (the (marketing)
 *    group has no layout to carry it); label resolved via `getTranslations` like
 *    the other async surfaces ((app)/(admin) layouts) — #284 fold-in, epic #267
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
  const t = await getTranslations("landing");
  return (
    <div className="flex min-h-screen flex-col bg-surface-primary text-text-primary">
      <SkipLink label={t("common.skipToContent")} />
      <LandingHeader stats={stats} />
      <main id="main" tabIndex={-1} className="flex-1 focus:outline-none">
        <LandingHeroSection />
        <LandingFeatures />
      </main>
      <SiteFooter />
    </div>
  );
}

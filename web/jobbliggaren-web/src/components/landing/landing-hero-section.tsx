import { Suspense } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { Eye } from "lucide-react";
import { AuthCard } from "@/components/auth/AuthCard";
import { AuthCardSkeleton } from "@/components/auth/AuthCardSkeleton";

/**
 * LandingHeroSection — the "Liggaren" ledger hero (LP-4 / #257), rewritten from
 * the former product-peek hero. Two columns (`.jp-land-hero__inner--ledger`):
 *
 *  - left: an editorial numbered ledger `<h1>` (01/02/03 + 800-weight verbs with
 *    dashed rules) followed by one factual lede. The ledger is REAL heading text
 *    (not three icon-cards) so the page stays crawlable/SEO-meaningful. Verbs and
 *    lede resolve to ink-1 (K2 — no grey).
 *  - right (`.jp-land-hero__authcol`): the on-page tabbed `<AuthCard/>` (LP-6,
 *    the single account action) under a Suspense boundary. The inner Login/
 *    RegisterForm read `useSearchParams`, so without the boundary `next build`
 *    fails static generation (same reason `/logga-in` wraps LoginForm). The
 *    fallback is a shape-matching skeleton, not `null`, because the card is above
 *    the fold. A discreet guest link sits below the card.
 *
 * Live stats live in the <LandingHeader/> and the single "gratis" mention lives
 * in the <SiteFooter/> closing row — neither is repeated here (design rule 2).
 * No CTA buttons, no OAuth, no gradient: civic-utility, deterministic.
 *
 * Sync RSC: `useTranslations` resolves synchronously; only <AuthCard/> is a
 * client island.
 */

const LEDGER = [
  { num: "01", verbKey: "hero.step1" },
  { num: "02", verbKey: "hero.step2" },
  { num: "03", verbKey: "hero.step3" },
] as const;

export function LandingHeroSection() {
  const t = useTranslations("landing");
  return (
    <section className="jp-land-hero">
      <div className="jp-land-hero__inner jp-land-hero__inner--ledger">
        <div className="jp-land-hero__copy">
          <h1 className="jp-land-hero__ledger">
            {LEDGER.map((row) => (
              <span key={row.num} className="jp-land-hero__ledger-row">
                <span className="jp-land-hero__ledger-num">{row.num}</span>
                <span className="jp-land-hero__ledger-verb">{t(row.verbKey)}</span>
              </span>
            ))}
          </h1>
          <p className="jp-land-hero__ledger-lede">{t("hero.ledgerLede")}</p>
        </div>

        <div className="jp-land-hero__authcol">
          <Suspense fallback={<AuthCardSkeleton />}>
            <AuthCard />
          </Suspense>
          <div className="jp-land-hero__guestrow">
            <Link href="/gast/oversikt" className="jp-land-hero__guestlink">
              <Eye size={16} aria-hidden="true" />
              {t("hero.guest")}
            </Link>
          </div>
        </div>
      </div>
    </section>
  );
}

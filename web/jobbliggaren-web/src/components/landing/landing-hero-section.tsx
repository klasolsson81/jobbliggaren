import { Suspense } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { Database, Eye } from "lucide-react";
import { AuthCard } from "@/components/auth/AuthCard";
import { AuthCardSkeleton } from "@/components/auth/AuthCardSkeleton";

/**
 * LandingHeroSection — "Plattan" (förslag 3a, epic #267). The former light
 * ledger hero (01/02/03 + dashed rules) is replaced by a full-bleed deep-green
 * plate on the sanctioned hero gradient (ADR 0068 permits `--jp-hero-gradient`
 * on the landing hero specifically). Two columns:
 *
 *  - left: a gold mono kicker, a clean three-line verb `<h1>` (no numbers, no
 *    rules, no colour shift — pure white typography, still REAL heading text so
 *    the page stays crawlable/SEO-meaningful), one factual lede, and a mono
 *    source line. Everything on the plate uses LITERAL white/gold values
 *    (theme-stable, same doctrine as the footer) — never `--jp-ink-inverse`.
 *  - right (`.jp-land-hero__authcol`): the on-page tabbed `<AuthCard/>` (the
 *    single account action) under a Suspense boundary. The inner Login/
 *    RegisterForm read `useSearchParams`, so without the boundary `next build`
 *    fails static generation (same reason `/logga-in` wraps LoginForm). The
 *    fallback is a shape-matching skeleton, not `null`, because the card is above
 *    the fold. A white guest link sits below the card.
 *
 * Live stats live in the <LandingHeader/> and are never repeated here (design
 * rule 2). No CTA buttons, no OAuth: civic-utility, deterministic.
 *
 * Sync RSC: `useTranslations` resolves synchronously; only <AuthCard/> is a
 * client island.
 */

const VERB_KEYS = ["hero.step1", "hero.step2", "hero.step3"] as const;

export function LandingHeroSection() {
  const t = useTranslations("landing");
  return (
    <section className="jp-land-hero jp-land-hero--plate">
      <div className="jp-land-hero__inner jp-land-hero__inner--plate">
        <div className="jp-land-hero__copy">
          <p className="jp-land-hero__kicker">{t("hero.kicker")}</p>
          <h1 className="jp-land-hero__stack">
            {VERB_KEYS.map((key) => (
              <span key={key} className="jp-land-hero__stack-verb">
                {t(key)}
              </span>
            ))}
          </h1>
          <p className="jp-land-hero__lede--plate">{t("hero.ledgerLede")}</p>
          <p className="jp-land-hero__source">
            <Database size={13} aria-hidden="true" />
            {t("hero.source")}
          </p>
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

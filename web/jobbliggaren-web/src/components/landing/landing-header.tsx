import Link from "next/link";
import { useFormatter, useTranslations } from "next-intl";
import { BrandLogo } from "@/components/brand/brand-logo";
import { formatNumber } from "@/lib/i18n/format";
import { type LandingStats } from "./landing-stats-format";

/**
 * LandingHeader — the landing-surface public header (LP-4 / #257). Brand left,
 * live Platsbanken stats right; NO login link (the on-page AuthCard carries the
 * single account action, epic #267 design rule 4 + the `.jp-head` contract).
 *
 * Consumes the dormant shared `.jp-head*` classes #254 seeded. #258 (LP-5a)
 * gives the other public surfaces (auth, marketing-inner) a visually matching
 * minimal SiteHeader; the landing keeps its own LP-4-mounted header here.
 *
 * Stats live in ONE place (here) — the hero never repeats them (design rule 2).
 * `+N nya idag` uses the `--jp-success` delta tint via `.jp-head__stat-delta`.
 *
 * Sync RSC: `useTranslations`/`useFormatter` resolve synchronously. Stats arrive
 * as a prop from the async <LandingPage/> server-fetch (`getLandingStats`, ADR
 * 0064), keeping this component renderable in tests without mocking the API.
 */
export function LandingHeader({ stats }: { stats: LandingStats }) {
  const { activeCount, newToday } = stats;
  const t = useTranslations("landing");
  const format = useFormatter();
  return (
    <header className="jp-head">
      <div className="jp-head__inner">
        <Link href="/" className="jp-brand" aria-label={t("brand.homeAriaLabel")}>
          <BrandLogo />
        </Link>
        <div className="jp-head__stats" aria-label={t("topbar.statsAriaLabel")}>
          <div className="jp-head__stat">
            <span className="jp-head__stat-num">
              {formatNumber(format, activeCount)}
            </span>
            <span className="jp-head__stat-label">{t("topbar.activeAdsLabel")}</span>
          </div>
          <span className="jp-head__sep" aria-hidden="true" />
          <div className="jp-head__stat">
            <span className="jp-head__stat-num jp-head__stat-delta">
              {"+"}
              {formatNumber(format, newToday)}
            </span>
            <span className="jp-head__stat-label">{t("topbar.newTodayLabel")}</span>
          </div>
        </div>
      </div>
    </header>
  );
}

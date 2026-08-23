import Link from "next/link";
import { useFormatter, useTranslations } from "next-intl";
import { BrandLogo } from "@/components/brand/brand-logo";
import { SkipLink } from "@/components/site/skip-link";
import { formatNumber } from "@/lib/i18n/format";
import { type LandingStats } from "@/components/landing/landing-stats-format";

/**
 * SiteHeader — the ONE public header. Landing (`/`), the auth surfaces
 * (`/logga-in`, `/registrera`, …) and the marketing-inner pages (`/villkor`,
 * `/integritet`, `/cookies`, `/tillganglighet`) all mount this component.
 *
 * It absorbs the former `landing-header.tsx` (LP-4 / #257), which was a second
 * component on the SAME `.jp-head` contract — the split was never a styling
 * difference, only a different right slot, and #1476 measured what that cost:
 * the auth pages ended up with a header whose only interactive element was the
 * brand logo. Two axes remain, and both have consumers:
 *
 *   `stats`      present only on the landing (the inner pages never repeat them)
 *   `showLogin`  false on the auth surfaces, where a link to the page you are
 *                already on is not an action
 *
 * LP-5b (#259) did NOT fold the signed-in shells in here, and this change does
 * not either: `.jp-header` is a different CSS contract with its own dark-mode
 * pins, and merging the namespaces was weighed and rejected (CTO trade-off,
 * #259). The legacy `.jp-land-top` class lost its last consumer in #258 and its
 * CSS was removed in #1054; `site-header.test.tsx` still asserts the markup is
 * absent — that assertion is the contract, not a consumer.
 *
 * A11y: brand + action sit in a labelled `<nav>` landmark, distinct from the
 * footer's section navs. The shared `<SkipLink>` is rendered first, so every
 * public surface gets the same first-focusable jump to `#main` — which means a
 * surface mounting this header must NOT render a second one.
 *
 * Sync RSC: `useTranslations`/`useFormatter` resolve synchronously. Stats arrive
 * as a prop from the async page's server-fetch (`getLandingStats`, ADR 0064), so
 * the header stays renderable in tests without mocking the API.
 */
export function SiteHeader({
  stats,
  showLogin = true,
}: {
  stats?: LandingStats;
  showLogin?: boolean;
}) {
  const t = useTranslations("landing");
  const format = useFormatter();

  /**
   * **Omätta tal renderas ALDRIG (CTO-bind 2026-07-13, A′).** Tills dess
   * returnerade backend ett hårdkodat golv vid kall cache och headern visade det
   * som ett faktum — en siffra ingen mätt, på produktens ytterdörr, för varje
   * anonym besökare. Ett omätt tal är `null` och HELA stat-gruppen utelämnas.
   * En MÄTT nolla är fortfarande 0 och renderas. Båda talen kommer från samma
   * mätning: har vi det ena har vi det andra, så narrowingen tar dem ihop.
   */
  const measured =
    stats && stats.activeCount !== null && stats.newToday !== null
      ? { activeCount: stats.activeCount, newToday: stats.newToday }
      : null;

  return (
    <>
      <SkipLink label={t("common.skipToContent")} />
      <header className="jp-head">
        <nav
          className="jp-head__inner"
          aria-label={t("common.headerNavAriaLabel")}
        >
          <Link
            href="/"
            className="jp-brand"
            aria-label={t("brand.homeAriaLabel")}
          >
            <BrandLogo />
          </Link>
          <div className="jp-head__right">
            {measured && (
              <>
                <div
                  className="jp-head__stats"
                  role="group"
                  aria-label={t("topbar.statsAriaLabel")}
                >
                  <div className="jp-head__stat">
                    <span className="jp-head__stat-num">
                      {formatNumber(format, measured.activeCount)}
                    </span>
                    <span className="jp-head__stat-label">
                      {t("topbar.activeAdsLabel")}
                    </span>
                  </div>
                  <span className="jp-head__sep" aria-hidden="true" />
                  <div className="jp-head__stat">
                    <span className="jp-head__stat-num jp-head__stat-delta">
                      {"+"}
                      {formatNumber(format, measured.newToday)}
                    </span>
                    <span className="jp-head__stat-label">
                      {t("topbar.newTodayLabel")}
                    </span>
                  </div>
                </div>
                {showLogin && (
                  <span className="jp-head__sep" aria-hidden="true" />
                )}
              </>
            )}
            {showLogin && (
              <Link href="/logga-in" className="jp-btn jp-btn--secondary">
                {t("common.loginLink")}
              </Link>
            )}
          </div>
        </nav>
      </header>
    </>
  );
}

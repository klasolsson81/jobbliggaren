import { useTranslations } from "next-intl";

/**
 * LandingFeatures — "Funktioner" (förslag 3a, epic #267). Six features in a
 * high-contrast 3×2 grid (→ 2 cols < 1040, 1 col < 700). The former mono-key/
 * value rows are replaced by cells with a 2px ink-1 top rule, an `<h3>` title
 * and a body paragraph — everything in `--jp-ink-1` (K2: no washed-out grey;
 * hierarchy comes from size/weight, not colour). White section sits directly
 * under the dark hero plate for a clean contrast break.
 *
 * Sync RSC (server component → `useTranslations` resolves synchronously). Copy
 * via next-intl (`landing.features.*`). Civic-utility tone: no icons, no
 * numbered "how it works" circles, no trust pills.
 */

const FEATURE_KEYS = [
  "search",
  "matching",
  "applications",
  "cvReview",
  "companyWatch",
  "reminders",
] as const;

export function LandingFeatures() {
  const t = useTranslations("landing");
  return (
    <section className="jp-land-section">
      <div className="jp-container">
        <div className="jp-land-section__head">
          <div className="jp-land-kicker">{t("features.kicker")}</div>
          <h2 className="jp-land-section__title">{t("features.title")}</h2>
        </div>
        <div className="jp-land-features--grid">
          {FEATURE_KEYS.map((key) => (
            <div key={key} className="jp-land-featcell">
              <h3 className="jp-land-featcell__title">
                {t(`features.${key}.key`)}
              </h3>
              <p className="jp-land-featcell__body">
                {t(`features.${key}.body`)}
              </p>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}

import { useTranslations } from "next-intl";

/**
 * LandingFeatures — "Funktioner"-sektion i v3-stil (HANDOVER §7.1 punkt 3).
 *
 * Mono-key (left, 220px, uppercase navy-700) + brödtext (right). Ren RSC
 * (sync server component → `useTranslations` resolveras synkront).
 *
 * Feature-copy verbatim från `src-v3/landing.jsx` (prototyp-källan är kontrakt
 * enligt Klas pre-F6 Prompt 1 förkrav), nu via next-intl (`landing.features.*`).
 * Civic-utility-ton: ingen ikon, ingen "Så funkar det"-numrerad cirkel, inga
 * trust-pills.
 */

const FEATURE_KEYS = ["search", "pipeline", "cv", "reminders"] as const;

export function LandingFeatures() {
  const t = useTranslations("landing");
  return (
    <section className="jp-land-section jp-land-section--alt">
      <div className="jp-container">
        <div className="jp-land-section__head">
          <div className="jp-land-kicker">{t("features.kicker")}</div>
          <h2 className="jp-land-section__title">{t("features.title")}</h2>
        </div>
        <div className="jp-land-features">
          {FEATURE_KEYS.map((key) => (
            <div key={key} className="jp-land-feature">
              <div className="jp-land-feature__key">
                {t(`features.${key}.key`)}
              </div>
              <div className="jp-land-feature__val">
                {t(`features.${key}.body`)}
              </div>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}

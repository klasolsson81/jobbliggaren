import Link from "next/link";
import { useTranslations } from "next-intl";
import { ArrowRight } from "lucide-react";

/**
 * "Kräver åtgärd"-kortet (#726) — visas när `!hasStatedDesiredOccupation`.
 * Server Component (ingen state): ersätter den gamla setup-info-notisraden.
 * Ligger aldrig i en notislista och är inte avfärdbart (ingen X), precis som
 * dagens nudge. Ömsesidigt uteslutande mot matchnings-notisen: yrke angett →
 * kortet försvinner och matchnings-notisen renderas i Jobbannonser i stället.
 *
 * CTA:n länkar till `/oversikt?matchsetup=1` (öppnar match-setup-rail-modalen via
 * MatchSetupLauncher, epik #526 — oförändrat mål).
 */
export function SetupCallout() {
  const t = useTranslations("oversikt");
  return (
    <div className="jp-callout-block">
      <div className="jp-notice-group">
        <span className="jp-notice-group__title">
          {t("notices.groupAction")}
        </span>
        <span className="jp-notice-group__count">1</span>
      </div>
      <div className="jp-callout">
        <span className="jp-callout__strip" aria-hidden="true" />
        <div className="jp-callout__body">
          <div className="jp-callout__label">{t("notices.calloutLabel")}</div>
          <p className="jp-callout__text">
            {t.rich("notices.calloutText", { b: (chunks) => <b>{chunks}</b> })}
          </p>
        </div>
        <div className="jp-callout__aside">
          <Link
            href="/oversikt?matchsetup=1"
            className="jp-btn jp-btn--primary"
          >
            {t("notices.calloutCta")} <ArrowRight size={14} aria-hidden="true" />
          </Link>
          <span className="jp-callout__hint">{t("notices.calloutHint")}</span>
        </div>
      </div>
    </div>
  );
}

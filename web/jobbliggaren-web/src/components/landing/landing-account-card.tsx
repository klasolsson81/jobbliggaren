import Link from "next/link";
import { useTranslations } from "next-intl";
import { Check } from "lucide-react";

/**
 * LandingAccountCard — the hero's right column (direction 2b "Kontokortet",
 * #1480). It replaces the embedded `<AuthCard/>`, which asked an anonymous
 * visitor to fill in a registration form before the page had said what an
 * account is for. The card sells the account and links to `/registrera`.
 *
 * Server component. Removing the form removed the Suspense boundary the inner
 * forms needed (they read `useSearchParams`, which suspends during static
 * generation).
 *
 * **Every row restates a capability the Funktioner section already claims two
 * bands below, on this same page** — the reminder before a closing date, the
 * graded matching (Grundmatch to Toppmatch), the draft-to-reply application
 * trail, the followed companies surfacing on the overview, the CV reviewed
 * against the versioned Swedish rubric. That is the
 * rule the copy is written to: if a row here were false, `landing.features.*`
 * would already be false. It is deliberately NOT the handoff's own first row
 * ("sparade sökningar som bevakar nya annonser") — a saved search is a stored
 * filter in "Senaste sökningar", and nothing in the product makes one watch.
 *
 * One solid primary button on the page (ADR 0038 / DESIGN.md §6): this CTA.
 * "Logga in" in the header is secondary and the guest link is a link.
 */

const BENEFIT_KEYS = [
  "savedAds",
  "matching",
  "applications",
  "companyWatch",
  "cvReview",
] as const;

export function LandingAccountCard() {
  const t = useTranslations("landing");

  return (
    <div className="jp-land-account">
      <h2 className="jp-land-account__title">{t("account.title")}</h2>
      <ul className="jp-land-account__list">
        {BENEFIT_KEYS.map((key) => (
          <li key={key} className="jp-land-account__item">
            <Check
              size={18}
              strokeWidth={2.5}
              aria-hidden="true"
              className="jp-land-account__check"
            />
            <span>{t(`account.${key}`)}</span>
          </li>
        ))}
      </ul>
      <Link
        href="/registrera"
        className="jp-btn jp-btn--primary jp-land-account__cta"
      >
        {t("account.cta")}
      </Link>
      <p className="jp-auth-free">{t("auth.free")}</p>
    </div>
  );
}

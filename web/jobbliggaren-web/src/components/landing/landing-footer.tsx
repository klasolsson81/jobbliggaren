import Link from "next/link";
import { useTranslations } from "next-intl";
import { LandingLangToggle } from "./lang-toggle";

/**
 * LandingFooter — länkrad + lang-toggle.
 *
 * HANDOVER §7.1 punkt 4 + §0 punkt 7: lang-toggeln är medvetet placerad här
 * (och i `/installningar`) — INTE i header. Footer är RSC-komposit med en
 * client-island (LandingLangToggle). MVP (Klas 2026-06-24): theme-toggeln
 * borttagen — ett färgläge (light); dark behålls dormant i koden.
 *
 * Länkar är placeholders: målroutes för Användarvillkor/Integritet/Cookies/
 * Tillgänglighet/Kontakt/Om är inte byggda än. Pekar på `/` med
 * `aria-disabled` så de syns men inte blir trasiga länkar (Klas pre-F6 Prompt 1
 * verbatim: "Länkarna pekar mot befintliga statiska routes om sådana finns;
 * annars no-op med TODO").
 */

// Översättningsnyckel (`landing.footer.*`) + href. TODO: Fas 7 — peka mot
// riktiga om-/villkor-/integritet-routes.
const FOOTER_LINKS = [
  { labelKey: "footer.linkAbout", href: "/" },
  { labelKey: "footer.linkTerms", href: "/" },
  { labelKey: "footer.linkPrivacy", href: "/" },
  { labelKey: "footer.linkCookies", href: "/" },
  { labelKey: "footer.linkAccessibility", href: "/" },
  { labelKey: "footer.linkContact", href: "/" },
] as const satisfies ReadonlyArray<{ labelKey: string; href: string }>;

export function LandingFooter() {
  const t = useTranslations("landing");
  return (
    <footer className="jp-land-foot">
      <div className="jp-land-foot__inner">
        <nav className="jp-land-foot__links" aria-label={t("common.footerNavLabel")}>
          {FOOTER_LINKS.map((l, i) => (
            <span key={l.labelKey} className="inline-flex items-center">
              <Link href={l.href}>{t(l.labelKey)}</Link>
              {i < FOOTER_LINKS.length - 1 && (
                <span className="jp-land-foot__dot" aria-hidden="true">
                  ·
                </span>
              )}
            </span>
          ))}
        </nav>
        {/* MVP: ThemeToggle borttaget — ett färgläge (light). Dark dormant i koden. */}
        <div className="jp-land-foot__settings">
          <LandingLangToggle />
        </div>
      </div>
    </footer>
  );
}

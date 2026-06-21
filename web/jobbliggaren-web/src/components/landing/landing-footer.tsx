import Link from "next/link";
import { useTranslations } from "next-intl";
import { ThemeToggle } from "@/components/theme-toggle";
import { LandingLangToggle } from "./lang-toggle";

/**
 * LandingFooter — länkrad + theme + lang toggles.
 *
 * HANDOVER §7.1 punkt 4 + §0 punkt 7: theme- och lang-togglarna är medvetet
 * placerade här (och i `/installningar`) — INTE i header. Footer är RSC-
 * komposit med två client-islands (ThemeToggle, LandingLangToggle).
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
        <div className="jp-land-foot__settings">
          <ThemeToggle />
          <LandingLangToggle />
        </div>
      </div>
    </footer>
  );
}

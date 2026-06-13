import Link from "next/link";
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

const FOOTER_LINKS: ReadonlyArray<{ label: string; href: string }> = [
  // TODO: Fas 7 — peka mot riktiga om-/villkor-/integritet-routes
  { label: "Om Jobbliggaren", href: "/" },
  { label: "Användarvillkor", href: "/" },
  { label: "Integritetspolicy", href: "/" },
  { label: "Cookies", href: "/" },
  { label: "Tillgänglighet", href: "/" },
  { label: "Kontakt", href: "/" },
];

export function LandingFooter() {
  return (
    <footer className="jp-land-foot">
      <div className="jp-land-foot__inner">
        <nav className="jp-land-foot__links" aria-label="Sidfot">
          {FOOTER_LINKS.map((l, i) => (
            <span key={l.label} className="inline-flex items-center">
              <Link href={l.href}>{l.label}</Link>
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

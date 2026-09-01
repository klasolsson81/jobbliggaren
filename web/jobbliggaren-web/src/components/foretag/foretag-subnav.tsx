import Link from "next/link";
import { useTranslations } from "next-intl";

/**
 * /foretag sub-nav (S1 #996). A persistent horizontal wayfinding strip rendered
 * under the pagehero on every /foretag surface. RSC, searchParam-free — plain
 * `<Link>`s styled as `.jp-subnav` give a server re-render with no client JS.
 * `aria-current="page"` + `data-active` mark the current surface; hrefs are full
 * paths so every item is a real, shareable navigation target.
 *
 * The active surface is a PROP the page passes (each page knows its own identity),
 * not derived from `usePathname` — so this stays a zero-client-JS server component.
 * Adding or removing a surface is a single OPTIONS entry.
 *
 * Taxonomy (ADR 0117): Smarta bevakningar is a browsing surface, a sibling of Sök
 * företag — never nested with Bevakade företag under a shared "Bevakningar" parent.
 * The `smartaBevakningar` key maps to the `/foretag/smarta-bevakningar` slug so the
 * URL carries the full disambiguating noun (never a bare `bevakningar`). Order +
 * default landing = Bevakade först (Klas 2026-07-21).
 *
 * `nyaAnnonser` (#1576, Klas 2026-08-31) is the fifth entry: `/foretag/bevakade/nya`
 * is a real surface a user stands on, and before this it borrowed `bevakade`'s
 * `aria-current="page"` and told a screen reader it was on a page it was not. Its
 * label stays inside the bevakade-företag noun and never says a bare "bevakningar",
 * so ADR 0117's rule is untouched.
 */

export type ForetagSurface =
  | "bevakade"
  | "sok"
  | "smartaBevakningar"
  | "historik"
  | "nyaAnnonser";

const OPTIONS: ReadonlyArray<{ surface: ForetagSurface; href: string }> = [
  { surface: "bevakade", href: "/foretag/bevakade" },
  { surface: "sok", href: "/foretag/sok" },
  { surface: "smartaBevakningar", href: "/foretag/smarta-bevakningar" },
  { surface: "historik", href: "/foretag/historik" },
  { surface: "nyaAnnonser", href: "/foretag/bevakade/nya" },
];

export function ForetagSubnav({ active }: { active: ForetagSurface }) {
  const t = useTranslations("pages.foretag.subnav");
  return (
    <nav className="jp-subnav" aria-label={t("label")}>
      {OPTIONS.map((option) => {
        const isActive = option.surface === active;
        return (
          <Link
            key={option.surface}
            href={option.href}
            className="jp-subnav__item"
            data-active={isActive}
            aria-current={isActive ? "page" : undefined}
          >
            {t(option.surface)}
          </Link>
        );
      })}
    </nav>
  );
}

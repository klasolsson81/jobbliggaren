import Link from "next/link";
import { Bell, LogOut, Shield, Target, UserRound, type LucideIcon } from "lucide-react";
import { useTranslations } from "next-intl";
import { logoutAction } from "@/lib/auth/actions";
import { MINA_SIDOR_HREF } from "@/lib/nav/mina-sidor-hrefs";

export type MinaSidorSection = keyof typeof MINA_SIDOR_HREF;

const SECTIONS: ReadonlyArray<MinaSidorSection> = ["matchning", "notiser", "konto", "sekretess"];

/** One fixed glyph per section (ADR 0145, Amendment 2026-09-28). */
const SECTION_ICON: Record<MinaSidorSection, LucideIcon> = {
  matchning: Target,
  notiser: Bell,
  konto: UserRound,
  sekretess: Shield,
};

/**
 * The /mina-sidor section menu (#1891, #1916). The current section is a prop the page passes, as
 * `ForetagSubnav` does, so this stays a server component with no client JS; every section is a
 * real URL. Logga ut sits under the menu, outside the navigation landmark, because it is an action:
 * the same `logoutAction` form as the header's user menu.
 */
export function MinaSidorNav({ active }: { active: MinaSidorSection }) {
  const t = useTranslations("pages.minaSidor");
  const ts = useTranslations("settings");
  return (
    <div className="jp-settingsnav">
      <nav aria-label={t("nav.label")}>
        <ul className="jp-settingsnav__list">
          {SECTIONS.map((section) => {
            const Icon = SECTION_ICON[section];
            return (
              <li key={section}>
                <Link
                  href={MINA_SIDOR_HREF[section]}
                  className="jp-settingsnav__item"
                  aria-current={section === active ? "page" : undefined}
                >
                  <Icon size={16} aria-hidden="true" className="jp-settingsnav__icon" />
                  {t(`sections.${section}`)}
                </Link>
              </li>
            );
          })}
        </ul>
      </nav>
      <form action={logoutAction} className="jp-settingsnav__foot">
        <button type="submit" className="jp-settingsnav__logout">
          <LogOut size={16} aria-hidden="true" />
          {ts("logout.action")}
        </button>
      </form>
    </div>
  );
}

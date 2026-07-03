"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useTranslations } from "next-intl";

// Client-island so the active admin nav link gets `aria-current="page"`
// (WCAG 2.4.8 Location — parity with app-shell.tsx / guest-shell.tsx). The
// admin surface is a topbar; styling lives in the scoped .jp-adminnav__link
// class (globals.css) mirroring .jp-nav__link: ink text in BOTH states, and
// the active state is carried by an accent ::after-bar + weight + aria-current
// (three independent cues, CTO D4/#549 — the bar, never a fill; supersedes
// the #247 fill after design-review Major 1).

// i18n keys under `admin.nav.*` (literal union keeps next-intl typed-message
// checking when the label resolves dynamically in the map below).
type AdminNavLabelKey = "nav.granskning" | "nav.jobb";

interface AdminNavItem {
  readonly href: string;
  readonly labelKey: AdminNavLabelKey;
}

const ADMIN_NAV: ReadonlyArray<AdminNavItem> = [
  { href: "/admin/granskning", labelKey: "nav.granskning" },
  { href: "/admin/jobb", labelKey: "nav.jobb" },
];

function isActive(pathname: string, href: string): boolean {
  return pathname === href || pathname.startsWith(href + "/");
}


export function AdminNav() {
  const pathname = usePathname();
  const t = useTranslations("admin");

  return (
    <nav aria-label={t("nav.label")} className="flex items-center gap-1">
      {ADMIN_NAV.map((item) => {
        const active = isActive(pathname, item.href);
        return (
          <Link
            key={item.href}
            href={item.href}
            aria-current={active ? "page" : undefined}
            className="jp-adminnav__link"
          >
            {t(item.labelKey)}
          </Link>
        );
      })}
    </nav>
  );
}

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
// the #247 fill after design-review Major 1). The row's responsive layout is
// `.jp-adminnav` in (admin)/admin.css (ADR 0150 D7).

// i18n keys under `admin.nav.*` (literal union keeps next-intl typed-message
// checking when the label resolves dynamically in the map below).
type AdminNavLabelKey =
  | "nav.oversikt"
  | "nav.anvandare"
  | "nav.feedback"
  | "nav.loggar"
  | "nav.epost"
  | "nav.jobb"
  | "nav.granskning";

interface AdminNavItem {
  readonly href: string;
  readonly labelKey: AdminNavLabelKey;
  /** The overview owns the bare `/admin`, which prefixes every other item, so it matches exactly. */
  readonly exact?: true;
}

const ADMIN_NAV: ReadonlyArray<AdminNavItem> = [
  { href: "/admin", labelKey: "nav.oversikt", exact: true },
  { href: "/admin/anvandare", labelKey: "nav.anvandare" },
  { href: "/admin/feedback", labelKey: "nav.feedback" },
  { href: "/admin/loggar", labelKey: "nav.loggar" },
  { href: "/admin/e-post", labelKey: "nav.epost" },
  { href: "/admin/jobb", labelKey: "nav.jobb" },
  { href: "/admin/granskning", labelKey: "nav.granskning" },
];

function isActive(pathname: string, item: AdminNavItem): boolean {
  if (item.exact) return pathname === item.href;
  return pathname === item.href || pathname.startsWith(item.href + "/");
}

export function AdminNav() {
  const pathname = usePathname();
  const t = useTranslations("admin");

  return (
    <nav aria-label={t("nav.label")} className="jp-adminnav">
      {ADMIN_NAV.map((item) => {
        const active = isActive(pathname, item);
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

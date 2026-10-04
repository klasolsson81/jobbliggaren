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
  /** The item's path under the surface's base path. */
  readonly path: string;
  readonly labelKey: AdminNavLabelKey;
  /** The overview owns the bare `/admin`, which prefixes every other item, so it matches exactly. */
  readonly exact?: true;
}

const ADMIN_NAV: ReadonlyArray<AdminNavItem> = [
  { path: "", labelKey: "nav.oversikt", exact: true },
  { path: "/anvandare", labelKey: "nav.anvandare" },
  { path: "/feedback", labelKey: "nav.feedback" },
  { path: "/loggar", labelKey: "nav.loggar" },
  { path: "/e-post", labelKey: "nav.epost" },
  { path: "/jobb", labelKey: "nav.jobb" },
  { path: "/granskning", labelKey: "nav.granskning" },
];

function isActive(pathname: string, href: string, exact: boolean): boolean {
  if (exact) return pathname === href;
  return pathname === href || pathname.startsWith(href + "/");
}

/** `basePath` is "/admin", or the local preview's own root (ADR 0150 D5). */
export function AdminNav({ basePath = "/admin" }: { readonly basePath?: string }) {
  const pathname = usePathname();
  const t = useTranslations("admin");

  return (
    <nav aria-label={t("nav.label")} className="jp-adminnav">
      {ADMIN_NAV.map((item) => {
        const href = basePath + item.path;
        const active = isActive(pathname, href, item.exact === true);
        return (
          <Link
            key={item.path}
            href={href}
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

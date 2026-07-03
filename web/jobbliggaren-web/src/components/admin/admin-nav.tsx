"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useTranslations } from "next-intl";

// Client-island so the active admin nav link gets `aria-current="page"`
// (WCAG 2.4.8 Location — parity with app-shell.tsx / guest-shell.tsx). The
// admin surface is a topbar; the active state is an accent UNDERLINE bar +
// weight + aria-current (three independent cues, CTO D4/#549 — the bar carries
// active-ness, never a fill; supersedes the #247 fill after design-review
// Major 1, 2026-07-03). The base keeps a transparent border so states don't
// shift layout.

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

const NAV_LINK_BASE = "rounded-md border-b-2 border-transparent px-3 py-1.5 text-body-sm";

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
            className={`${NAV_LINK_BASE} ${
              active
                ? "border-brand-700 font-semibold text-text-primary"
                : "text-text-primary hover:bg-surface-tertiary"
            }`}
          >
            {t(item.labelKey)}
          </Link>
        );
      })}
    </nav>
  );
}

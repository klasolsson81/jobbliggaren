"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { onPlainNav } from "@/lib/nav/modified-click";
import { useTranslations } from "next-intl";

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
export function AdminNav({ basePath = "/admin", variant = "standalone", onNavigate }: {
  readonly basePath?: string;
  readonly variant?: "standalone" | "header" | "drawer";
  readonly onNavigate?: () => void;
}) {
  const pathname = usePathname();
  const t = useTranslations("common.adminNav");

  return (
    <nav aria-label={t("nav.label")} className={variant === "header" ? "jp-nav" : variant === "drawer" ? "jp-drawer__list" : "jp-adminnav"}>
      {ADMIN_NAV.map((item) => {
        const href = basePath + item.path;
        const active = isActive(pathname, href, item.exact === true);
        return (
          <Link
            key={item.path}
            href={href}
            aria-current={active ? "page" : undefined}
            className={variant === "header" ? "jp-nav__link" : variant === "drawer" ? "jp-drawer__item" : "jp-adminnav__link"}
            onClick={onNavigate ? (event) => onPlainNav(event, onNavigate) : undefined}
          >
            {t(item.labelKey)}
          </Link>
        );
      })}
    </nav>
  );
}

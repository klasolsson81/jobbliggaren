"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useTranslations } from "next-intl";
import type { ReactNode } from "react";
import { Briefcase, Inbox, LayoutDashboard, LogIn, ScrollText } from "lucide-react";
import { HeaderStrip } from "@/components/site/header-strip";

// F-Pre Punkt 5 — Gäst-shell (CTO-dom 2026-05-24 Beslut 1, Variant A).
// Egen shell separat från `(app)`-shellen — gemensam Look (header + brand)
// men anonym-spår med ingen `getServerSession()`-grind. Lämna-demo-CTA leder
// till landing istället för logout (gäst har ingen session att rensa).
//
// Client-island så vi kan sätta `aria-current="page"` på aktiv nav-länk
// (design-reviewer B1 2026-05-24 — WCAG 2.4.8 Location, paritet med
// app-shell.tsx:305). `.jp-nav__link[aria-current="page"]` ger active-stripe.

// i18n-nyckel under `guest.shell.*` (literal-union så next-intl typed-messages
// behåller nyckel-kontrollen när texten resolveras dynamiskt i komponenten).
type GuestNavLabelKey =
  | "shell.navOversikt"
  | "shell.navJobb"
  | "shell.navAnsokningar"
  | "shell.navCv";

interface GuestNavItem {
  readonly href: string;
  readonly labelKey: GuestNavLabelKey;
  readonly icon: typeof LayoutDashboard;
}

// F-Pre Punkt 5b — /gast/jobb tillbaka i nav (CTO 2026-05-24 5b Beslut 3);
// föregående hide motiverad av LIVE-deferral (Punkt 5 Alt 2), mockdata-väg
// har inte den risk-profilen (inga BE-anrop, ingen anonym auth-yta).
const GUEST_NAV: ReadonlyArray<GuestNavItem> = [
  { href: "/gast/oversikt", labelKey: "shell.navOversikt", icon: LayoutDashboard },
  { href: "/gast/jobb", labelKey: "shell.navJobb", icon: Briefcase },
  { href: "/gast/ansokningar", labelKey: "shell.navAnsokningar", icon: Inbox },
  { href: "/gast/cv", labelKey: "shell.navCv", icon: ScrollText },
];

function isActive(pathname: string, href: string): boolean {
  return pathname === href || pathname.startsWith(href + "/");
}

export function GuestShell({ children }: { children: ReactNode }) {
  const pathname = usePathname();
  const t = useTranslations("guest");

  return (
    <div className="jp-shell">
      <HeaderStrip brandHref="/" brandLabel={t("shell.brandAriaLabel")}>
        <nav className="jp-nav" aria-label={t("shell.navAriaLabel")}>
          {GUEST_NAV.map((item) => (
            <Link
              key={item.href}
              href={item.href}
              className="jp-nav__link"
              aria-current={isActive(pathname, item.href) ? "page" : undefined}
            >
              {t(item.labelKey)}
            </Link>
          ))}
        </nav>

        <span className="jp-header__spacer" />

        <div className="jp-header__actions">
          <Link
            href="/logga-in"
            className="jp-btn jp-btn--secondary jp-btn--sm"
          >
            <LogIn size={16} aria-hidden="true" /> {t("shell.logIn")}
          </Link>
          <Link
            href="/registrera"
            className="jp-btn jp-btn--primary jp-btn--sm"
          >
            {t("shell.register")}
          </Link>
        </div>
      </HeaderStrip>

      <main id="main" tabIndex={-1} className="jp-content focus:outline-none">
        {children}
      </main>
    </div>
  );
}

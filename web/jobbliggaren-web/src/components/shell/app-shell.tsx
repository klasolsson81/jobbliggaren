"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useCallback, useEffect, useId, useRef, useState } from "react";
import { useTranslations } from "next-intl";
import {
  Bell,
  Bookmark,
  Briefcase,
  Clock,
  Inbox,
  LayoutDashboard,
  LogOut,
  Menu,
  ScrollText,
  Settings,
  ShieldCheck,
  X,
} from "lucide-react";
import { logoutAction } from "@/lib/auth/actions";
import { useDismissable } from "@/lib/hooks/use-dismissable";
import { HeaderStats } from "@/components/shell/header-stats";
import { BrandLogo } from "@/components/brand/brand-logo";
import type { LandingStatsDto } from "@/lib/dto/landing";

/**
 * v3 header-shell (ADR 0054 — header-meny ersätter sektionerad sidebar).
 *
 * Layout: `jp-shell` (flex column) = sticky `jp-header` + `jp-content` main.
 * Ingen sidebar, ingen desktop-burger. På <900px döljs nav-länkarna via CSS
 * och burgern öppnar `jp-drawer` från höger med samma länkar.
 *
 * Tema-logik finns INTE här — `.jp-header` är vit i båda teman via
 * CSS-scopad override (`[data-theme="dark"] .jp-header`, ADR 0052 Beslut 6).
 * Theme/lang-toggles flyttade till Inställningar + landing-footer (HANDOVER §0.7).
 */

type NavLabelKey = "oversikt" | "jobb" | "ansokningar" | "cv";

type NavItem = {
  href: string;
  labelKey: NavLabelKey;
  icon: typeof Briefcase;
};

const PRIMARY_NAV: NavItem[] = [
  // F6 P5 Punkt 4 — additivt tillägg. Default-route-byte (login-redirect +
  // brand-länk) DEFERRAS till separat Klas-GO-commit per CTO-dom 2026-05-24 D6.
  { href: "/oversikt", labelKey: "oversikt", icon: LayoutDashboard },
  { href: "/jobb", labelKey: "jobb", icon: Briefcase },
  { href: "/ansokningar", labelKey: "ansokningar", icon: Inbox },
  { href: "/cv", labelKey: "cv", icon: ScrollText },
];

function isActive(pathname: string, href: string): boolean {
  return pathname === href || pathname.startsWith(href + "/");
}

/**
 * v3-native routes (CTO D1, Variant B 2026-05-19): sidor som äger sin egen
 * bredd-layout (edge-to-edge hero + egen .jp-container, ELLER — som
 * /ansokningar efter F5 — egen .jp-container/.jp-page utan hero). Prefix-
 * match, samma idiom som aktiv-route-logiken ovan. För dessa routes
 * renderas children DIREKT i .jp-content utan .jp-shell-transitional-
 * container — annars constrainas heron (edge-to-edge bryts) eller dubbleras
 * .jp-container (dubbel max-width/padding på sidor som äger sin egen).
 * BORTTAGNINGS-TRIGGER: när alla (app)-sidor refaktorerats till egna
 * .jp-container/.jp-page tas denna lista + .jp-shell-transitional-
 * container bort tillsammans (se globals.css-trail, ADR 0052).
 */
const V3_NATIVE_ROUTES = ["/jobb", "/ansokningar", "/oversikt", "/cv"];

function isV3Native(pathname: string): boolean {
  return V3_NATIVE_ROUTES.some(
    (r) => pathname === r || pathname.startsWith(r + "/")
  );
}

function initials(email: string): string {
  const local = email.split("@")[0] ?? email;
  const parts = local.split(/[._-]+/).filter(Boolean);
  const chars =
    parts.length >= 2 && parts[0] && parts[1]
      ? parts[0].charAt(0) + parts[1].charAt(0)
      : local.slice(0, 2);
  return chars.toUpperCase();
}

function NotificationsBell() {
  const t = useTranslations("common");
  const [open, setOpen] = useState(false);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const ref = useDismissable(open, () => setOpen(false), triggerRef);
  const titleId = useId();

  return (
    <div className="relative">
      <button
        ref={triggerRef}
        type="button"
        className="jp-icon-btn"
        aria-label={t("notifications.buttonAriaLabel")}
        aria-expanded={open}
        aria-haspopup="dialog"
        onClick={() => setOpen((v) => !v)}
      >
        <Bell size={18} aria-hidden="true" />
      </button>
      {open && (
        <div
          ref={ref}
          role="dialog"
          aria-labelledby={titleId}
          className="jp-notif"
        >
          <div id={titleId} className="jp-notif__head">
            {t("notifications.heading")}
          </div>
          <div className="jp-notif__list">
            <p className="jp-notif__item">{t("notifications.empty")}</p>
          </div>
        </div>
      )}
    </div>
  );
}

function UserMenu({ email, isAdmin }: { email: string; isAdmin: boolean }) {
  const t = useTranslations("common");
  const [open, setOpen] = useState(false);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const ref = useDismissable(open, () => setOpen(false), triggerRef);
  const local = email.split("@")[0] ?? email;

  return (
    <div className="relative">
      <button
        ref={triggerRef}
        type="button"
        className="jp-avatar"
        aria-label={t("userMenu.ariaLabel")}
        aria-expanded={open}
        aria-haspopup="true"
        onClick={() => setOpen((v) => !v)}
      >
        {initials(email)}
      </button>
      {open && (
        <div
          ref={ref}
          role="group"
          aria-label={t("userMenu.ariaLabel")}
          className="jp-usermenu"
        >
          <div className="jp-usermenu__head">
            <div className="jp-usermenu__name">{local}</div>
            <div className="jp-usermenu__email">{email}</div>
          </div>
          <Link
            href="/installningar"
            className="jp-usermenu__item"
            onClick={() => setOpen(false)}
          >
            <Settings size={16} aria-hidden="true" /> {t("userMenu.installningar")}
          </Link>
          <Link
            href="/sokningar"
            className="jp-usermenu__item"
            onClick={() => setOpen(false)}
          >
            <Clock size={16} aria-hidden="true" /> {t("userMenu.senasteSokningar")}
          </Link>
          <Link
            href="/sparade"
            className="jp-usermenu__item"
            onClick={() => setOpen(false)}
          >
            <Bookmark size={16} aria-hidden="true" /> {t("userMenu.sparadeAnnonser")}
          </Link>
          <Link
            href="/cv"
            className="jp-usermenu__item"
            onClick={() => setOpen(false)}
          >
            <ScrollText size={16} aria-hidden="true" /> {t("userMenu.minaCv")}
          </Link>
          {isAdmin && (
            <>
              <div className="jp-usermenu__sep" role="separator" />
              <Link
                href="/admin/granskning"
                className="jp-usermenu__item"
                onClick={() => setOpen(false)}
              >
                <ShieldCheck size={16} aria-hidden="true" /> {t("userMenu.granskning")}
              </Link>
            </>
          )}
          <div className="jp-usermenu__sep" role="separator" />
          <form action={logoutAction}>
            <button
              type="submit"
              className="jp-usermenu__item"
            >
              <LogOut size={16} aria-hidden="true" /> {t("userMenu.loggaUt")}
            </button>
          </form>
        </div>
      )}
    </div>
  );
}

function Drawer({
  open,
  onClose,
  pathname,
  isAdmin,
  triggerRef,
}: {
  open: boolean;
  onClose: () => void;
  pathname: string;
  isAdmin: boolean;
  triggerRef: React.RefObject<HTMLButtonElement | null>;
}) {
  const t = useTranslations("common");
  const panelRef = useRef<HTMLElement>(null);
  const closeRef = useRef<HTMLButtonElement>(null);
  const labelId = useId();

  // Fokus in i drawern vid öppning, fokus-retur till triggern vid stängning.
  useEffect(() => {
    if (open) {
      closeRef.current?.focus();
    }
  }, [open]);

  // Escape stänger; fokus-trap håller Tab inom panelen (WCAG 2.1.2 / 2.4.3).
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        onClose();
        triggerRef.current?.focus();
        return;
      }
      if (e.key !== "Tab" || !panelRef.current) return;
      const focusable = panelRef.current.querySelectorAll<HTMLElement>(
        'a[href], button:not([disabled]), [tabindex]:not([tabindex="-1"])',
      );
      if (focusable.length === 0) return;
      const first = focusable[0]!;
      const last = focusable[focusable.length - 1]!;
      if (e.shiftKey && document.activeElement === first) {
        e.preventDefault();
        last.focus();
      } else if (!e.shiftKey && document.activeElement === last) {
        e.preventDefault();
        first.focus();
      }
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [open, onClose, triggerRef]);

  if (!open) return null;

  const handleNav = () => {
    onClose();
    triggerRef.current?.focus();
  };

  return (
    <>
      <div
        className="jp-drawer-scrim"
        onClick={() => {
          onClose();
          triggerRef.current?.focus();
        }}
        aria-hidden="true"
      />
      <aside
        ref={panelRef}
        className="jp-drawer"
        role="dialog"
        aria-modal="true"
        aria-labelledby={labelId}
      >
        <div className="jp-drawer__head">
          <span id={labelId} style={{ fontSize: 17, fontWeight: 700 }}>
            {t("drawer.title")}
          </span>
          <button
            ref={closeRef}
            type="button"
            className="jp-drawer__close"
            onClick={() => {
              onClose();
              triggerRef.current?.focus();
            }}
            aria-label={t("drawer.closeAriaLabel")}
          >
            {t("drawer.close")} <X size={18} aria-hidden="true" />
          </button>
        </div>
        <nav className="jp-drawer__list" aria-label={t("drawer.navAriaLabel")}>
          {PRIMARY_NAV.map((item) => {
            const Icon = item.icon;
            return (
              <Link
                key={item.href}
                href={item.href}
                className="jp-drawer__item"
                aria-current={isActive(pathname, item.href) ? "page" : undefined}
                onClick={handleNav}
              >
                <Icon size={18} aria-hidden="true" /> {t(`nav.${item.labelKey}`)}
              </Link>
            );
          })}
          <Link
            href="/installningar"
            className="jp-drawer__item"
            aria-current={isActive(pathname, "/installningar") ? "page" : undefined}
            onClick={handleNav}
          >
            <Settings size={18} aria-hidden="true" /> {t("drawer.installningar")}
          </Link>
          {isAdmin && (
            <Link
              href="/admin/granskning"
              className="jp-drawer__item"
              aria-current={
                isActive(pathname, "/admin/granskning") ? "page" : undefined
              }
              onClick={handleNav}
            >
              <ShieldCheck size={18} aria-hidden="true" /> {t("drawer.granskning")}
            </Link>
          )}
        </nav>
      </aside>
    </>
  );
}

export function AppShell({
  email,
  isAdmin,
  initialStats,
  children,
}: {
  email: string;
  isAdmin: boolean;
  initialStats: LandingStatsDto;
  children: React.ReactNode;
}) {
  const t = useTranslations("common");
  const pathname = usePathname();
  const [drawerOpen, setDrawerOpen] = useState(false);
  const drawerTriggerRef = useRef<HTMLButtonElement>(null);

  const closeDrawer = useCallback(() => setDrawerOpen(false), []);
  const v3Native = isV3Native(pathname);

  return (
    <div className="jp-shell">
      <header className="jp-header" role="banner">
        <div className="jp-header__inner">
          <Link href="/oversikt" className="jp-brand" aria-label={t("nav.brandHome")}>
            <BrandLogo />
          </Link>

          <nav className="jp-nav" aria-label={t("nav.ariaLabel")}>
            {PRIMARY_NAV.map((item) => (
              <Link
                key={item.href}
                href={item.href}
                className="jp-nav__link"
                aria-current={
                  isActive(pathname, item.href) ? "page" : undefined
                }
              >
                {t(`nav.${item.labelKey}`)}
              </Link>
            ))}
          </nav>

          <span className="jp-header__spacer" />

          {/* Klas post-leverans-feedback 2026-05-24: HeaderStats återställd
              på alla auth-routes inkl. /oversikt (revertera design-reviewer
              M1 från svans-PR1). Mock-konflikten 28 vs 9 är löst i samma
              svans-PR via att /oversikt "Aktiva annonser totalt" nu också
              använder getLandingStats() — samma siffra som HeaderStats. */}
          <HeaderStats initialStats={initialStats} />

          <div className="jp-header__actions">
            <NotificationsBell />
            <UserMenu email={email} isAdmin={isAdmin} />
            <button
              ref={drawerTriggerRef}
              type="button"
              className="jp-icon-btn jp-drawer-trigger"
              aria-label={t("header.openMenu")}
              aria-expanded={drawerOpen}
              aria-haspopup="dialog"
              onClick={() => setDrawerOpen(true)}
            >
              <Menu size={20} aria-hidden="true" />
            </button>
          </div>
        </div>
      </header>

      <Drawer
        open={drawerOpen}
        onClose={closeDrawer}
        pathname={pathname}
        isAdmin={isAdmin}
        triggerRef={drawerTriggerRef}
      />

      <main id="main" tabIndex={-1} className="jp-content focus:outline-none">
        {/* Transitionell bredd-container (CTO B1-reparation 2026-05-19):
            v3-shellen constrainar ej bredd; un-refaktorerade (app)-sidor
            wrappas här tills F3/F5/F6 ger dem egna .jp-container/.jp-page.
            v3-native routes (CTO D1, Variant B) opt-out:ar ur wrappern och
            äger sin egen .jp-container — heron går då edge-to-edge i
            .jp-content. Borttagnings-trigger dokumenterad i globals.css +
            ADR 0052-trail (container + V3_NATIVE_ROUTES tas bort ihop). */}
        {v3Native ? (
          children
        ) : (
          <div className="jp-shell-transitional-container">{children}</div>
        )}
      </main>
    </div>
  );
}

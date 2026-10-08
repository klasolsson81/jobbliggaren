"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useCallback, useEffect, useId, useRef, useState } from "react";
import { useTranslations } from "next-intl";
import {
  Bell,
  Bookmark,
  Briefcase,
  Building2,
  Clock,
  Inbox,
  LayoutDashboard,
  LogOut,
  Menu,
  ScrollText,
  ArrowLeftRight,
  Target,
  UserRound,
  X,
} from "lucide-react";
import { AdminNav } from "@/components/admin/admin-nav";
import { LogoutForm } from "@/components/auth/logout-form";
import { useDismissable } from "@/lib/hooks/use-dismissable";
import { HeaderStats } from "@/components/shell/header-stats";
import { HeaderStrip } from "@/components/site/header-strip";
import type { LandingStatsDto } from "@/lib/dto/landing";
import { onPlainNav } from "@/lib/nav/modified-click";

/**
 * v3 header-shell (ADR 0054 — header-meny ersätter sektionerad sidebar).
 *
 * Layout: `jp-shell` (flex column) = sticky `jp-header` + `jp-content` main.
 * Ingen sidebar, ingen desktop-burger. På <900px döljs nav-länkarna via CSS
 * och burgern öppnar `jp-drawer` från höger med samma länkar.
 *
 * Tema-logik finns INTE här — `.jp-header` är vit i båda teman via
 * CSS-scopad override (`[data-theme="dark"] .jp-header`, ADR 0052 Beslut 6).
 */

type NavLabelKey = "oversikt" | "jobb" | "ansokningar" | "foretag" | "cv";

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
  // #582 (Klas 2026-07-04) — the Företag-hubb (bevakade företag + ansökningshistorik, #448) promoted to
  // a primary header quick-link (+ mobile drawer), grouped with the job-hunt items. It stays in the
  // UserMenu too — the structural precedent is /cv, also present in both the nav and the UserMenu (that
  // one uses distinct labels, nav.cv "CV" vs userMenu.minaCv "Mina CV"; /foretag instead reuses the
  // shared nav.foretag label in both so they never drift — #582 label-align). Building2 already imported.
  { href: "/foretag", labelKey: "foretag", icon: Building2 },
  { href: "/cv", labelKey: "cv", icon: ScrollText },
];

function isActive(pathname: string, href: string): boolean {
  return pathname === href || pathname.startsWith(href + "/");
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

function UserMenu({ email }: { email: string }) {
  const t = useTranslations("common");
  const [open, setOpen] = useState(false);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const close = () => setOpen(false);
  const ref = useDismissable(open, close, triggerRef);
  const headId = useId();

  return (
    <div className="relative">
      <button
        ref={triggerRef}
        type="button"
        className="jp-icon-btn"
        aria-label={t("nav.minaSidor")}
        aria-expanded={open}
        aria-haspopup="dialog"
        onClick={() => setOpen((v) => !v)}
      >
        <UserRound size={18} aria-hidden="true" />
      </button>
      {open && (
        <div
          ref={ref}
          role="dialog"
          aria-label={t("nav.minaSidor")}
          aria-describedby={headId}
          className="jp-usermenu"
        >
          <div id={headId} className="jp-usermenu__head">
            <div>{t("userMenu.signedInAs")}</div>
            <div className="jp-usermenu__email">{email}</div>
          </div>
          <Link
            href="/mina-sidor"
            className="jp-usermenu__item"
            onClick={(e) => onPlainNav(e, close)}
          >
            <UserRound size={16} aria-hidden="true" /> {t("nav.minaSidor")}
          </Link>
          <Link
            href="/sokningar"
            className="jp-usermenu__item"
            onClick={(e) => onPlainNav(e, close)}
          >
            <Clock size={16} aria-hidden="true" /> {t("userMenu.senasteSokningar")}
          </Link>
          <Link
            href="/sparade"
            className="jp-usermenu__item"
            onClick={(e) => onPlainNav(e, close)}
          >
            <Bookmark size={16} aria-hidden="true" /> {t("userMenu.sparadeAnnonser")}
          </Link>
          <Link
            href="/matchningar"
            className="jp-usermenu__item"
            onClick={(e) => onPlainNav(e, close)}
          >
            <Target size={16} aria-hidden="true" /> {t("userMenu.minaMatchningar")}
          </Link>
          <Link
            href="/foretag"
            className="jp-usermenu__item"
            onClick={(e) => onPlainNav(e, close)}
          >
            {/* #582 — reuse the shared nav label "Företag" (hub noun) so the header quick-link and the
                UserMenu item never drift; the old "Bevakade företag" label is dropped. */}
            <Building2 size={16} aria-hidden="true" /> {t("nav.foretag")}
          </Link>
          <Link
            href="/cv"
            className="jp-usermenu__item"
            onClick={(e) => onPlainNav(e, close)}
          >
            <ScrollText size={16} aria-hidden="true" /> {t("userMenu.minaCv")}
          </Link>
          <div className="jp-usermenu__sep" role="separator" />
          <LogoutForm>
            <button
              type="submit"
              className="jp-usermenu__item"
            >
              <LogOut size={16} aria-hidden="true" /> {t("userMenu.loggaUt")}
            </button>
          </LogoutForm>
        </div>
      )}
    </div>
  );
}

function Drawer({
  open,
  onClose,
  pathname,
  adminNavigation,
  triggerRef,
}: {
  open: boolean;
  onClose: () => void;
  pathname: string;
  adminNavigation: boolean;
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
      // Full focusable set — must stay identical across every focus-trap shell
      // (input/select/textarea included so a trap never leaks to the browser
      // chrome when the panel gains a form control). SPOT-centralisation: #575.
      const focusable = panelRef.current.querySelectorAll<HTMLElement>(
        'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
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

  // A modified click must not just leave the drawer open but leave focus
  // alone: this handler also pulls focus back to the hamburger, which the
  // other two surfaces do not.
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
          <span id={labelId} className="text-body-lg font-bold">
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
        {adminNavigation ? <AdminNav variant="drawer" onNavigate={handleNav} /> : <nav className="jp-drawer__list" aria-label={t("drawer.navAriaLabel")}>
          {PRIMARY_NAV.map((item) => {
            const Icon = item.icon;
            return (
              <Link
                key={item.href}
                href={item.href}
                className="jp-drawer__item"
                aria-current={isActive(pathname, item.href) ? "page" : undefined}
                onClick={(e) => onPlainNav(e, handleNav)}
              >
                <Icon size={18} aria-hidden="true" /> {t(`nav.${item.labelKey}`)}
              </Link>
            );
          })}
          <Link
            href="/mina-sidor"
            className="jp-drawer__item"
            aria-current={isActive(pathname, "/mina-sidor") ? "page" : undefined}
            onClick={(e) => onPlainNav(e, handleNav)}
          >
            <UserRound size={18} aria-hidden="true" /> {t("nav.minaSidor")}
          </Link>
        </nav>}
      </aside>
    </>
  );
}

export function AppShell({
  email,
  isAdmin,
  initialStats,
  className,
  contentClassName = "jp-content focus:outline-none",
  children,
}: {
  email: string;
  isAdmin: boolean;
  initialStats: LandingStatsDto;
  className?: string;
  contentClassName?: string;
  children: React.ReactNode;
}) {
  const t = useTranslations("common");
  const pathname = usePathname();
  const [adminNavigation, setAdminNavigation] = useState(() => isAdmin && isActive(pathname, "/admin"));
  const showAdminNavigation = isAdmin && adminNavigation;
  const [drawerOpen, setDrawerOpen] = useState(false);
  const drawerTriggerRef = useRef<HTMLButtonElement>(null);

  const closeDrawer = useCallback(() => setDrawerOpen(false), []);

  return (
    <div className={className ? `jp-shell ${className}` : "jp-shell"}>
      <HeaderStrip
        brandHref="/oversikt"
        brandLabel={t("nav.brandHome")}
        brandPrefetch={showAdminNavigation ? false : undefined}
        className={isAdmin ? "jp-header--switchable" : undefined}
      >
        {showAdminNavigation ? <AdminNav variant="header" /> : <nav className="jp-nav jp-nav--user" aria-label={t("nav.ariaLabel")}>
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
        </nav>}

        {isAdmin && (
          <button
            type="button"
            className="jp-nav-switch jp-icon-btn"
            aria-label={t("header.adminNavigation")}
            aria-pressed={showAdminNavigation}
            title={t(showAdminNavigation ? "header.showUserNavigation" : "header.showAdminNavigation")}
            onClick={() => setAdminNavigation((value) => !value)}
          >
            <ArrowLeftRight size={16} aria-hidden="true" />
          </button>
        )}

        <span className="jp-header__spacer" />

        {/* Klas post-leverans-feedback 2026-05-24: HeaderStats återställd
            på alla auth-routes inkl. /oversikt (revertera design-reviewer
            M1 från svans-PR1). Mock-konflikten 28 vs 9 är löst i samma
            svans-PR via att /oversikt "Aktiva annonser totalt" nu också
            använder getLandingStats() — samma siffra som HeaderStats. */}
        <HeaderStats initialStats={initialStats} />

        <div className="jp-header__actions">
          <NotificationsBell />
          <UserMenu email={email} />
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
      </HeaderStrip>

      <Drawer
        open={drawerOpen}
        onClose={closeDrawer}
        pathname={pathname}
        adminNavigation={showAdminNavigation}
        triggerRef={drawerTriggerRef}
      />

      <main id="main" tabIndex={-1} className={contentClassName}>
        {children}
      </main>
    </div>
  );
}

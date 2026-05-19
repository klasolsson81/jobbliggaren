"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useCallback, useEffect, useId, useRef, useState } from "react";
import {
  Bell,
  Briefcase,
  Clock,
  Inbox,
  LogOut,
  Menu,
  ScrollText,
  Settings,
  ShieldCheck,
  X,
} from "lucide-react";
import { logoutAction } from "@/lib/auth/actions";

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

type NavItem = {
  href: string;
  label: string;
  icon: typeof Briefcase;
};

const PRIMARY_NAV: NavItem[] = [
  { href: "/jobb", label: "Jobb", icon: Briefcase },
  { href: "/ansokningar", label: "Mina ansökningar", icon: Inbox },
  { href: "/cv", label: "CV", icon: ScrollText },
];

function isActive(pathname: string, href: string): boolean {
  return pathname === href || pathname.startsWith(href + "/");
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

/**
 * Outside-click + Escape stänger. Vid stängning återförs fokus till triggern
 * (WCAG 2.4.3) om stängningen skedde via Escape eller utanför-klick.
 */
function useDismissable(
  open: boolean,
  onClose: () => void,
  triggerRef: React.RefObject<HTMLButtonElement | null>,
) {
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const onDoc = (e: MouseEvent) => {
      if (
        ref.current &&
        !ref.current.contains(e.target as Node) &&
        triggerRef.current &&
        !triggerRef.current.contains(e.target as Node)
      ) {
        onClose();
      }
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        onClose();
        triggerRef.current?.focus();
      }
    };
    document.addEventListener("mousedown", onDoc);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("mousedown", onDoc);
      document.removeEventListener("keydown", onKey);
    };
  }, [open, onClose, triggerRef]);

  return ref;
}

function NotificationsBell() {
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
        aria-label="Aviseringar"
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
            Aviseringar
          </div>
          <div className="jp-notif__list">
            <p className="jp-notif__item">Inga nya aviseringar.</p>
          </div>
        </div>
      )}
    </div>
  );
}

function UserMenu({ email, isAdmin }: { email: string; isAdmin: boolean }) {
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
        aria-label="Användarmeny"
        aria-expanded={open}
        aria-haspopup="true"
        onClick={() => setOpen((v) => !v)}
      >
        {initials(email)}
      </button>
      {open && (
        <div ref={ref} role="group" aria-label="Användarmeny" className="jp-usermenu">
          <div className="jp-usermenu__head">
            <div className="jp-usermenu__name">{local}</div>
            <div className="jp-usermenu__email">{email}</div>
          </div>
          <Link
            href="/mig"
            className="jp-usermenu__item"
            onClick={() => setOpen(false)}
          >
            <Settings size={16} aria-hidden="true" /> Inställningar
          </Link>
          <Link
            href="/sokningar"
            className="jp-usermenu__item"
            onClick={() => setOpen(false)}
          >
            <Clock size={16} aria-hidden="true" /> Senaste sökningar
          </Link>
          <Link
            href="/cv"
            className="jp-usermenu__item"
            onClick={() => setOpen(false)}
          >
            <ScrollText size={16} aria-hidden="true" /> Mina CV
          </Link>
          {isAdmin && (
            <>
              <div className="jp-usermenu__sep" role="separator" />
              <Link
                href="/admin/granskning"
                className="jp-usermenu__item"
                onClick={() => setOpen(false)}
              >
                <ShieldCheck size={16} aria-hidden="true" /> Granskning
              </Link>
            </>
          )}
          <div className="jp-usermenu__sep" role="separator" />
          <form action={logoutAction}>
            <button
              type="submit"
              className="jp-usermenu__item"
            >
              <LogOut size={16} aria-hidden="true" /> Logga ut
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
            Meny
          </span>
          <button
            ref={closeRef}
            type="button"
            className="jp-drawer__close"
            onClick={() => {
              onClose();
              triggerRef.current?.focus();
            }}
            aria-label="Stäng meny"
          >
            Stäng <X size={18} aria-hidden="true" />
          </button>
        </div>
        <nav className="jp-drawer__list" aria-label="Meny">
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
                <Icon size={18} aria-hidden="true" /> {item.label}
              </Link>
            );
          })}
          <Link
            href="/mig"
            className="jp-drawer__item"
            aria-current={isActive(pathname, "/mig") ? "page" : undefined}
            onClick={handleNav}
          >
            <Settings size={18} aria-hidden="true" /> Inställningar
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
              <ShieldCheck size={18} aria-hidden="true" /> Granskning
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
  children,
}: {
  email: string;
  isAdmin: boolean;
  children: React.ReactNode;
}) {
  const pathname = usePathname();
  const [drawerOpen, setDrawerOpen] = useState(false);
  const drawerTriggerRef = useRef<HTMLButtonElement>(null);

  const closeDrawer = useCallback(() => setDrawerOpen(false), []);

  return (
    <div className="jp-shell">
      <header className="jp-header" role="banner">
        <div className="jp-header__inner">
          <Link href="/jobb" className="jp-brand" aria-label="JobbPilot — till start">
            <span className="jp-brand__mark" aria-hidden="true">
              J
            </span>
            <span className="jp-brand__word">JobbPilot</span>
          </Link>

          <nav className="jp-nav" aria-label="Huvudnavigation">
            {PRIMARY_NAV.map((item) => (
              <Link
                key={item.href}
                href={item.href}
                className="jp-nav__link"
                aria-current={
                  isActive(pathname, item.href) ? "page" : undefined
                }
              >
                {item.label}
              </Link>
            ))}
          </nav>

          <span className="jp-header__spacer" />

          <div className="jp-header__actions">
            <NotificationsBell />
            <UserMenu email={email} isAdmin={isAdmin} />
            <button
              ref={drawerTriggerRef}
              type="button"
              className="jp-icon-btn jp-drawer-trigger"
              aria-label="Öppna meny"
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
        {children}
      </main>
    </div>
  );
}

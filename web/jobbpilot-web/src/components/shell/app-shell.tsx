"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import {
  Bell,
  Briefcase,
  Inbox,
  LogOut,
  ScrollText,
  Search,
  ShieldCheck,
  User,
} from "lucide-react";
import { logoutAction } from "@/lib/auth/actions";
import { ThemeToggle } from "@/components/theme-toggle";

type NavItem = {
  href: string;
  label: string;
  icon: typeof Briefcase;
};

type NavSection = {
  label: string;
  items: NavItem[];
};

/**
 * Sektionerad navigation (Shell Variant B). Grupplabels är mono caps.
 * ADMINISTRATION-sektionen renderas endast för admin-roll — vanliga
 * användare ser den inte (rollgejtas i AppLayout via `isAdmin`-prop).
 */
function buildSections(isAdmin: boolean): NavSection[] {
  const sections: NavSection[] = [
    { label: "Söka jobb", items: [{ href: "/jobb", label: "Jobb", icon: Briefcase }] },
    {
      label: "Mina ansökningar",
      items: [{ href: "/ansokningar", label: "Ansökningar", icon: Inbox }],
    },
    {
      label: "Min profil",
      items: [
        { href: "/cv", label: "CV", icon: ScrollText },
        { href: "/mig", label: "Konto", icon: User },
      ],
    },
  ];
  if (isAdmin) {
    sections.push({
      label: "Administration",
      items: [{ href: "/admin/granskning", label: "Granskning", icon: ShieldCheck }],
    });
  }
  return sections;
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

function LangToggle() {
  // SV/EN segmenterad — i18n är inte i scope för denna batch. SV är aktivt;
  // EN renderas som inaktiv platshållare (ingen route-växling ännu).
  return (
    <div
      role="group"
      aria-label="Språk"
      className="inline-flex h-7 overflow-hidden rounded-sm border border-border-default"
    >
      <span
        aria-current="true"
        className="font-mono px-2.5 text-[11px] font-medium tracking-wide bg-text-primary text-surface-primary flex items-center"
      >
        SV
      </span>
      <span
        aria-disabled="true"
        title="Engelska kommer senare"
        className="font-mono border-l border-border-default px-2.5 text-[11px] font-medium tracking-wide bg-surface-primary text-text-tertiary flex items-center cursor-not-allowed"
      >
        EN
      </span>
    </div>
  );
}

function NotificationsBell() {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const onDoc = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener("mousedown", onDoc);
    return () => document.removeEventListener("mousedown", onDoc);
  }, [open]);

  return (
    <div ref={ref} className="relative">
      <button
        type="button"
        className="jp-iconbtn relative"
        aria-label="Aviseringar"
        aria-expanded={open}
        onClick={() => setOpen((v) => !v)}
      >
        <Bell size={15} />
      </button>
      {open && (
        <div
          role="dialog"
          aria-label="Aviseringar"
          className="absolute right-0 top-[calc(100%+6px)] z-20 w-64 rounded-sm border border-border-default bg-surface-primary"
          style={{ boxShadow: "var(--jp-shadow-md)" }}
        >
          <div className="font-mono border-b border-border-default px-3.5 py-2.5 text-[11.5px] font-medium uppercase tracking-[0.08em] text-text-secondary">
            Aviseringar
          </div>
          <p className="px-3.5 py-4 text-body-sm text-text-secondary">
            Inga nya aviseringar.
          </p>
        </div>
      )}
    </div>
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
  const sections = buildSections(isAdmin);
  const local = email.split("@")[0] ?? email;

  return (
    <div className="jp-app" data-shell="B" data-sidebar="default">
      <aside className="jp-sidebar" aria-label="Sidonavigation">
        <div className="jp-sidebar__brand">
          <span className="jp-sidebar__wordmark">JobbPilot</span>
          <span className="jp-sidebar__tag">v2</span>
        </div>

        <nav className="jp-nav" aria-label="Huvudnavigation">
          {sections.map((section) => (
            <div key={section.label} className="jp-nav__section">
              <div className="jp-nav__label">{section.label}</div>
              {section.items.map((item) => {
                const active =
                  pathname === item.href ||
                  pathname.startsWith(item.href + "/");
                const Icon = item.icon;
                return (
                  <Link
                    key={item.href}
                    href={item.href}
                    className="jp-nav__item"
                    aria-current={active ? "page" : undefined}
                  >
                    <Icon size={16} className="jp-nav__item__icon" />
                    <span className="jp-nav__item__label">{item.label}</span>
                  </Link>
                );
              })}
            </div>
          ))}
        </nav>

        <div className="jp-sidebar__user">
          <div className="jp-avatar" aria-hidden="true">
            {initials(email)}
          </div>
          <div className="jp-sidebar__user__info">
            <div className="jp-sidebar__user__name">{local}</div>
            <div className="jp-sidebar__user__email">{email}</div>
          </div>
          <form action={logoutAction}>
            <button
              type="submit"
              className="jp-iconbtn"
              aria-label="Logga ut"
              title="Logga ut"
            >
              <LogOut size={14} />
            </button>
          </form>
        </div>
      </aside>

      <div className="jp-main">
        <header className="jp-topbar">
          <div />
          <div className="jp-topbar__actions">
            <label className="jp-search" style={{ width: 320, height: 32 }}>
              <Search size={13} />
              <input
                type="search"
                placeholder="Sök jobb, ansökningar, CV…"
                aria-label="Sök"
              />
              <span className="jp-search__kbd">⌘K</span>
            </label>
            <span
              aria-hidden="true"
              style={{
                width: 1,
                height: 18,
                background: "var(--jp-border)",
                margin: "0 4px",
              }}
            />
            <NotificationsBell />
            <ThemeToggle />
            <LangToggle />
          </div>
        </header>

        <div className="jp-pageScroll">
          <main id="main" tabIndex={-1} className="jp-page focus:outline-none">
            {children}
          </main>
        </div>
      </div>
    </div>
  );
}

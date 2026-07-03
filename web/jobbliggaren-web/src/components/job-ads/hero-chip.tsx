"use client";

import { useEffect, useRef, useState, type ReactNode } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { ChevronDown } from "lucide-react";
import { useDismissable } from "@/lib/hooks/use-dismissable";

interface HeroChipProps<T> {
  /** Triggertext (h ex. "Senaste sökningar"). Också panel-titel. */
  label: string;
  /** Ikon i triggern (lucide). */
  icon: ReactNode;
  /**
   * Totalantal items för triggerns parentes-räknare ("(N)"). Null → ingen
   * räknare visas (paritet v3-prototyp HeroChip när count == null).
   */
  count: number | null;
  /** Dropdown-items. Tom array renderar `emptyText`. */
  items: ReadonlyArray<T>;
  /** Funktion som returnerar id (för React-nyckel). */
  getKey: (item: T) => string;
  /** Render-funktion för varje rad. Får `onClose` för intern dismiss-control. */
  renderItem: (item: T, onClose: () => void) => ReactNode;
  /** Visas när items.length === 0. */
  emptyText: string;
  /** Footer-länk (typiskt "Visa alla" → /sokningar). */
  footerHref?: string;
  footerLabel?: string;
  /** Max antal items i dropdown (slice + footer). Default 5. */
  maxItems?: number;
  /**
   * Notifieras när dropdownen öppnas/stängs. Låter konsumenten lat-hämta
   * on-demand-data (t.ex. recent-search-counts) först när panelen visas —
   * undviker kostnad på sidor där användaren aldrig öppnar chippen.
   */
  onOpenChange?: (open: boolean) => void;
}

export function HeroChip<T>({
  label,
  icon,
  count,
  items,
  getKey,
  renderItem,
  emptyText,
  footerHref,
  footerLabel,
  maxItems = 5,
  onOpenChange,
}: HeroChipProps<T>) {
  const t = useTranslations("jobads.ui");
  const [open, setOpen] = useState(false);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const panelRef = useDismissable<HTMLDivElement, HTMLButtonElement>(
    open,
    () => setOpen(false),
    triggerRef,
  );

  // Notifiera konsumenten om öppna-tillståndet (lat on-demand-hämtning).
  useEffect(() => {
    onOpenChange?.(open);
  }, [open, onOpenChange]);

  const close = () => setOpen(false);
  const visible = items.slice(0, maxItems);
  const hasMore = items.length > maxItems;

  return (
    <div style={{ position: "relative" }}>
      <button
        ref={triggerRef}
        type="button"
        className="jp-hero-chip"
        aria-expanded={open}
        aria-haspopup="dialog"
        onClick={() => setOpen((v) => !v)}
      >
        {icon}
        <span>{label}</span>
        {count !== null && (
          <span className="jp-hero-chip__count">({count})</span>
        )}
        <ChevronDown size={14} aria-hidden="true" />
      </button>
      {open && (
        <div
          ref={panelRef}
          role="dialog"
          aria-label={label}
          className="jp-popover"
          style={{
            position: "absolute",
            top: "calc(100% + 6px)",
            left: 0,
            width: 320,
            zIndex: 30,
          }}
        >
          <div className="jp-popover__head">
            <span className="jp-popover__title">{label}</span>
          </div>
          <div style={{ padding: "6px 0", maxHeight: 320, overflow: "auto" }}>
            {visible.length === 0 ? (
              <div className="jp-popover__empty px-4 py-3.5">
                {emptyText}
              </div>
            ) : (
              visible.map((item) => (
                <div key={getKey(item)}>{renderItem(item, close)}</div>
              ))
            )}
          </div>
          {footerHref && (hasMore || visible.length > 0) && (
            <div className="jp-popover__foot">
              <Link
                href={footerHref}
                onClick={close}
                className="jp-popover__footlink"
              >
                {footerLabel ?? t("heroChip.showAll")}
              </Link>
            </div>
          )}
        </div>
      )}
    </div>
  );
}

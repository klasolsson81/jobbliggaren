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
  /** Where the row navigates. The host renders it as the row's `href`. */
  getHref: (item: T) => string;
  /**
   * The row's primary label text. The host owns the span and its clamp. Keep
   * it `string`: widening to `ReactNode` lets a consumer nest markup inside
   * the clamp box and re-opens the hole this seam closed. Rich labels get
   * their own slot, never a widened `getLabel`.
   */
  getLabel: (item: T) => string;
  /**
   * Demotes the primary label when true — the host applies
   * `.jp-popover__rowlabel--muted`. Omitted → never demoted.
   */
  isMuted?: (item: T) => boolean;
  /** Optional content rendered after the label (a count, a badge). */
  renderTrailing?: (item: T) => ReactNode;
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

/**
 * The host owns the row shell: the row `<Link>`, its primary label span and
 * the navigation. A consumer supplies data accessors and, at most, a trailing
 * slot — it has no row markup to get wrong, so the clamp contract is pinned
 * once here instead of once per consumer.
 *
 * The row navigates, so it is an `<a href>` and not a `<button>` + `router.push`
 * (jobbpilot-design-a11y §1). That is what gives back ctrl-/middle-click, "open
 * in new tab", the status-bar URL preview and the "link" role a screen reader
 * announces — none of which a click handler can emulate.
 */
export function HeroChip<T>({
  label,
  icon,
  count,
  items,
  getKey,
  getHref,
  getLabel,
  isMuted,
  renderTrailing,
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
                <Link
                  key={getKey(item)}
                  href={getHref(item)}
                  onClick={close}
                  className="jp-popover__rowbtn"
                >
                  <span
                    className={
                      isMuted?.(item)
                        ? "jp-popover__rowlabel jp-popover__rowlabel--muted"
                        : "jp-popover__rowlabel"
                    }
                  >
                    {getLabel(item)}
                  </span>
                  {renderTrailing?.(item)}
                </Link>
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

"use client";

// "use client": the shell owns the open panel's dismissal listeners, its measured position and the
// focus it moves on open.

import { useEffect, useId } from "react";
import { useTranslations } from "next-intl";
import { X } from "lucide-react";
import { useCloseOnFocusOut } from "@/lib/hooks/use-close-on-focus-out";
import { useDismissable } from "@/lib/hooks/use-dismissable";
import { usePanelPosition } from "@/lib/hooks/use-panel-position";
import { FOCUSABLE_SELECTOR } from "@/lib/popover/focusable";
import { focusOnOpen, isCoarsePointer } from "@/lib/popover/focus-on-open";

/** Where the shell placed the panel; the body lays itself out for it. */
export type ShellPlacement = "anchored" | "sheet";

/**
 * The one shell every `.jp-popover` root renders through (#2212): the dialog element, its place in
 * the viewport (anchored under its trigger, or a sheet under the header), dismissal by an outside
 * click or by Escape with focus back on the trigger, closing when keyboard focus leaves it, focus
 * on open, and a band that names the dialog and carries the close button in a sheet. The body is
 * the host's.
 */
export interface PopoverShellProps {
  readonly open: boolean;
  readonly onClose: () => void;
  readonly triggerRef: React.RefObject<HTMLElement | null>;
  /** The panel's width in px when anchored; the viewport may narrow it. */
  readonly preferredWidth: number;
  /** The dialog's accessible name, and the sheet band's title when the body has no band. */
  readonly label: string;
  /** The body's own root class, where it has one, beside `jp-popover`. */
  readonly className?: string;
  /** When given, its first focusable element is where focus goes on open, as `focusOnOpen` rules. */
  readonly fieldRef?: React.RefObject<HTMLElement | null>;
  /** Passed to `useDismissable`: an Escape this returns true for leaves the panel open. */
  readonly keepOpenOnEscape?: (event: KeyboardEvent) => boolean;
  /**
   * The body's own band, shown at every width. Its title carries `titleId`, which names the dialog;
   * in a sheet the close button goes at its end. Without one, a sheet gets a band of the label.
   */
  readonly band?: (parts: { titleId: string; closeButton: React.ReactNode }) => React.ReactNode;
  readonly children: React.ReactNode | ((placement: ShellPlacement) => React.ReactNode);
}

export function PopoverShell({
  open,
  onClose,
  triggerRef,
  preferredWidth,
  label,
  className,
  fieldRef,
  keepOpenOnEscape,
  band,
  children,
}: PopoverShellProps) {
  const t = useTranslations("common.dialog");
  const titleId = useId();
  const ref = useDismissable<HTMLDivElement, HTMLElement>(open, onClose, triggerRef, keepOpenOnEscape);
  const { style, placement, ready } = usePanelPosition(open, triggerRef, ref, preferredWidth);
  useCloseOnFocusOut(open, ref, triggerRef, onClose, placement);

  const target =
    placement === null ? "none" : focusOnOpen(placement, open && isCoarsePointer(), fieldRef !== undefined);

  // Once the panel has its place (WCAG 2.4.3), and again when a resize changes its placement. Focus
  // already on a shown element inside the panel stays; one a sheet's drill-down just hid does not.
  useEffect(() => {
    const panel = ref.current;
    if (!ready || target === "none" || !panel) return;
    const active = document.activeElement;
    if (active && panel.contains(active) && !active.closest("[hidden]")) return;
    const field =
      target === "field" ? fieldRef?.current?.querySelector<HTMLElement>(FOCUSABLE_SELECTOR) : null;
    (field ?? panel).focus({ preventScroll: true });
  }, [ready, target, placement, fieldRef, ref]);

  if (!open) return null;

  const sheet = placement === "sheet";
  const closeButton = sheet ? (
    <button
      type="button"
      className="jp-icon-btn"
      aria-label={t("close")}
      onClick={() => {
        onClose();
        triggerRef.current?.focus();
      }}
    >
      <X size={16} aria-hidden="true" />
    </button>
  ) : null;
  const bandNode = band
    ? band({ titleId, closeButton })
    : sheet && <PopoverBand titleId={titleId} label={label} closeButton={closeButton} />;
  const named = band !== undefined || sheet;

  return (
    <div
      ref={ref}
      className={className ? `jp-popover ${className}` : "jp-popover"}
      role="dialog"
      aria-label={named ? undefined : label}
      aria-labelledby={named ? titleId : undefined}
      style={style}
      data-placement={sheet ? "sheet" : undefined}
      tabIndex={target === "root" || sheet ? -1 : undefined}
    >
      {bandNode}
      {typeof children === "function" ? children(sheet ? "sheet" : "anchored") : children}
    </div>
  );
}

/** The shell's band: the title that names the dialog, then the close button when there is one. */
export function PopoverBand({
  titleId,
  label,
  closeButton,
}: {
  readonly titleId: string;
  readonly label: string;
  readonly closeButton: React.ReactNode;
}) {
  return (
    <div className="jp-popover__head">
      <span id={titleId} className="jp-popover__title">
        {label}
      </span>
      {closeButton}
    </div>
  );
}

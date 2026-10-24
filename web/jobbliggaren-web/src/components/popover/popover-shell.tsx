"use client";

// "use client": the shell owns the open panel's dismissal listeners, its measured position and the
// focus it moves on open.

import { useEffect } from "react";
import { useCloseOnFocusOut } from "@/lib/hooks/use-close-on-focus-out";
import { useDismissable } from "@/lib/hooks/use-dismissable";
import { usePanelPosition } from "@/lib/hooks/use-panel-position";
import { FOCUSABLE_SELECTOR } from "@/lib/popover/focusable";

/**
 * The one shell every `.jp-popover` root renders through (#2212): the dialog element, its place in
 * the viewport, Escape and outside-click dismissal with focus back on the trigger, closing when
 * keyboard focus leaves it, and focus on open. The body is the host's.
 */
export interface PopoverShellProps {
  readonly open: boolean;
  readonly onClose: () => void;
  readonly triggerRef: React.RefObject<HTMLElement | null>;
  /** The panel's width in px; the viewport may narrow it. */
  readonly preferredWidth: number;
  /** The dialog's accessible name. */
  readonly label: string;
  /** `jp-popover`, plus the body's own root class where it has one. */
  readonly className: string;
  /** When given, its first focusable element takes focus once the panel is placed. */
  readonly fieldRef?: React.RefObject<HTMLElement | null>;
  /** Passed to `useDismissable`: an Escape this returns true for leaves the panel open. */
  readonly keepOpenOnEscape?: (event: KeyboardEvent) => boolean;
  readonly children: React.ReactNode;
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
  children,
}: PopoverShellProps) {
  const ref = useDismissable<HTMLDivElement, HTMLElement>(open, onClose, triggerRef, keepOpenOnEscape);
  const { style, ready } = usePanelPosition(open, triggerRef, ref, preferredWidth);
  useCloseOnFocusOut(open, ref, triggerRef, onClose);

  // Once the panel has its place (WCAG 2.4.3); before that it sits off screen, where a focus could
  // scroll the page.
  useEffect(() => {
    if (!ready) return;
    fieldRef?.current?.querySelector<HTMLElement>(FOCUSABLE_SELECTOR)?.focus({ preventScroll: true });
  }, [ready, fieldRef]);

  if (!open) return null;

  return (
    <div ref={ref} className={className} role="dialog" aria-label={label} style={style}>
      {children}
    </div>
  );
}

"use client";

// Client Component: enkelkolumns popover-skal (öppen/stäng + position + dismiss).
// INGEN egen filtreringslogik — det bor i barn-komponenten. Används av
// Matchning-pillen i hero-filterraden (`JobbMatchGradeFilter`); 2026-06-30
// flyttades Matchning från toolbaren hit och Status-popovern togs bort.

import { PopoverShell } from "@/components/popover/popover-shell";

/**
 * Enkelkolumns popover-skal för /jobb-filterradens `[Matchning ▾]`-pill
 * (#408; flyttad till hero-raden 2026-06-30). Samma `PopoverShell` som
 * `JobbKlass2Panel` (`.jp-popover.jp-panel` `role="dialog"`, position ur
 * triggerns ref, Esc/utanför-klick + fokus-retur till triggern) men tar
 * en titel-header + children i stället för Klass-2:ans sektioner. Samma
 * `.jp-panel__sectionhead`/`.jp-popover__title`-rytm som Klass-2-panelen.
 *
 * Renderar ingenting när `open` är false (samma mönster som JobbKlass2Panel —
 * triggern äger open-staten, panelen är en ren funktion av den).
 */
interface JobbToolbarPopoverProps {
  open: boolean;
  /** Tillgängligt namn på dialogen (aria-label) + synlig header-titel. */
  title: string;
  /** Triggerns ref — position-mätning + fokus-retur vid Esc/utanför-klick. */
  triggerRef: React.RefObject<HTMLButtonElement | null>;
  onClose: () => void;
  children: React.ReactNode;
  /** Preferred panel width in px, 320 by default. The width belongs to the body, not to the shell. */
  width?: number;
  /**
   * Optional right-hand slot in the panel header — a selection count and a `.jp-clearlink`, for a body
   * whose own inline header would otherwise sit orphaned above the content and change the panel's
   * height on the first selection (measured at 55px on the #999 picker, which is more than one row).
   * The header is always rendered, so hosting them here reserves their height.
   */
  headerRight?: React.ReactNode;
}

export function JobbToolbarPopover({
  open,
  title,
  triggerRef,
  onClose,
  children,
  width = 320,
  headerRight,
}: JobbToolbarPopoverProps) {
  return (
    <PopoverShell
      open={open}
      onClose={onClose}
      triggerRef={triggerRef}
      preferredWidth={width}
      label={title}
      className="jp-popover jp-panel"
    >
      <div className="jp-panel__sectionhead">
        <span className="jp-popover__title">{title}</span>
        {headerRight}
      </div>
      <div className="jp-panel__body">{children}</div>
    </PopoverShell>
  );
}

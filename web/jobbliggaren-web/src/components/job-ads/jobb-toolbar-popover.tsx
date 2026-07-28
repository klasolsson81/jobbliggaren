"use client";

// Client Component: enkelkolumns popover-skal (öppen/stäng + position + dismiss).
// INGEN egen filtreringslogik — det bor i barn-komponenten. Används av
// Matchning-pillen i hero-filterraden (`JobbMatchGradeFilter`); 2026-06-30
// flyttades Matchning från toolbaren hit och Status-popovern togs bort.

import { useDismissable } from "@/lib/hooks/use-dismissable";
import { usePanelPosition } from "@/lib/hooks/use-panel-position";

/**
 * Enkelkolumns popover-skal för /jobb-filterradens `[Matchning ▾]`-pill
 * (#408; flyttad till hero-raden 2026-06-30). Speglar `JobbKlass2Panel`-skalet
 * (`.jp-popover.jp-panel` `role="dialog"`, `usePanelPosition` ur triggerns ref,
 * `useDismissable` för Esc/utanför-klick + fokus-retur till triggern) men tar
 * en titel-header + children i stället för Klass-2:ans sektioner. SPOT för
 * skal-infrastrukturen (DRY — CLAUDE.md §9.1): ingen ny globals.css, samma
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
  /**
   * Panel width. Defaults to the 320px the two /jobb consumers ship. A wider body (the #999 bransch
   * picker) passes a reflow-safe expression rather than a bare number — `min(<target>, calc(100vw -
   * 32px))` — because a fixed width plus a left-anchored position is the WCAG 1.4.10 failure mode at
   * 320px. Kept as a prop rather than baked in: the width belongs to the body, not to the shell.
   */
  width?: React.CSSProperties["width"];
}

export function JobbToolbarPopover({
  open,
  title,
  triggerRef,
  onClose,
  children,
  width = 320,
}: JobbToolbarPopoverProps) {
  const ref = useDismissable<HTMLDivElement>(open, onClose, triggerRef);
  const pos = usePanelPosition(open, triggerRef);

  if (!open) return null;

  const style: React.CSSProperties = pos
    ? { top: pos.top, left: pos.left, width }
    : { top: -9999, left: -9999, width };

  return (
    <div
      ref={ref}
      className="jp-popover jp-panel"
      role="dialog"
      aria-label={title}
      style={style}
    >
      <div className="jp-panel__sectionhead">
        <span className="jp-popover__title">{title}</span>
      </div>
      <div className="jp-panel__body">{children}</div>
    </div>
  );
}

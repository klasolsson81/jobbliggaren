"use client";

// "use client": an open/close popover hosting the shared criterion picker, which holds its own filter
// string. The trigger lives in the parent (it owns the ref and the open state), exactly as the ort
// cascade beside it does.

import { useMemo } from "react";
import { useTranslations } from "next-intl";
import { JobbToolbarPopover } from "@/components/job-ads/jobb-toolbar-popover";
import { CriterionPicker } from "./criterion-picker";
import {
  decomposeSelection,
  flattenCriterionOptions,
  type CriterionTreeNode,
} from "@/lib/company-criteria/criterion-options";

/**
 * #999 — the bransch (SNI) control on `/foretag/sok`, as a popover you can both browse and filter.
 *
 * WHY THIS SHAPE, given the issue asks for "/jobb's two-column dropdown". The affordance IS /jobb's:
 * the trigger, the `.jp-popover` panel, Esc/outside-click dismissal with focus return, and the
 * checkbox rhythm all come from the same family, and the ort trigger beside it literally renders
 * `JobbFilterPopover`. What is deliberately NOT adopted is that component's two-column cascade BODY,
 * for two measured reasons:
 *
 *  1. SNI 2025 is THREE levels — 22 sections, 87 divisions, 835 leaves. A two-level contract can hold
 *     two of them. Collapsing section→division drops all 835 detail codes out of the UI, which is less
 *     than the single-select typeahead this replaces could already reach; collapsing division→leaf
 *     turns the left column into an 87-row dump and loses the sections as an entry point.
 *  2. `JobbFilterPopover` has no filter field, and "a list I can open AND filter in" is the actual ask
 *     behind #999. Adding one there would put a third responsibility on a component whose documented
 *     identity is the Platsbanken two-column cascade, and would do it inside another lane's surface.
 *
 * `CriterionPicker` already answers both, is already reviewed, and already ships on the sibling browse
 * surface (Smarta bevakningar). ADR 0117 Beslut 3 makes that a requirement rather than a convenience:
 * two sibling surfaces answering "which branches exist in the register?" must not answer it with two
 * different controls. CTO bind: `docs/reviews/2026-07-28-foretag-sok-pr5-bransch-form-cto.md`.
 *
 * The selection count and the clear control live in the PANEL HEADER rather than inline in the picker:
 * the header always renders, so their height is reserved and the first selection cannot push the tree
 * down under the pointer (measured at 55px before the move — more than one 44px row).
 */
interface BranschPopoverProps {
  readonly open: boolean;
  readonly onClose: () => void;
  /** The trigger's ref — position measurement + focus return on Esc/outside click. */
  readonly triggerRef: React.RefObject<HTMLButtonElement | null>;
  /** The SNI tree (section → division → leaf), built from the already-loaded SCB reference. */
  readonly nodes: ReadonlyArray<CriterionTreeNode>;
  /** The draft selection: SNI leaf codes, the exact set the URL `sni` axis carries. */
  readonly selected: ReadonlySet<string>;
  readonly onToggle: (leafCodes: ReadonlyArray<string>) => void;
  readonly onClear: () => void;
}

export function BranschPopover({
  open,
  onClose,
  triggerRef,
  nodes,
  selected,
  onToggle,
  onClear,
}: BranschPopoverProps) {
  const t = useTranslations("pages.foretag.sok");
  // The picker's axis strings live one scope up, shared with the criterion dialog that renders the
  // same component — they were byte-identical duplicates under this page's namespace before #999.
  const tc = useTranslations("pages.foretag.criteria");
  // This popover hosts the picker's clear control in its own panel header rather
  // than inline, so the LABEL is the picker's even though the button is not —
  // it must stay byte-identical to the one `CriterionPicker` renders inline.
  const tp = useTranslations("components.criterionPicker");
  const options = useMemo(() => flattenCriterionOptions(nodes), [nodes]);
  // Counts what was PICKED, not what it expanded to. `selected.size` reports "52 valda branscher" for
  // one click on a section while the chip row outside shows ONE chip — same axis, same screen, two
  // numbers. The decomposition is the number that describes the action the user took.
  const pickedCount = useMemo(
    () => decomposeSelection(nodes, selected).length,
    [nodes, selected],
  );

  return (
    <JobbToolbarPopover
      open={open}
      // Matches the trigger's visible name, so the screen reader announces the same thing the pointer
      // user clicked (the ort popover's `dialogLabel` does the same).
      title={t("branschTrigger")}
      triggerRef={triggerRef}
      onClose={onClose}
      // Wide enough for three indent levels of Swedish SNI names, and never wider than the viewport
      // (WCAG 1.4.10 — verified rendered at 320px, not assumed).
      width="min(560px, calc(100vw - 32px))"
      headerRight={
        <span className="flex items-center gap-3">
          {/* Polite, and rendered UNCONDITIONALLY — emptied rather than unmounted. The count changes
              without focus moving (one click on a section takes it from nothing to a whole subtree,
              and the row's own `aria-checked` does not carry the total), and a live region that
              MOUNTS with its content already in place is the unreliable form. Gating this on
              `pickedCount > 0` made the 0→1 transition — the first pick — exactly that case. Only
              the clear control is conditional; there is nothing to clear at zero. */}
          <span
            className="text-caption tabular-nums text-text-secondary"
            aria-live="polite"
          >
            {pickedCount > 0 ? tc("sniSelectedCount", { count: pickedCount }) : ""}
          </span>
          {pickedCount > 0 && (
            <button type="button" className="jp-clearlink" onClick={onClear}>
              {tp("clear")}
            </button>
          )}
        </span>
      }
    >
      {/* `.jp-panel__body` carries the scroll cap but no inset; the picker's own rows are edge-to-edge
          inside their bordered box, so the padding belongs here. */}
      <div className="px-4 pt-1 pb-4">
        <CriterionPicker
          nodes={nodes}
          options={options}
          selected={selected}
          onToggle={onToggle}
          // `help` STAYS. The panel header says "Välj bransch", which names the control but not its
          // mechanics: that a checkbox on a parent selects its whole subtree, and that more than one
          // branch can be picked. One click here can select 52 codes, and after #999 this is the only
          // sentence on /foretag/sok that says so. `filterHint` is the one dropped instead — it sat
          // under a field already labelled "Sök bransch" and paid no rent (finding 9).
          help={tc("sniHelp")}
          filterLabel={tc("sniFilterLabel")}
          groupAria={tc("sniGroupAria")}
          expandAria={(name) => tc("sniExpandAria", { name })}
          collapseAria={(name) => tc("sniCollapseAria", { name })}
          optionsUnavailable={t("branschUnavailable")}
        />
      </div>
    </JobbToolbarPopover>
  );
}

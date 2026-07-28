"use client";

// "use client": an open/close popover hosting the shared criterion picker, which holds its own filter
// string. The trigger lives in the parent (it owns the ref and the open state), exactly as the ort
// cascade beside it does.

import { useTranslations } from "next-intl";
import { JobbToolbarPopover } from "@/components/job-ads/jobb-toolbar-popover";
import { CriterionPicker } from "./criterion-picker";
import {
  flattenCriterionOptions,
  type CriterionTreeNode,
} from "@/lib/company-criteria/criterion-options";
import { useMemo } from "react";

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
 * different controls. CTO bind: `docs/reviews/2026-07-27-foretag-sok-pr5-bransch-form-cto.md`.
 *
 * `heading`/`help` are omitted: the panel header already says "Bransch", so both would repeat it.
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
  const options = useMemo(() => flattenCriterionOptions(nodes), [nodes]);

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
    >
      {/* `.jp-panel__body` carries the scroll cap but no inset; the picker's own rows are edge-to-edge
          inside their bordered box, so the padding belongs here. */}
      <div className="px-4 pt-1 pb-4">
        <CriterionPicker
          nodes={nodes}
          options={options}
          selected={selected}
          onToggle={onToggle}
          onClear={onClear}
          filterLabel={t("branschFilterLabel")}
          filterHint={t("branschFilterHint")}
          groupAria={t("branschTrigger")}
          selectedCountLabel={t("branschSelectedCount", { count: selected.size })}
          optionsUnavailable={t("branschUnavailable")}
        />
      </div>
    </JobbToolbarPopover>
  );
}

"use client";

// "use client": a hierarchical checkbox tree with per-node expand/collapse state and keyboard toggle.
// A fresh component (RF-4): the JobTech ort cascade is a different code namespace and a different
// shape (two disjoint geo axes, no expansion). Here every axis is a plain leaf-code Set and a parent
// EXPANDS to its leaves, so a generic depth-agnostic tree serves both the 3-level SNI picker and the
// 2-level län→kommun picker.

import { useId, useState } from "react";
import { ChevronRight, Check, Minus } from "lucide-react";
import { groupTriState, type TriState } from "@/lib/company-criteria/criterion-selection";

/**
 * One node in the picker tree. `leafCodes` are the WIRE leaf codes this node covers (a leaf node
 * carries its own code as the single element); toggling a node toggles that whole group. `children`
 * is absent/empty for a leaf.
 */
export interface CriterionTreeNode {
  readonly code: string;
  readonly name: string;
  readonly leafCodes: ReadonlyArray<string>;
  readonly children?: ReadonlyArray<CriterionTreeNode>;
}

interface CriterionTreeProps {
  readonly nodes: ReadonlyArray<CriterionTreeNode>;
  readonly selected: ReadonlySet<string>;
  /** Toggle a node's whole leaf group (add all if not all present, else remove all). */
  readonly onToggle: (leafCodes: ReadonlyArray<string>) => void;
  readonly groupAriaLabel: string;
  readonly expandAria: (name: string) => string;
  readonly collapseAria: (name: string) => string;
}

/**
 * The hierarchical checkbox tree. Non-leaf rows carry an expand toggle (chevron) AND a tri-state
 * checkbox: the checkbox selects/deselects the whole subtree via its leaf codes, the chevron only
 * reveals children. A parent's state is derived upward from the leaf selection (checked / mixed /
 * unchecked). Following the ort-cascade a11y stance, the containers are `role="group"` and rows are
 * `role="checkbox"` (tabbable) — not a full ARIA tree widget (which would owe roving tabindex).
 */
export function CriterionTree({
  nodes,
  selected,
  onToggle,
  groupAriaLabel,
  expandAria,
  collapseAria,
}: CriterionTreeProps) {
  return (
    <div role="group" aria-label={groupAriaLabel} className="flex flex-col">
      {nodes.map((node) => (
        <TreeRow
          key={node.code}
          node={node}
          depth={0}
          selected={selected}
          onToggle={onToggle}
          expandAria={expandAria}
          collapseAria={collapseAria}
        />
      ))}
    </div>
  );
}

function TreeRow({
  node,
  depth,
  selected,
  onToggle,
  expandAria,
  collapseAria,
}: {
  readonly node: CriterionTreeNode;
  readonly depth: number;
  readonly selected: ReadonlySet<string>;
  readonly onToggle: (leafCodes: ReadonlyArray<string>) => void;
  readonly expandAria: (name: string) => string;
  readonly collapseAria: (name: string) => string;
}) {
  const [open, setOpen] = useState(false);
  const panelId = useId();
  const hasChildren = (node.children?.length ?? 0) > 0;
  const state = groupTriState(selected, node.leafCodes);

  return (
    <div>
      <div
        className="flex items-center gap-1 border-b border-border last:border-b-0"
        style={{ paddingInlineStart: depth * 20 }}
      >
        {hasChildren ? (
          <button
            type="button"
            className="inline-flex size-8 shrink-0 items-center justify-center rounded-md text-text-secondary hover:bg-surface-secondary hover:text-text-primary"
            aria-expanded={open}
            aria-controls={panelId}
            aria-label={open ? collapseAria(node.name) : expandAria(node.name)}
            onClick={() => setOpen((prev) => !prev)}
          >
            <ChevronRight
              size={16}
              aria-hidden="true"
              className={open ? "rotate-90 transition-transform" : "transition-transform"}
            />
          </button>
        ) : (
          <span className="size-8 shrink-0" aria-hidden="true" />
        )}

        <div
          role="checkbox"
          aria-checked={state === "indeterminate" ? "mixed" : state === "checked"}
          tabIndex={0}
          onClick={() => onToggle(node.leafCodes)}
          onKeyDown={(e) => {
            if (e.key === " " || e.key === "Enter") {
              e.preventDefault();
              onToggle(node.leafCodes);
            }
          }}
          className="flex flex-1 cursor-pointer items-center gap-2.5 py-2 pe-2 text-body-sm text-text-primary"
        >
          <CheckBox state={state} />
          <span>{node.name}</span>
        </div>
      </div>

      {hasChildren && open && (
        <div id={panelId} role="group" aria-label={node.name}>
          {node.children!.map((child) => (
            <TreeRow
              key={child.code}
              node={child}
              depth={depth + 1}
              selected={selected}
              onToggle={onToggle}
              expandAria={expandAria}
              collapseAria={collapseAria}
            />
          ))}
        </div>
      )}
    </div>
  );
}

/** The tri-state box: filled brand for checked/mixed (Check / Minus glyph), outlined for unchecked. */
export function CheckBox({ state }: { state: TriState }) {
  const filled = state === "checked" || state === "indeterminate";
  return (
    <span
      aria-hidden="true"
      className={
        filled
          ? "inline-flex size-[18px] shrink-0 items-center justify-center rounded-sm border-2 border-brand-600 bg-brand-600 text-white"
          : "inline-flex size-[18px] shrink-0 items-center justify-center rounded-sm border-2 border-border-strong bg-surface-primary"
      }
    >
      {state === "checked" && <Check size={13} aria-hidden="true" />}
      {state === "indeterminate" && <Minus size={13} aria-hidden="true" />}
    </span>
  );
}

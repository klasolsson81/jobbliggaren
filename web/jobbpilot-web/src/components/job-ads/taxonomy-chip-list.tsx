"use client";

import { X } from "lucide-react";

interface TaxonomyChip {
  conceptId: string;
  label: string;
}

interface TaxonomyChipListProps {
  // Civic-utility: chippar visar NAMN (label), aldrig font-mono concept-id
  // (ADR 0043 — concept-id försvinner ur UI:t). label kommer från trädet
  // eller reverse-lookup; renderas som ren TEXT, aldrig dangerouslySetInnerHTML
  // (security-auditor FE-flagga 2026-05-17).
  items: ReadonlyArray<TaxonomyChip>;
  // Tillgänglig etikett för listan, t.ex. "Valda län".
  ariaLabel: string;
  onRemove: (conceptId: string) => void;
}

/**
 * ADR 0043 — vald-taxonomi som namn-chippar med dismiss. Delas av läns-
 * och yrkes-väljaren. Civic-utility (jobbpilot-design-components): chip =
 * text + dismiss-X, neutral surface, ingen färgad bakgrund, inga ikoner
 * som dekoration. Hit-area på X expanderas via padding (a11y §9: ≥32px
 * in-app, ≥44px touch).
 */
export function TaxonomyChipList({
  items,
  ariaLabel,
  onRemove,
}: TaxonomyChipListProps) {
  if (items.length === 0) return null;

  return (
    <ul className="flex flex-wrap gap-2" aria-label={ariaLabel}>
      {items.map((item) => (
        <li key={item.conceptId}>
          <span className="inline-flex items-center gap-1.5 rounded-md border border-border-default bg-surface-secondary py-1 pl-2.5 text-body-sm text-text-primary">
            {item.label}
            <button
              type="button"
              onClick={() => onRemove(item.conceptId)}
              className="inline-flex items-center justify-center rounded-sm p-2 text-text-secondary hover:text-text-primary focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring max-md:p-2.5"
              aria-label={`Ta bort ${item.label}`}
            >
              <X className="size-3.5" aria-hidden="true" />
            </button>
          </span>
        </li>
      ))}
    </ul>
  );
}

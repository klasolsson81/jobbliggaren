"use client";

import { useTranslations } from "next-intl";

interface ApplicationsBulkBarProps {
  count: number;
  onMarkGhosted: () => void;
  onMarkRejected: () => void;
  onClear: () => void;
  pending: boolean;
}

/**
 * Massåtgärdsraden för Tabell-vyn (#630 PR 10, design §7). Mörk rad (samma
 * ink-1-bakgrund + ink-inverse-text som ångra-toasten — båda tokens flippar i
 * dark så raden förblir mörk-mot-ljus i BÅDA teman) som glider in ovanför
 * tabellen när minst en rad är markerad.
 *
 * Antals-regionen är en egen `role="status" aria-live="polite"` så en ändring i
 * urvalet annonseras utan att flytta fokus. "Markera Nekad" (destruktiv) grindas
 * av en bekräftelsedialog i containern; "Markera Inget svar" (Ghosted) körs direkt
 * med ångra-toast. Knapparna disable:as under det pågående batch-anropet.
 */
export function ApplicationsBulkBar({
  count,
  onMarkGhosted,
  onMarkRejected,
  onClear,
  pending,
}: ApplicationsBulkBarProps) {
  const tUi = useTranslations("applications.ui");

  return (
    <div
      className="jp-bulkbar"
      role="group"
      aria-label={tUi("bulk.barAriaLabel")}
    >
      <span className="jp-bulkbar__count" role="status" aria-live="polite">
        {tUi("bulk.selectedCount", { count })}
      </span>
      <div className="jp-bulkbar__actions">
        <button
          type="button"
          className="jp-bulkbar__btn"
          onClick={onMarkGhosted}
          disabled={pending}
        >
          {tUi("bulk.markGhosted")}
        </button>
        <button
          type="button"
          className="jp-bulkbar__btn"
          onClick={onMarkRejected}
          disabled={pending}
        >
          {tUi("bulk.markRejected")}
        </button>
        <button
          type="button"
          className="jp-bulkbar__btn jp-bulkbar__btn--ghost"
          onClick={onClear}
          disabled={pending}
        >
          {tUi("bulk.clear")}
        </button>
      </div>
    </div>
  );
}

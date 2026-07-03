"use client";

import { useFormatter, useTranslations } from "next-intl";
import { Check } from "lucide-react";
import type { TaxonomyOption } from "@/lib/dto/taxonomy";
import { formatNumber } from "@/lib/i18n/format";
import { useDismissable } from "@/lib/hooks/use-dismissable";
import { usePanelPosition } from "@/lib/hooks/use-panel-position";

/**
 * Klass-2-filterpanel (ADR 0067 Fas E, rad 109 "Filter-panel", 2026-06-13).
 * EN-kolumns popover — INTE den tvåkolumns-kaskade `JobbFilterPopover`.
 * Speglar Platsbankens panel: staplade sektioner, varje med en röd
 * "Rensa"-text-länk (`.jp-clearlink`) uppe till höger.
 *
 * Sektioner (Klas-låsta produktbeslut):
 * - **Omfattning** (worktimeExtent) = RADIO single-select: "Alla" (= inget
 *   filter) / Heltid / Deltid. Val mappar till en worktimeExtent-array med
 *   0 eller 1 element.
 * - **Anställningsform** (employmentType) = CHECKBOX multi-select: ALLA
 *   options ur `taxonomy.employmentTypes` med deras RIKTIGA JobTech-labels
 *   ("honest 8" — ingen kurering/om-etikettering/utelämning).
 *
 * Facet-counts (PR-3): per-option-antal ("Heltid (29 427)") via debouncade
 * `useFacetCounts`-hooks (föräldern äger). null → inga tal renderas (degraderad/
 * pre-fetch); panelen är fullt funktionell utan dem. "Alla"-radion bär INGET tal
 * (summan ägs av list-svarets totalCount, SPOT — samma som Yrkes "Välj alla").
 *
 * Shell-infrastruktur (DRY, CLAUDE.md §9.1): återanvänder `useDismissable`
 * (Esc stänger + fokus-retur till triggern, klick-utanför) och samma
 * position-mätning som `JobbFilterPopover` så fokus/outside-click/Esc beter
 * sig konsekvent (jobbliggaren-design-a11y / ADR 0047).
 */

// "Alla"-radens sentinel-värde i radio-gruppen — INTE ett conceptId. Tom
// worktimeExtent-array = inget filter; "Alla" är radio-UI-representationen.
const WORKTIME_ALL = "__all__";

interface JobbKlass2PanelProps {
  open: boolean;
  /** Anställningsform-options (råa JobTech-labels, "honest 8"). */
  employmentTypeOptions: ReadonlyArray<TaxonomyOption>;
  /** Omfattning-options (Heltid/Deltid; "Alla" adderas i panelen). */
  worktimeExtentOptions: ReadonlyArray<TaxonomyOption>;
  /** Valda anställningsform-conceptId (checkbox-multi). */
  employmentType: ReadonlyArray<string>;
  /** Valt omfattning-conceptId (radio-single → 0–1 element). */
  worktimeExtent: ReadonlyArray<string>;
  /** Per-option facet-counts (PR-3) — null = inga tal (degraderad/pre-fetch). */
  employmentTypeCounts?: Record<string, number> | null;
  worktimeExtentCounts?: Record<string, number> | null;
  /** Live-commit: hela nästa anställningsform-listan. */
  onEmploymentTypeChange: (next: string[]) => void;
  /** Live-commit: 0 eller 1 omfattning-conceptId (radio). */
  onWorktimeExtentChange: (next: string[]) => void;
  onClose: () => void;
  /** Triggerns ref — fokus-retur vid Esc/utanför-klick (a11y). */
  triggerRef: React.RefObject<HTMLButtonElement | null>;
  /** Footer-yta ("Visa N annonser"-knappen — föräldern äger). */
  footer?: React.ReactNode;
  /** Civil degradering när options inte kunde laddas. */
  emptyText: string;
}

function toggleEmployment(
  selected: ReadonlyArray<string>,
  conceptId: string,
  onChange: (next: string[]) => void,
) {
  onChange(
    selected.includes(conceptId)
      ? selected.filter((v) => v !== conceptId)
      : [...selected, conceptId],
  );
}

export function JobbKlass2Panel({
  open,
  employmentTypeOptions,
  worktimeExtentOptions,
  employmentType,
  worktimeExtent,
  employmentTypeCounts,
  worktimeExtentCounts,
  onEmploymentTypeChange,
  onWorktimeExtentChange,
  onClose,
  triggerRef,
  footer,
  emptyText,
}: JobbKlass2PanelProps) {
  const t = useTranslations("jobads.ui");
  const format = useFormatter();
  const ref = useDismissable<HTMLDivElement>(open, onClose, triggerRef);
  const pos = usePanelPosition(open, triggerRef);

  if (!open) return null;

  const style: React.CSSProperties = pos
    ? { top: pos.top, left: pos.left, width: 320 }
    : { top: -9999, left: -9999, width: 320 };

  // Aktivt radio-värde: första (enda) valda conceptId eller "Alla"-sentinel.
  const activeWorktime = worktimeExtent[0] ?? WORKTIME_ALL;
  // "Alla" först, därefter options as-is (backend sorterar Label Ordinal →
  // Deltid före Heltid; ren as-is-rendering per Klas-constraint, flaggat).
  const worktimeRadioOptions: ReadonlyArray<{ value: string; label: string }> =
    [
      { value: WORKTIME_ALL, label: t("klass2.worktimeAll") },
      ...worktimeExtentOptions.map((o) => ({
        value: o.conceptId,
        label: o.label,
      })),
    ];

  const noOptions =
    employmentTypeOptions.length === 0 && worktimeExtentOptions.length === 0;

  return (
    <div
      ref={ref}
      className="jp-popover jp-panel"
      role="dialog"
      aria-label={t("klass2.dialogLabel")}
      style={style}
    >
      {noOptions ? (
        <div className="jp-popover__empty p-4">
          {emptyText}
        </div>
      ) : (
        <div className="jp-panel__body">
          {/* ── Omfattning (radio single-select) ── */}
          <div className="jp-panel__section">
            <div className="jp-panel__sectionhead">
              <span className="jp-popover__title">{t("klass2.worktimeHeading")}</span>
              {worktimeExtent.length > 0 && (
                <button
                  type="button"
                  className="jp-clearlink"
                  onClick={() => onWorktimeExtentChange([])}
                >
                  {t("klass2.clear")}
                </button>
              )}
            </div>
            <div
              role="radiogroup"
              aria-label={t("klass2.worktimeHeading")}
              className="jp-panel__group"
            >
              {worktimeRadioOptions.map((opt, index) => {
                const checked = activeWorktime === opt.value;
                return (
                  <div
                    key={opt.value}
                    className="jp-radioitem"
                    role="radio"
                    aria-checked={checked}
                    // Roving tabindex: bara den valda radion är i tab-ordningen
                    // (en tab-stop för hela gruppen, WAI-ARIA radiogroup).
                    tabIndex={checked ? 0 : -1}
                    onClick={() =>
                      onWorktimeExtentChange(
                        opt.value === WORKTIME_ALL ? [] : [opt.value],
                      )
                    }
                    onKeyDown={(e) => {
                      if (e.key === " " || e.key === "Enter") {
                        e.preventDefault();
                        onWorktimeExtentChange(
                          opt.value === WORKTIME_ALL ? [] : [opt.value],
                        );
                        return;
                      }
                      // Pilnavigering (WAI-ARIA radiogroup): flytta val + fokus.
                      if (
                        e.key === "ArrowDown" ||
                        e.key === "ArrowRight" ||
                        e.key === "ArrowUp" ||
                        e.key === "ArrowLeft"
                      ) {
                        e.preventDefault();
                        const dir =
                          e.key === "ArrowDown" || e.key === "ArrowRight"
                            ? 1
                            : -1;
                        const nextIndex =
                          (index + dir + worktimeRadioOptions.length) %
                          worktimeRadioOptions.length;
                        const next = worktimeRadioOptions[nextIndex];
                        if (!next) return;
                        onWorktimeExtentChange(
                          next.value === WORKTIME_ALL ? [] : [next.value],
                        );
                        const parent = e.currentTarget.parentElement;
                        const target = parent?.children[nextIndex];
                        if (target instanceof HTMLElement) target.focus();
                      }
                    }}
                  >
                    <span className="jp-radioitem__dot" aria-hidden="true">
                      {checked && <span className="jp-radioitem__fill" />}
                    </span>
                    {opt.label}
                    {/* "Alla"-radion bär inget tal (summan = totalCount, SPOT). */}
                    {opt.value !== WORKTIME_ALL && worktimeExtentCounts && (
                      <span className="jp-radioitem__count">
                        ({formatNumber(format, worktimeExtentCounts[opt.value] ?? 0)})
                      </span>
                    )}
                  </div>
                );
              })}
            </div>
          </div>

          {/* ── Anställningsform (checkbox multi-select) ── */}
          <div className="jp-panel__section">
            <div className="jp-panel__sectionhead">
              <span className="jp-popover__title">{t("klass2.employmentHeading")}</span>
              {employmentType.length > 0 && (
                <button
                  type="button"
                  className="jp-clearlink"
                  onClick={() => onEmploymentTypeChange([])}
                >
                  {t("klass2.clear")}
                </button>
              )}
            </div>
            <div role="group" aria-label={t("klass2.employmentHeading")}>
              {employmentTypeOptions.map((opt) => {
                const checked = employmentType.includes(opt.conceptId);
                return (
                  <div
                    key={opt.conceptId}
                    className="jp-checkitem"
                    role="checkbox"
                    aria-checked={checked}
                    tabIndex={0}
                    onClick={() =>
                      toggleEmployment(
                        employmentType,
                        opt.conceptId,
                        onEmploymentTypeChange,
                      )
                    }
                    onKeyDown={(e) => {
                      if (e.key === " " || e.key === "Enter") {
                        e.preventDefault();
                        toggleEmployment(
                          employmentType,
                          opt.conceptId,
                          onEmploymentTypeChange,
                        );
                      }
                    }}
                  >
                    <span className="jp-checkitem__box">
                      {checked && <Check size={14} aria-hidden="true" />}
                    </span>
                    {opt.label}
                    {employmentTypeCounts && (
                      <span className="jp-checkitem__count">
                        ({formatNumber(format, employmentTypeCounts[opt.conceptId] ?? 0)})
                      </span>
                    )}
                  </div>
                );
              })}
            </div>
          </div>
        </div>
      )}
      {footer && <div className="jp-popover__foot">{footer}</div>}
    </div>
  );
}

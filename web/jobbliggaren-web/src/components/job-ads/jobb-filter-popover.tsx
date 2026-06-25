"use client";

import { useEffect, useMemo, useState } from "react";
import { useFormatter, useTranslations } from "next-intl";
import { Check, ChevronRight } from "lucide-react";
import { formatNumber } from "@/lib/i18n/format";
import { useDismissable } from "@/lib/hooks/use-dismissable";

/**
 * Platsbanken-mönster filter-popover (HANDOVER-v3.md §5.4, ADR 0055).
 * STRUKTUR-referens: src-v3/jobb.jsx `FilterPopover` — pixel-nära, men
 * window-globals/mock ersatta med conceptId↔label-kontraktet (ADR 0043 ACL)
 * och live searchParams-commit (ADR 0042 Beslut B, OFÖRÄNDRAT).
 *
 * Tvåkolumns kaskad: vänster = grupper (yrkesområden/län) som
 * navigationsrader, höger = aktiva gruppens val med "Välj alla"-rad överst.
 * Fas E2b (CTO VAL 3, docs/reviews/2026-06-11-sok-paritet-e2b-cto.md):
 * kontraktet är AXEL-MEDVETET via optionala `groupAxis`-props — "Välj
 * alla"-raden kan toggla GRUPPENS conceptId i en egen axel (Ort: hela
 * länet = ETT region-id; kommun-rader = municipality-axeln) i stället för
 * att materialisera höger-kolumnens ids i `selected`. Yrke utelämnar
 * `groupAxis` = degenererat enaxel-fall (parameterisering med data, inte
 * mode-flagga — Flag Argument-smell avvisat). Det tidigare enkelkolumns-
 * läget (Ort som platt Län-lista) utgick i E2b — noll konsumenter.
 *
 * Ingen footer, ingen Använd/Stäng-knapp (ADR 0055 Beslut 2). ESC + klick
 * utanför stänger, fokus återförs till triggern (jobbliggaren-design-a11y,
 * delat `useDismissable`-idiom — DRY, CLAUDE.md §9.1).
 */

export interface PopoverGroup {
  /** conceptId för gruppen (vänsterrad — yrkesområde/län). */
  conceptId: string;
  label: string;
  /** Val under gruppen (yrkesgrupper resp. kommuner). */
  items: ReadonlyArray<PopoverItem>;
}

export interface PopoverItem {
  /** conceptId som emitteras till URL (ADR 0042 Beslut B). */
  conceptId: string;
  label: string;
}

interface JobbFilterPopoverProps {
  open: boolean;
  /** conceptId-lista för ITEM-axeln (occupationGroup eller municipality). */
  selected: ReadonlyArray<string>;
  /** Live-commit: emitterar hela nästa conceptId-listan (item-axeln). */
  onChange: (next: string[]) => void;
  onClose: () => void;
  /** Återställ ALLA axlar denna picker äger (header-Rensa). */
  onClearAll: () => void;
  /** Triggerns ref — fokus-retur vid ESC/utanför-klick (a11y). */
  triggerRef: React.RefObject<HTMLButtonElement | null>;
  /** Vänster kolumn-titel (t.ex. "Yrkesområde", "Län"). */
  leftTitle: string;
  /**
   * Dialogens `aria-label` (E2d-Minor): bör matcha TRIGGERNS namn ("Ort"/
   * "Yrke") så skärmläsaren annonserar samma sak som pillen. Utelämnad →
   * faller till `leftTitle` (bakåtkompat).
   */
  dialogLabel?: string;
  /** Höger kolumn-titel (t.ex. "Yrkesgrupper", "Kommuner"). */
  rightTitle: string;
  /**
   * "Välj alla X"-radens text, GRUPP-specifik (E2d-Minor): Ort ger
   * "Hela Stockholms län" per aktivt län; Yrke ger statiskt "Välj alla
   * yrkesgrupper". Funktion av aktiv grupp i stället för en enda statisk
   * sträng (per-grupp-precision, jobbliggaren-design-copy).
   */
  selectAllLabel: (group: PopoverGroup) => string;
  groups: ReadonlyArray<PopoverGroup>;
  /** Civil degradering när grupperna inte kunde laddas. */
  emptyText: string;
  /** Höger kolumns tomtext (grupp utan val). */
  rightEmptyText: string;
  /**
   * Axel-medveten "Välj alla" (Ort, CTO VAL 3): raden togglar AKTIVA
   * gruppens conceptId i en EGEN axel (region) i stället för att
   * materialisera höger-kolumnens items i `selected`. `onClearColumn`
   * rensar båda axlarna för EN grupp (höger-kolumnens Rensa). Utelämnad
   * (Yrke) → "Välj alla" materialiserar item-ids i `selected` som förut.
   */
  groupAxis?: {
    selected: ReadonlyArray<string>;
    onToggleGroup: (groupConceptId: string) => void;
    onClearColumn: (groupConceptId: string) => void;
    /**
     * E2f (Klas rendered-feedback 2026-06-11): item-klick i dual-axis-läget
     * går till föräldern med BÅDA id:na — föräldern äger semantiken
     * ("hela länet minus kommun X" kräver kunskap om båda axlarna).
     */
    onToggleItem: (itemConceptId: string, groupConceptId: string) => void;
  };
  /**
   * Fas E2c (ADR 0067 Beslut 4) — per-option-counts för höger-kolumnens
   * item-rader (concept-id → count; saknad nyckel = 0). `null`/utelämnad =
   * counts ej laddade/degraderade → inga tal visas (popovern fullt
   * användbar — counts är en hint, aldrig en förutsättning).
   */
  counts?: Record<string, number> | null;
  /** Count för "Hela länet"-raden (gruppens eget id i grupp-facetten). */
  groupCounts?: Record<string, number> | null;
  /** Footer-yta ("Visa N annonser"-knappen, CTO VAL 2 — föräldern äger). */
  footer?: React.ReactNode;
}

// Position härleds ur triggerns ref INNE I en effect (refs får inte läsas
// under render — react-hooks/refs). Mätningen sker efter mount/öppning och
// uppdateras vid scroll/resize.
function usePopoverPosition(
  open: boolean,
  triggerRef: React.RefObject<HTMLButtonElement | null>,
) {
  const [pos, setPos] = useState<{ top: number; left: number } | null>(null);

  useEffect(() => {
    const trigger = triggerRef.current;
    if (!open || !trigger) {
      setPos(null);
      return;
    }
    const measure = () => {
      const r = trigger.getBoundingClientRect();
      setPos({
        top: r.bottom + 8 + window.scrollY,
        left: r.left + window.scrollX,
      });
    };
    measure();
    window.addEventListener("resize", measure);
    window.addEventListener("scroll", measure, true);
    return () => {
      window.removeEventListener("resize", measure);
      window.removeEventListener("scroll", measure, true);
    };
  }, [open, triggerRef]);

  return pos;
}

function toggle(
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

/** "Välj alla"/"Avmarkera alla" för en grupp av conceptId (enaxel-fallet). */
function toggleAll(
  selected: ReadonlyArray<string>,
  groupIds: ReadonlyArray<string>,
  allSelected: boolean,
  onChange: (next: string[]) => void,
) {
  if (allSelected) {
    onChange(selected.filter((v) => !groupIds.includes(v)));
    return;
  }
  const next = [...selected];
  for (const id of groupIds) if (!next.includes(id)) next.push(id);
  onChange(next);
}

function CheckRow({
  label,
  checked,
  onToggle,
  isAll,
  count,
  indeterminate,
}: {
  label: string;
  checked: boolean;
  onToggle: () => void;
  isAll?: boolean;
  /** Per-option-count (E2c) — undefined = counts ej laddade, inget tal. */
  count?: number;
  /**
   * Tri-state (E2d-Minor): vid partiellt val annonserar "Välj alla"-raden
   * `aria-checked="mixed"` (WAI-ARIA tri-state-checkbox) i stället för
   * false — skärmläsaren hör "delvis markerad", inte "omarkerad".
   */
  indeterminate?: boolean;
}) {
  const format = useFormatter();
  return (
    <div
      className={isAll ? "jp-checkitem jp-checkitem--all" : "jp-checkitem"}
      role="checkbox"
      aria-checked={indeterminate ? "mixed" : checked}
      tabIndex={0}
      onClick={onToggle}
      onKeyDown={(e) => {
        if (e.key === " " || e.key === "Enter") {
          e.preventDefault();
          onToggle();
        }
      }}
    >
      <span className="jp-checkitem__box">
        {checked && <Check size={14} aria-hidden="true" />}
      </span>
      {label}
      {count !== undefined && (
        <span className="jp-checkitem__count">
          ({formatNumber(format, count)})
        </span>
      )}
    </div>
  );
}

export function JobbFilterPopover({
  open,
  selected,
  onChange,
  onClose,
  onClearAll,
  triggerRef,
  leftTitle,
  dialogLabel,
  rightTitle,
  selectAllLabel,
  groups,
  emptyText,
  rightEmptyText,
  groupAxis,
  counts,
  groupCounts,
  footer,
}: JobbFilterPopoverProps) {
  const t = useTranslations("jobads.ui");
  const ref = useDismissable<HTMLDivElement>(open, onClose, triggerRef);
  const pos = usePopoverPosition(open, triggerRef);

  // Aktiv grupp (vänsterrad). E2f (Klas rendered-feedback 2026-06-11,
  // Platsbanken-paritet): startar TOM — höger kolumn visas först när
  // användaren valt ett län/yrkesområde (ingen auto-vald första grupp).
  // Reset till tom vid varje öppning via `key`-remount i föräldern — INTE
  // setState i en effect (react-hooks/set-state-in-effect).
  const [activeLeft, setActiveLeft] = useState<string | null>(null);

  const selectedSet = useMemo(() => new Set(selected), [selected]);
  const groupSelectedSet = useMemo(
    () => new Set(groupAxis?.selected ?? []),
    [groupAxis?.selected],
  );

  if (!open) return null;

  const style: React.CSSProperties = pos
    ? { top: pos.top, left: pos.left, width: 580 }
    : // Innan mätning: håll utanför viewport (ingen flimmer-hopp).
      { top: -9999, left: -9999, width: 580 };

  // Ingen groups[0]-fallback (E2f) — null tills användaren klickar vänster.
  const activeGroup =
    groups.find((g) => g.conceptId === activeLeft) ?? null;
  const rightItems = activeGroup?.items ?? [];
  const rightIds = rightItems.map((it) => it.conceptId);
  const activeGroupChecked =
    activeGroup != null && groupSelectedSet.has(activeGroup.conceptId);
  const rightAnySelected =
    rightIds.some((id) => selectedSet.has(id)) || activeGroupChecked;
  // "Välj alla"-radens checked-state: axel-medvetet = gruppens eget id;
  // enaxel = samtliga höger-ids markerade.
  const selectAllChecked = groupAxis
    ? activeGroupChecked
    : rightItems.length > 0 && rightIds.every((id) => selectedSet.has(id));
  // Tri-state (E2d-Minor): partiellt val = något valt men inte allt → "mixed".
  const selectAllMixed = !selectAllChecked && rightAnySelected;
  const anySelectedAnywhere =
    selected.length > 0 || (groupAxis?.selected.length ?? 0) > 0;

  return (
    <div
      ref={ref}
      className="jp-popover"
      role="dialog"
      aria-label={dialogLabel ?? leftTitle}
      style={style}
    >
      <div className="jp-popover__body">
        {/* maxHeight/overflowY på själva kolumnen (ej enbart grid-
            förälderns max-height) — grid-barn får ingen användbar höjd att
            scrolla inom från förälderns max-height; constraint måste sitta
            på scroll-elementet självt (design-reviewer F4 Blocker x2). */}
        {/* Vänsterkolumnen NAVIGERAR aktiv grupp (avslöjar dess val till
            höger) — den väljer inget värde (det gör höger-kolumnens
            checkbox-rader). Därför en knapp-grupp (role="group" + <button>),
            inte role="listbox" (en listbox lovar single-tab-stop + roving
            tabindex + piltangenter, vilket interaktionen aldrig hade). Native
            <button> ger Enter/Space + fokus gratis; aktiv rad via aria-pressed.
            Paritet med ort-kaskaden (CTO-verdikt 2026-06-22). */}
        <div
          className="jp-popover__col"
          role="group"
          aria-label={leftTitle}
          style={{ maxHeight: "60vh", overflowY: "auto" }}
        >
          <div className="jp-popover__colhead">
            <span className="jp-popover__title">{leftTitle}</span>
            {anySelectedAnywhere && (
              <button
                type="button"
                className="jp-clearlink"
                onClick={onClearAll}
              >
                {t("popover.clear")}
              </button>
            )}
          </div>
          {groups.length === 0 ? (
            <div
              style={{
                padding: "12px 16px",
                color: "var(--jp-ink-2)",
                fontSize: 14,
              }}
            >
              {emptyText}
            </div>
          ) : (
            groups.map((g) => {
              const active = activeGroup?.conceptId === g.conceptId;
              const hasSel =
                g.items.some((it) => selectedSet.has(it.conceptId)) ||
                groupSelectedSet.has(g.conceptId);
              return (
                <button
                  key={g.conceptId}
                  type="button"
                  className="jp-popover-row"
                  aria-pressed={active}
                  onClick={() => setActiveLeft(g.conceptId)}
                >
                  <span
                    style={{
                      display: "flex",
                      alignItems: "center",
                      gap: 8,
                    }}
                  >
                    {hasSel && !active && (
                      <span
                        aria-hidden="true"
                        style={{
                          width: 8,
                          height: 8,
                          borderRadius: 999,
                          background: "var(--jp-leaf-600)",
                        }}
                      />
                    )}
                    {g.label}
                  </span>
                  <ChevronRight
                    size={14}
                    className="jp-popover-row__chev"
                    aria-hidden="true"
                  />
                </button>
              );
            })
          )}
        </div>

        <div
          className="jp-popover__col"
          style={{ maxHeight: "60vh", overflowY: "auto" }}
        >
          <div className="jp-popover__colhead">
            <span className="jp-popover__title">{rightTitle}</span>
            {rightAnySelected && activeGroup && (
              <button
                type="button"
                className="jp-clearlink"
                onClick={() => {
                  if (groupAxis) {
                    groupAxis.onClearColumn(activeGroup.conceptId);
                  } else {
                    onChange(
                      selected.filter((v) => !rightIds.includes(v)),
                    );
                  }
                }}
              >
                {t("popover.clear")}
              </button>
            )}
          </div>
          {rightItems.length === 0 ? (
            <div
              style={{
                padding: "12px 16px",
                color: "var(--jp-ink-2)",
                fontSize: 14,
              }}
            >
              {rightEmptyText}
            </div>
          ) : (
            <>
              <CheckRow
                label={activeGroup ? selectAllLabel(activeGroup) : ""}
                checked={selectAllChecked}
                indeterminate={selectAllMixed}
                isAll
                // "Hela länet"-radens count = gruppens eget id i grupp-
                // facetten (region). Enaxel-fallet (Yrke) har ingen
                // grupp-count — summan vore semantiskt fel (CTO VAL 2-not).
                count={
                  groupAxis && activeGroup && groupCounts
                    ? (groupCounts[activeGroup.conceptId] ?? 0)
                    : undefined
                }
                onToggle={() => {
                  if (groupAxis && activeGroup) {
                    groupAxis.onToggleGroup(activeGroup.conceptId);
                  } else {
                    toggleAll(
                      selected,
                      rightIds,
                      selectAllChecked,
                      onChange,
                    );
                  }
                }}
              />
              {rightItems.map((it) => (
                <CheckRow
                  key={it.conceptId}
                  label={it.label}
                  // E2f: när hela länet är valt RENDERAS alla kommun-rader
                  // markerade (Platsbanken-paritet — tydligt vad valet
                  // omfattar); klick på en sådan rad = "hela länet minus
                  // denna" (förälderns semantik via onToggleItem).
                  checked={
                    selectedSet.has(it.conceptId) ||
                    (groupAxis !== undefined && activeGroupChecked)
                  }
                  // Saknad nyckel = 0 träffar (counts laddade); null/undefined
                  // counts = inget tal alls (tyst degradering).
                  count={counts ? (counts[it.conceptId] ?? 0) : undefined}
                  onToggle={() => {
                    if (groupAxis && activeGroup) {
                      groupAxis.onToggleItem(
                        it.conceptId,
                        activeGroup.conceptId,
                      );
                    } else {
                      toggle(selected, it.conceptId, onChange);
                    }
                  }}
                />
              ))}
            </>
          )}
        </div>
      </div>
      {footer && <div className="jp-popover__foot">{footer}</div>}
    </div>
  );
}

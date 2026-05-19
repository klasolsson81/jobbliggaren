"use client";

import { useEffect, useMemo, useState } from "react";
import { Check, ChevronRight } from "lucide-react";
import { useDismissable } from "@/lib/hooks/use-dismissable";

/**
 * Platsbanken-mönster filter-popover (HANDOVER-v3.md §5.4, ADR 0055).
 * STRUKTUR-referens: src-v3/jobb.jsx `FilterPopover` — pixel-nära, men
 * window-globals/mock ersatta med conceptId↔label-kontraktet (ADR 0043 ACL)
 * och live searchParams-commit (ADR 0042 Beslut B, OFÖRÄNDRAT).
 *
 * Två renderingslägen:
 * - Tvåkolumns (Yrke): vänster = grupper (occupationFields) som rader,
 *   höger = valt grupps val (occupations) med "Välj alla"-rad överst.
 * - Enkelkolumns (Ort): EN kolumn = Län-val (ADR 0055 amendment
 *   2026-05-19 — regions enkelnivå, ingen kommun; INGEN platshållare för
 *   framtida höger-kolumn).
 *
 * Ingen footer, ingen Använd/Stäng-knapp (ADR 0055 Beslut 2). ESC + klick
 * utanför stänger, fokus återförs till triggern (jobbpilot-design-a11y,
 * delat `useDismissable`-idiom — DRY, CLAUDE.md §9.1).
 */

export interface PopoverGroup {
  /** conceptId för yrkesområdet (vänsterrad) — endast tvåkolumns. */
  conceptId: string;
  label: string;
  /** Val under gruppen (occupations). */
  items: ReadonlyArray<PopoverItem>;
}

export interface PopoverItem {
  /** conceptId som emitteras till URL (ADR 0042 Beslut B). */
  conceptId: string;
  label: string;
}

interface BaseProps {
  open: boolean;
  /** conceptId-lista för denna axel (ssyk eller region). */
  selected: ReadonlyArray<string>;
  /** Live-commit: emitterar hela nästa conceptId-listan. */
  onChange: (next: string[]) => void;
  onClose: () => void;
  /** Återställ hela denna axel (header-Rensa). */
  onClearAll: () => void;
  /** Triggerns ref — fokus-retur vid ESC/utanför-klick (a11y). */
  triggerRef: React.RefObject<HTMLButtonElement | null>;
}

interface TwoColumnProps extends BaseProps {
  mode: "two-column";
  /** Vänster kolumn-titel (t.ex. "Yrkesområde"). */
  leftTitle: string;
  /** Höger kolumn-titel (t.ex. "Yrken"). */
  rightTitle: string;
  /** "Välj alla X"-radens text (höger kolumn). */
  selectAllLabel: string;
  groups: ReadonlyArray<PopoverGroup>;
}

interface SingleColumnProps extends BaseProps {
  mode: "single-column";
  /** Kolumn-titel (t.ex. "Län"). */
  title: string;
  selectAllLabel: string;
  items: ReadonlyArray<PopoverItem>;
}

type JobbFilterPopoverProps = TwoColumnProps | SingleColumnProps;

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

/** "Välj alla"/"Avmarkera alla" för en grupp av conceptId. */
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
}: {
  label: string;
  checked: boolean;
  onToggle: () => void;
  isAll?: boolean;
}) {
  return (
    <div
      className={isAll ? "jp-checkitem jp-checkitem--all" : "jp-checkitem"}
      role="checkbox"
      aria-checked={checked}
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
    </div>
  );
}

export function JobbFilterPopover(props: JobbFilterPopoverProps) {
  const { open, selected, onChange, onClose, onClearAll, triggerRef } = props;

  const ref = useDismissable<HTMLDivElement>(open, onClose, triggerRef);
  const pos = usePopoverPosition(open, triggerRef);

  // Aktivt yrkesområde (vänsterrad) — endast tvåkolumns. Lazy-initieras
  // till första gruppen. Reset till första gruppen vid varje öppning sker
  // via `key`-remount i föräldern (JobbHeroFilters) — INTE setState i en
  // effect (react-hooks/set-state-in-effect). Pixel-/beteende-paritet med
  // src-v3 FilterPopover (som re-initierar activeLeft vid open) behålls.
  const groups = props.mode === "two-column" ? props.groups : [];
  const [activeLeft, setActiveLeft] = useState<string | null>(
    () => groups[0]?.conceptId ?? null,
  );

  const selectedSet = useMemo(() => new Set(selected), [selected]);

  if (!open) return null;

  const style: React.CSSProperties = pos
    ? { top: pos.top, left: pos.left, width: 580 }
    : // Innan mätning: håll utanför viewport (ingen flimmer-hopp).
      { top: -9999, left: -9999, width: 580 };

  if (props.mode === "single-column") {
    const ids = props.items.map((it) => it.conceptId);
    const anySelected = ids.some((id) => selectedSet.has(id));
    const allSelected =
      props.items.length > 0 && ids.every((id) => selectedSet.has(id));

    return (
      <div
        ref={ref}
        className="jp-popover"
        role="dialog"
        aria-label={props.title}
        style={style}
      >
        <div style={{ maxHeight: "60vh", overflow: "auto", padding: "6px 0" }}>
          <div className="jp-popover__colhead">
            <span className="jp-popover__title">{props.title}</span>
            {anySelected && (
              <button
                type="button"
                className="jp-popover__clear"
                onClick={onClearAll}
              >
                Rensa
              </button>
            )}
          </div>
          {props.items.length === 0 ? (
            <div
              style={{
                padding: "12px 16px",
                color: "var(--jp-ink-2)",
                fontSize: 14,
              }}
            >
              Län kunde inte laddas just nu. Du kan söka på sökord ändå.
            </div>
          ) : (
            <>
              <CheckRow
                label={props.selectAllLabel}
                checked={allSelected}
                isAll
                onToggle={() =>
                  toggleAll(selected, ids, allSelected, onChange)
                }
              />
              {props.items.map((it) => (
                <CheckRow
                  key={it.conceptId}
                  label={it.label}
                  checked={selectedSet.has(it.conceptId)}
                  onToggle={() => toggle(selected, it.conceptId, onChange)}
                />
              ))}
            </>
          )}
        </div>
      </div>
    );
  }

  // ── Tvåkolumns (Yrke) ──────────────────────────────
  const activeGroup =
    groups.find((g) => g.conceptId === activeLeft) ?? groups[0] ?? null;
  const rightItems = activeGroup?.items ?? [];
  const rightIds = rightItems.map((it) => it.conceptId);
  const rightAnySelected = rightIds.some((id) => selectedSet.has(id));
  const rightAllSelected =
    rightItems.length > 0 && rightIds.every((id) => selectedSet.has(id));
  const anySelectedAnywhere = selected.length > 0;

  return (
    <div
      ref={ref}
      className="jp-popover"
      role="dialog"
      aria-label={props.leftTitle}
      style={style}
    >
      <div className="jp-popover__body">
        <div className="jp-popover__col" role="listbox" aria-label={props.leftTitle}>
          <div className="jp-popover__colhead">
            <span className="jp-popover__title">{props.leftTitle}</span>
            {anySelectedAnywhere && (
              <button
                type="button"
                className="jp-popover__clear"
                onClick={onClearAll}
              >
                Rensa
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
              Yrkesområden kunde inte laddas just nu. Du kan söka på sökord
              ändå.
            </div>
          ) : (
            groups.map((g) => {
              const active = activeGroup?.conceptId === g.conceptId;
              const hasSel = g.items.some((it) =>
                selectedSet.has(it.conceptId),
              );
              return (
                <div
                  key={g.conceptId}
                  className="jp-popover-row"
                  role="option"
                  aria-selected={active}
                  tabIndex={0}
                  onClick={() => setActiveLeft(g.conceptId)}
                  onKeyDown={(e) => {
                    if (e.key === "Enter" || e.key === " ") {
                      e.preventDefault();
                      setActiveLeft(g.conceptId);
                    }
                  }}
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
                </div>
              );
            })
          )}
        </div>

        <div className="jp-popover__col">
          <div className="jp-popover__colhead">
            <span className="jp-popover__title">{props.rightTitle}</span>
            {rightAnySelected && (
              <button
                type="button"
                className="jp-popover__clear"
                onClick={() =>
                  onChange(
                    selected.filter((v) => !rightIds.includes(v)),
                  )
                }
              >
                Rensa
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
              Välj ett yrkesområde till vänster.
            </div>
          ) : (
            <>
              <CheckRow
                label={props.selectAllLabel}
                checked={rightAllSelected}
                isAll
                onToggle={() =>
                  toggleAll(
                    selected,
                    rightIds,
                    rightAllSelected,
                    onChange,
                  )
                }
              />
              {rightItems.map((it) => (
                <CheckRow
                  key={it.conceptId}
                  label={it.label}
                  checked={selectedSet.has(it.conceptId)}
                  onToggle={() => toggle(selected, it.conceptId, onChange)}
                />
              ))}
            </>
          )}
        </div>
      </div>
    </div>
  );
}

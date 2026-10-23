"use client";

// "use client": which Word row is open is local state, and opening one moves focus.

import { useId, useRef, useState } from "react";
import { ChevronRight } from "lucide-react";
import { useTranslations } from "next-intl";
import { CheckItem } from "@/components/settings/section-helpers";
import { useFocusAfterCommit } from "@/lib/hooks/use-focus-after-commit";
import type { OccupationSearchState } from "@/lib/hooks/use-occupation-search";
import { revealColumn } from "./reveal-column";
import type {
  OccupationSearchGroupRow,
  OccupationSearchResult,
  OccupationSearchRow,
  OccupationSearchVariant,
  OccupationSearchWordRow,
} from "@/lib/dto/occupation-search";

type Translate = ReturnType<typeof useTranslations<"components.occupationSearch">>;

/**
 * What the picker's one box holds for a search state: the host's browse view, or this body —
 * empty while the current word is pending, rows once it has answered. No hits, an unavailable
 * search and a missing session all give the box back to browsing; the field's status line says why.
 */
export type OccupationSearchBox =
  | { readonly kind: "browse" }
  | { readonly kind: "pending" }
  | { readonly kind: "rows"; readonly result: OccupationSearchResult };

export function occupationSearchBox(state: OccupationSearchState): OccupationSearchBox {
  switch (state.kind) {
    case "pending":
      return { kind: "pending" };
    case "ok":
      return state.result.rows.length > 0 ? { kind: "rows", result: state.result } : { kind: "browse" };
    case "idle":
    case "unauthorized":
    case "unavailable":
      return { kind: "browse" };
  }
}

export interface OccupationSearchResultsProps {
  /** The answer to show, or `null` while the current word is pending. */
  readonly result: OccupationSearchResult | null;
  /** Selected group ids. Every row's checked state is read from here, so two rows that select the
   *  same group cannot disagree. */
  readonly selected: ReadonlyArray<string>;
  /** Toggles one group id: the same call a group row in the browse view makes. */
  readonly onToggle: (groupConceptId: string) => void;
}

// How many matched names a group row spells out before "och N till".
const MAX_EVIDENCE_NAMES = 3;

// Many occupation names carry a comma of their own ("Projektledare, IT"), so a comma between names
// would merge them. The semicolon is the list separator Swedish and English both use when the items
// contain commas.
const EVIDENCE_NAME_SEPARATOR = "; ";

/**
 * The search answer (#2198, ADR 0180), placed by the host in the box its browse view occupies.
 *
 * - **Group row** — a checkbox for one occupation group, with the names that reached it as its
 *   description. A group reached only by its own label has no second line.
 * - **Word row** — one head word whose names span several groups. It is a pressed/unpressed
 *   button, never a checkbox: choosing it would choose groups the user has not seen (ADR 0040
 *   Beslut 4). Opening it shows its variants in the right column, each naming the group it selects,
 *   and moves focus to that column's heading so a 50-row list does not stand between them.
 *
 * Row 1 opens by itself when it is a Word row that holds the exact word or is the only row; opening
 * selects nothing. Any other Word row waits to be chosen. There is no "Välj alla" here: it would make
 * a Word row checkable in practice. Labels render as text, with no match highlighting.
 */
export function OccupationSearchResults({ result, selected, onToggle }: OccupationSearchResultsProps) {
  if (result === null) {
    // Pending: the box keeps its place and height, and shows nothing from an earlier word.
    return <div className="jp-matchdialog__cascade jp-occsearch__box jp-occsearch__box--single" />;
  }
  return <Answer result={result} selected={selected} onToggle={onToggle} />;
}

interface Opened {
  readonly result: OccupationSearchResult;
  readonly headWord: string;
}

function Answer({
  result,
  selected,
  onToggle,
}: {
  readonly result: OccupationSearchResult;
  readonly selected: ReadonlyArray<string>;
  readonly onToggle: (groupConceptId: string) => void;
}) {
  const t = useTranslations("components.occupationSearch");
  const id = useId();
  const hitsHeadingId = `${id}-hits`;
  const rightId = `${id}-variants`;
  const rightHeadingId = `${id}-variants-heading`;
  const rightColumnRef = useRef<HTMLDivElement | null>(null);
  const headingRef = useRef<HTMLParagraphElement | null>(null);
  const focusAfterCommit = useFocusAfterCommit();

  // The open row belongs to the answer it was opened in: a new answer starts from its own default,
  // derived here rather than reset by an effect.
  const [opened, setOpened] = useState<Opened | null>(null);
  const openHeadWord =
    opened !== null && opened.result === result ? opened.headWord : defaultOpenHeadWord(result.rows);

  const rows = result.rows;
  const hasWordRows = rows.some((row) => row.kind === "Word");
  const openRow = rows.find(
    (row): row is OccupationSearchWordRow => row.kind === "Word" && row.headWord === openHeadWord,
  );
  const notShown = result.totalRows - rows.length;

  function open(headWord: string) {
    focusAfterCommit(() => revealColumn(rightColumnRef.current, headingRef.current));
    setOpened({ result, headWord });
  }

  return (
    <div
      className={
        hasWordRows
          ? "jp-matchdialog__cascade jp-occsearch__box"
          : "jp-matchdialog__cascade jp-occsearch__box jp-occsearch__box--single"
      }
    >
      <div className="jp-matchdialog__cascade-col" role="group" aria-labelledby={hitsHeadingId}>
        <div className="jp-matchdialog__cascade-colhead">
          <span id={hitsHeadingId} className="jp-popover__title">
            {t("hitsHeading")}
          </span>
        </div>
        {rows.map((row) =>
          row.kind === "Group" ? (
            <CheckItem
              key={`group:${row.groupConceptId}`}
              label={row.groupLabel}
              secondary={groupEvidence(t, row)}
              checked={selected.includes(row.groupConceptId)}
              onToggle={() => onToggle(row.groupConceptId)}
            />
          ) : (
            <WordRowButton
              key={`word:${row.headWord}`}
              row={row}
              open={row.headWord === openHeadWord}
              controls={rightId}
              hasSelection={row.variants.some((v) => selected.includes(v.groupConceptId))}
              groupsLabel={t("wordGroups", { count: distinctGroupCount(row.variants) })}
              onOpen={() => open(row.headWord)}
            />
          ),
        )}
        {notShown > 0 && (
          <p className="text-body-sm text-text-primary px-4 py-3">
            {t("notShown", { count: notShown })}
          </p>
        )}
      </div>

      {hasWordRows &&
        (openRow === undefined ? (
          <div
            id={rightId}
            className="jp-matchdialog__cascade-col"
            role="group"
            aria-labelledby={rightHeadingId}
          >
            <div className="jp-matchdialog__cascade-colhead">
              <span id={rightHeadingId} className="jp-popover__title">
                {t("groupsHeading")}
              </span>
            </div>
            <p className="text-body-sm text-text-primary px-4 py-3">{t("chooseWord")}</p>
          </div>
        ) : (
          <div
            id={rightId}
            ref={rightColumnRef}
            className="jp-matchdialog__cascade-col"
            role="group"
            aria-labelledby={rightHeadingId}
          >
            <div className="jp-matchdialog__cascade-colhead">
              <p
                id={rightHeadingId}
                ref={headingRef}
                tabIndex={-1}
                className="jp-popover__title jp-occsearch__heading"
              >
                {openRow.headWord}
              </p>
            </div>
            {openRow.variants.map((variant) => (
              <CheckItem
                key={variant.nameLabel}
                label={variant.nameLabel}
                secondary={t("variantGroup", { group: variant.groupLabel })}
                checked={selected.includes(variant.groupConceptId)}
                onToggle={() => onToggle(variant.groupConceptId)}
              />
            ))}
          </div>
        ))}
    </div>
  );
}

function WordRowButton({
  row,
  open,
  controls,
  hasSelection,
  groupsLabel,
  onOpen,
}: {
  readonly row: OccupationSearchWordRow;
  readonly open: boolean;
  readonly controls: string;
  readonly hasSelection: boolean;
  readonly groupsLabel: string;
  readonly onOpen: () => void;
}) {
  const id = useId();
  const nameId = `${id}-name`;
  const secondaryId = `${id}-secondary`;
  return (
    <button
      type="button"
      className="jp-popover-row"
      aria-pressed={open}
      aria-controls={controls}
      aria-labelledby={nameId}
      aria-describedby={secondaryId}
      onClick={onOpen}
    >
      <span className="flex min-w-0 items-center gap-2">
        {hasSelection && (
          <span
            aria-hidden="true"
            className="inline-block size-2 shrink-0 rounded-full bg-(--jp-accent-700)"
          />
        )}
        <span className="flex min-w-0 flex-col">
          <span id={nameId}>{row.headWord}</span>
          <span id={secondaryId} className="text-body-sm text-text-secondary">
            {groupsLabel}
          </span>
        </span>
      </span>
      <ChevronRight size={14} className="jp-popover-row__chev shrink-0" aria-hidden="true" />
    </button>
  );
}

/** Row 1, when it is a Word row holding the exact word or standing alone; otherwise none. */
function defaultOpenHeadWord(rows: ReadonlyArray<OccupationSearchRow>): string | null {
  const first = rows[0];
  if (first?.kind === "Word" && (first.exactMatch || rows.length === 1)) return first.headWord;
  return null;
}

function distinctGroupCount(variants: ReadonlyArray<OccupationSearchVariant>): number {
  return new Set(variants.map((v) => v.groupConceptId)).size;
}

/** "träff på A; B; C och 4 till", in the server's order; nothing for a label-only hit. */
function groupEvidence(t: Translate, row: OccupationSearchGroupRow): string | undefined {
  if (row.matchedNames.length === 0) return undefined;
  const names = row.matchedNames.slice(0, MAX_EVIDENCE_NAMES).join(EVIDENCE_NAME_SEPARATOR);
  const more = row.matchedNames.length - MAX_EVIDENCE_NAMES;
  return more > 0 ? t("groupEvidenceMore", { names, more }) : t("groupEvidence", { names });
}

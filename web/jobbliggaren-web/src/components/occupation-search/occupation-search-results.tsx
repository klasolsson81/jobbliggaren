"use client";

// "use client": which row is open is local state, and opening one moves focus.

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

/** Whether an answer holds an occupation-name a /jobb tick could select: a variant or a Group row's evidence. */
export function answerHoldsNames(result: OccupationSearchResult): boolean {
  return result.rows.some((row) => row.kind === "Word" || row.matchedNames.length > 0);
}

/**
 * The /jobb mapping (#2199, ADR 0180's amendments): a name is ticked by its own occupation-name id,
 * whichever row carries it. Matchning and the onboarding rail pass none, and their variants select
 * their group (ADR 0040 Beslut 4).
 */
export interface OccupationNameSelection {
  /** Selected occupation-name ids: the URL's `occupationName` axis. */
  readonly selected: ReadonlyArray<string>;
  readonly onToggle: (occupationNameConceptId: string) => void;
  /** Counts by occupation-name id (`FacetDimension.OccupationName`); `null` shows no numbers. */
  readonly counts: Readonly<Record<string, number>> | null;
  /** The right column's hint under an open Word row. */
  readonly wordHint: string;
  /** The right column's hint under an open Group row. */
  readonly groupHint: string;
}

export interface OccupationSearchResultsProps {
  /** The answer to show, or `null` while the current word is pending. */
  readonly result: OccupationSearchResult | null;
  /** Selected group ids. */
  readonly selected: ReadonlyArray<string>;
  /** Toggles one group id: the same call a group row in the browse view makes. */
  readonly onToggle: (groupConceptId: string) => void;
  /** Facet counts by group id, shown as "(n)" on Group rows only; a missing key is 0. `null` or
   *  omitted shows no numbers. */
  readonly counts?: Readonly<Record<string, number>> | null;
  /** The /jobb mapping. Omitted, every row renders exactly as it did before #2199. */
  readonly nameSelection?: OccupationNameSelection;
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
 *
 * With `nameSelection` (/jobb, #2199) a Group row whose hit names occupations also gets a sibling
 * button after its checkbox that opens those names in the right column, as a Word row opens its
 * variants; the right column's band reads "Yrken" and what is open is named under it. Every name is
 * ticked by its own id, and one whose group is selected is included rather than separately selected.
 */
export function OccupationSearchResults({
  result,
  selected,
  onToggle,
  counts,
  nameSelection,
}: OccupationSearchResultsProps) {
  if (result === null) {
    // Pending: the box keeps its place and height, and shows nothing from an earlier word.
    return <div className="jp-matchdialog__cascade jp-occsearch__box jp-occsearch__box--single" />;
  }
  return (
    <Answer
      result={result}
      selected={selected}
      onToggle={onToggle}
      counts={counts}
      names={nameSelection}
    />
  );
}

interface Opened {
  readonly result: OccupationSearchResult;
  readonly key: string;
}

// One name in the right column, whichever row carried it.
interface NameEntry {
  readonly id: string;
  readonly label: string;
  readonly groupConceptId: string;
  readonly groupLabel: string;
}

function Answer({
  result,
  selected,
  onToggle,
  counts,
  names,
}: {
  readonly result: OccupationSearchResult;
  readonly selected: ReadonlyArray<string>;
  readonly onToggle: (groupConceptId: string) => void;
  readonly counts: Readonly<Record<string, number>> | null | undefined;
  readonly names: OccupationNameSelection | undefined;
}) {
  const t = useTranslations("components.occupationSearch");
  const id = useId();
  const hitsHeadingId = `${id}-hits`;
  const rightId = `${id}-variants`;
  const rightHeadingId = `${id}-variants-heading`;
  const rightHintId = `${id}-variants-hint`;
  const rightColumnRef = useRef<HTMLDivElement | null>(null);
  const headingRef = useRef<HTMLParagraphElement | null>(null);
  const focusAfterCommit = useFocusAfterCommit();

  const rows = result.rows;
  const canOpen = (row: OccupationSearchRow) =>
    row.kind === "Word" || (names !== undefined && row.matchedNames.length > 0);

  // The open row belongs to the answer it was opened in: a new answer starts from its own default,
  // derived here rather than reset by an effect.
  const [opened, setOpened] = useState<Opened | null>(null);
  const openKey =
    opened !== null && opened.result === result ? opened.key : defaultOpenKey(rows, canOpen);

  const twoColumns = rows.some(canOpen);
  const openRow = rows.find((row) => canOpen(row) && rowKey(row) === openKey);
  const notShown = result.totalRows - rows.length;

  function open(row: OccupationSearchRow) {
    focusAfterCommit(() => revealColumn(rightColumnRef.current, headingRef.current));
    setOpened({ result, key: rowKey(row) });
  }

  return (
    <div
      className={
        twoColumns
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
        {rows.map((row) => {
          if (row.kind === "Word") {
            return (
              <WordRowButton
                key={rowKey(row)}
                row={row}
                open={rowKey(row) === openKey}
                controls={rightId}
                hasSelection={row.variants.some((v) =>
                  names === undefined
                    ? selected.includes(v.groupConceptId)
                    : selected.includes(v.groupConceptId) || names.selected.includes(v.occupationNameConceptId),
                )}
                groupsLabel={t("wordGroups", { count: distinctGroupCount(row.variants) })}
                onOpen={() => open(row)}
              />
            );
          }
          const groupCheck = (
            <CheckItem
              key={rowKey(row)}
              label={row.groupLabel}
              secondary={groupEvidence(t, row)}
              count={counts ? (counts[row.groupConceptId] ?? 0) : undefined}
              checked={selected.includes(row.groupConceptId)}
              onToggle={() => onToggle(row.groupConceptId)}
            />
          );
          if (names === undefined || row.matchedNames.length === 0) return groupCheck;
          return (
            <div key={rowKey(row)} className="jp-occsearch__grouprow">
              {groupCheck}
              <GroupNamesOpener
                count={row.matchedNames.length}
                groupLabel={row.groupLabel}
                open={rowKey(row) === openKey}
                controls={rightId}
                hasSelection={row.matchedNames.some((n) => names.selected.includes(n.occupationNameConceptId))}
                onOpen={() => open(row)}
              />
            </div>
          );
        })}
        {notShown > 0 && (
          <p className="text-body-sm text-text-primary px-4 py-3">
            {t("notShown", { count: notShown })}
          </p>
        )}
      </div>

      {twoColumns &&
        (names === undefined ? (
          <GroupModeColumn
            t={t}
            openRow={openRow?.kind === "Word" ? openRow : undefined}
            selected={selected}
            onToggle={onToggle}
            rightId={rightId}
            rightHeadingId={rightHeadingId}
            rightColumnRef={rightColumnRef}
            headingRef={headingRef}
          />
        ) : openRow === undefined ? (
          <div
            id={rightId}
            className="jp-matchdialog__cascade-col"
            role="group"
            aria-labelledby={rightHeadingId}
          >
            <div className="jp-matchdialog__cascade-colhead">
              <span id={rightHeadingId} className="jp-popover__title">
                {t("namesHeading")}
              </span>
            </div>
            <p className="text-body-sm text-text-primary px-4 py-3">{t("namesIdle")}</p>
          </div>
        ) : (
          <div
            id={rightId}
            ref={rightColumnRef}
            className="jp-matchdialog__cascade-col"
            role="group"
            aria-labelledby={rightHeadingId}
            aria-describedby={rightHintId}
          >
            <div className="jp-matchdialog__cascade-colhead">
              <span className="jp-popover__title">{t("namesHeading")}</span>
            </div>
            <p id={rightHeadingId} ref={headingRef} tabIndex={-1} className="jp-popover__grouphead">
              {openRow.kind === "Word" ? openRow.headWord : openRow.groupLabel}
            </p>
            <p id={rightHintId} className="text-body-sm text-text-primary px-4 py-2">
              {openRow.kind === "Word" ? names.wordHint : names.groupHint}
            </p>
            {namesOf(openRow).map((name) => (
              <NameRow
                key={name.id}
                t={t}
                name={name}
                included={selected.includes(name.groupConceptId)}
                names={names}
              />
            ))}
          </div>
        ))}
    </div>
  );
}

// The right column as Matchning and the onboarding rail have it: a Word row's variants, each
// selecting its group.
function GroupModeColumn({
  t,
  openRow,
  selected,
  onToggle,
  rightId,
  rightHeadingId,
  rightColumnRef,
  headingRef,
}: {
  readonly t: Translate;
  readonly openRow: OccupationSearchWordRow | undefined;
  readonly selected: ReadonlyArray<string>;
  readonly onToggle: (groupConceptId: string) => void;
  readonly rightId: string;
  readonly rightHeadingId: string;
  readonly rightColumnRef: React.RefObject<HTMLDivElement | null>;
  readonly headingRef: React.RefObject<HTMLParagraphElement | null>;
}) {
  if (openRow === undefined) {
    return (
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
    );
  }
  return (
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
  );
}

// A name on /jobb: ticked by its own id, included (checked, inert, the reason as its second line)
// when its group is selected, with its own exact count or none (ADR 0120).
function NameRow({
  t,
  name,
  included,
  names,
}: {
  readonly t: Translate;
  readonly name: NameEntry;
  readonly included: boolean;
  readonly names: OccupationNameSelection;
}) {
  const count = names.counts ? (names.counts[name.id] ?? 0) : undefined;
  const toggle = () => names.onToggle(name.id);
  return included ? (
    <CheckItem
      label={name.label}
      secondary={t("variantIncluded", { group: name.groupLabel })}
      included
      checked
      count={count}
      onToggle={toggle}
    />
  ) : (
    <CheckItem
      label={name.label}
      checked={names.selected.includes(name.id)}
      count={count}
      onToggle={toggle}
    />
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
        {hasSelection && <SelectionDot />}
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

// The sibling after a Group row's checkbox (/jobb only): no control inside another. Its name starts
// with its visible text (WCAG 2.5.3).
function GroupNamesOpener({
  count,
  groupLabel,
  open,
  controls,
  hasSelection,
  onOpen,
}: {
  readonly count: number;
  readonly groupLabel: string;
  readonly open: boolean;
  readonly controls: string;
  readonly hasSelection: boolean;
  readonly onOpen: () => void;
}) {
  const t = useTranslations("components.occupationSearch");
  return (
    <button
      type="button"
      className="jp-occsearch__opener"
      aria-pressed={open}
      aria-controls={controls}
      aria-label={t("groupNamesLabel", { count, group: groupLabel })}
      onClick={onOpen}
    >
      {hasSelection && <SelectionDot />}
      <span>{t("groupNames", { count })}</span>
      <ChevronRight size={14} className="jp-popover-row__chev shrink-0" aria-hidden="true" />
    </button>
  );
}

function SelectionDot() {
  return (
    <span
      aria-hidden="true"
      className="inline-block size-2 shrink-0 rounded-full bg-(--jp-accent-700)"
    />
  );
}

function rowKey(row: OccupationSearchRow): string {
  return row.kind === "Word" ? `word:${row.headWord}` : `group:${row.groupConceptId}`;
}

/** Row 1, when it can open and holds the exact word or stands alone; otherwise none. */
function defaultOpenKey(
  rows: ReadonlyArray<OccupationSearchRow>,
  canOpen: (row: OccupationSearchRow) => boolean,
): string | null {
  const first = rows[0];
  if (first !== undefined && canOpen(first) && (first.exactMatch || rows.length === 1)) return rowKey(first);
  return null;
}

function namesOf(row: OccupationSearchRow): ReadonlyArray<NameEntry> {
  return row.kind === "Word"
    ? row.variants.map((v) => ({
        id: v.occupationNameConceptId,
        label: v.nameLabel,
        groupConceptId: v.groupConceptId,
        groupLabel: v.groupLabel,
      }))
    : row.matchedNames.map((n) => ({
        id: n.occupationNameConceptId,
        label: n.nameLabel,
        groupConceptId: row.groupConceptId,
        groupLabel: row.groupLabel,
      }));
}

function distinctGroupCount(variants: ReadonlyArray<OccupationSearchVariant>): number {
  return new Set(variants.map((v) => v.groupConceptId)).size;
}

/** "träff på A; B; C och 4 till", in the server's order; nothing for a label-only hit. */
function groupEvidence(t: Translate, row: OccupationSearchGroupRow): string | undefined {
  if (row.matchedNames.length === 0) return undefined;
  const names = row.matchedNames
    .slice(0, MAX_EVIDENCE_NAMES)
    .map((n) => n.nameLabel)
    .join(EVIDENCE_NAME_SEPARATOR);
  const more = row.matchedNames.length - MAX_EVIDENCE_NAMES;
  return more > 0 ? t("groupEvidenceMore", { names, more }) : t("groupEvidence", { names });
}

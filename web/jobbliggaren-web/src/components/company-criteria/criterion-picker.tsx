"use client";

// "use client": holds a label-filter string and delegates selection to the parent's draft. One axis
// of the criterion dialog (SNI branches or kommuner), composed from the generic CriterionTree.

import { useId, useMemo, useState } from "react";
import { useTranslations } from "next-intl";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { CriterionTree, CheckBox } from "./criterion-tree";
import { groupTriState } from "@/lib/company-criteria/criterion-selection";
import type {
  CriterionOption,
  CriterionTreeNode,
} from "@/lib/company-criteria/criterion-options";

/**
 * Above this many matches the filter list is NOT rendered — the tree is, with the count and a stated
 * reason. The ceiling is on RESULT CARDINALITY, never on query length, and it is never silent.
 *
 * The control this replaced gated on length (`MIN_QUERY = 2`) and its own docblock said why: "a 1-char
 * query would match hundreds of the ~900 options". Measured over all 944 names, that number does not
 * do that job — it blocks `c` (507 matches) while admitting `er` (665), and ten characters
 * (`verksamhet`, 152) narrow better than two. Length does not predict result size, which is why the
 * guard read as arbitrary.
 *
 * 300 sits in a measured gap: it excludes every non-selective query (`er` 665, `in` 649, `ni` 516,
 * `an` 405, `ve` 397) and admits every query that has actually narrowed something (`st` 275,
 * `ha` 181, `tr` 167, `verksamhet` 162, `dat` 25, `sys` 4). Above roughly a third of the catalogue the
 * filter has selected nothing, and the tree is the better rendering of "most of the catalogue".
 *
 * Counts of 3+ characters now span two match surfaces (name and alias, #1115) and were re-measured
 * on 2026-09-05; the 1-2 character counts are name-only and unchanged, since aliases do not answer
 * below ALIAS_MIN_QUERY.
 */
const MAX_FILTER_MATCHES = 300;

/**
 * Aliases (#1115) answer queries of this length and up; names still match at ONE character.
 *
 * **This is not a MIN_QUERY revival.** The constant above rejects length as a proxy for cardinality,
 * and rightly: `MIN_QUERY = 2` suppressed the whole filter — exact name matches included — on a
 * cardinality guess. Nothing here gates the filter. `c` still returns 507 rows and every 1-2
 * character count in the docblock above is unchanged.
 *
 * It bounds ONE match surface to its own domain: an alias resolves a WORD, and a 1-2 character
 * fragment is not one. Measured, that costs zero coverage — every term on the demand list is 4+
 * characters, and all 17 probed gap words resolve with the bound as without it. Without it `st`
 * went 275 → 318 and crossed the ceiling above, which is the regression this prevents.
 *
 * MAX_FILTER_MATCHES remains the only cardinality guard.
 */
const ALIAS_MIN_QUERY = 3;

/**
 * The part of an alias worth showing: the comma- or parenthesis-delimited segment that contains the
 * query.
 *
 * SCB writes its entries as whole classified sentences — "Datakonsultverksamhet, (IT-konsult,
 * ITkonsult, ADB-konsult), systemdesign" — and the everyday synonym the user actually typed is
 * usually one clause inside, often inside the parentheses. Rendering the whole term and clipping it
 * showed the OPENING of the sentence, which is the one part that never had to contain the query:
 * measured over the shipped asset, 279 of 335 terms overflowed and `it-konsult` produced 8 rows of
 * which 0 displayed the typed word, one of them tautologically reading
 * "Datakonsultverksamhet · matchar Datakonsultverksamhet, (…".
 *
 * The segment contains the query BY CONSTRUCTION, so the row can always answer "why am I here".
 * Measured: 750 of 1 017 segments are 27 characters or fewer, median 15 ("IT-konsult" 10,
 * "undersköterska" 14, "Agil systemutveckling" 21). Truncation stays as the last resort for the rest.
 *
 * The stored term is untouched — this is a rendering choice, and the asset keeps SCB's wording whole.
 */
function matchedSegment(term: string, query: string): string {
  const segments = term
    .split(/[,()]/)
    .map((part) => part.trim())
    .filter(Boolean);
  return segments.find((s) => s.toLocaleLowerCase("sv-SE").includes(query)) ?? term;
}

interface CriterionPickerProps {
  readonly nodes: ReadonlyArray<CriterionTreeNode>;
  /**
   * Every node at EVERY level, flattened, for the filter view — build it with
   * `flattenCriterionOptions(nodes)` so the two views can never describe different catalogues.
   */
  readonly options: ReadonlyArray<CriterionOption>;
  readonly selected: ReadonlySet<string>;
  /** Toggle a group's leaf codes (a tree node, or one filtered option's `leafCodes`). */
  readonly onToggle: (leafCodes: ReadonlyArray<string>) => void;
  /**
   * The four props below are optional so a caller can host the axis's own chrome OUTSIDE the picker.
   * The popover puts its title, selection count and clear control in the panel header, where they have
   * reserved height and cannot shift the tree under the pointer; the criterion dialog stacks two
   * pickers in one scroll column and keeps all four inline. Parameterised by data, not by a mode flag.
   */
  readonly onClear?: () => void;
  readonly heading?: string;
  readonly help?: string;
  readonly selectedCountLabel?: string;
  readonly filterLabel: string;
  /** Omitted where the field's own label already says it (the popover). */
  readonly filterHint?: string;
  readonly groupAria: string;
  /**
   * AXIS copy, supplied by the host like `heading`/`help`/`groupAria` — not
   * component-owned. The tree's chevron is an icon-only button, so it needs an
   * accessible name (its sibling `aria-expanded` cannot supply one, unlike the
   * house's text-wrapping accordion buttons). But WHAT the children are is the
   * axis's knowledge: this picker's two AXES render SNI sections and län ->
   * kommun respectively, so a single component-owned string said "underkategorier"
   * over a län while the same picker's help text said "ett helt län eller
   * enskilda kommuner" (design-reviewer, #1146).
   */
  readonly expandAria: (name: string) => string;
  readonly collapseAria: (name: string) => string;
  /** Axis-specific message when the reference tree is empty (degraded load). */
  readonly optionsUnavailable: string;
}

export function CriterionPicker({
  nodes,
  options,
  selected,
  onToggle,
  onClear,
  heading,
  help,
  selectedCountLabel,
  filterLabel,
  filterHint,
  groupAria,
  expandAria,
  collapseAria,
  optionsUnavailable,
}: CriterionPickerProps) {
  // The component's OWN strings, not the page's. Three surfaces render this
  // picker (`/foretag/sok`'s bransch popover and both pickers in the criterion
  // dialog), so reading them out of `pages.foretag.criteria` made a shared
  // component depend on one page's namespace: a second page reusing it either
  // inherits copy written for `/foretag`, or duplicates it.
  const t = useTranslations("components.criterionPicker");
  const filterId = useId();
  const filterHelpId = useId();
  const [filter, setFilter] = useState("");

  const trimmed = filter.trim().toLocaleLowerCase("sv-SE");
  const isFiltering = trimmed.length > 0;

  // Matches at EVERY level (#999): a section, a division and a leaf can all carry the searched word,
  // and the control this replaced searched all three. Leaf-only matching is why "hard to find" survived
  // the last two rounds — you had to already know the detail code's exact wording.
  //
  // A row matches on its NAME, or (#1115) on one of its ALIASES — the everyday words SNI, which
  // classifies activities, has no word for. `matchedAlias` is undefined when the name matched, and
  // that is what the row renders on: a row is annotated only when it appeared for a reason its own
  // visible text does not already show.
  const filteredOptions = useMemo(() => {
    if (!isFiltering) return [];
    const out: Array<{ option: CriterionOption; matchedAlias?: string }> = [];
    for (const option of options) {
      if (option.name.toLocaleLowerCase("sv-SE").includes(trimmed)) {
        out.push({ option });
        continue;
      }
      if (trimmed.length < ALIAS_MIN_QUERY) continue;
      const hit = option.aliases.find((alias) =>
        alias.toLocaleLowerCase("sv-SE").includes(trimmed),
      );
      if (hit) out.push({ option, matchedAlias: matchedSegment(hit, trimmed) });
    }
    return out;
  }, [options, trimmed, isFiltering]);

  const tooMany = filteredOptions.length > MAX_FILTER_MATCHES;
  const showFilterList = isFiltering && !tooMany && filteredOptions.length > 0;
  const hasSelection = selected.size > 0;
  const showInlineHeader = heading !== undefined || (onClear !== undefined && hasSelection);

  return (
    <section className="flex flex-col gap-2" role="group" aria-label={groupAria}>
      {showInlineHeader && (
        <div className="flex items-center justify-between gap-3">
          {heading !== undefined ? (
            <h3 className="text-label font-medium text-text-primary">{heading}</h3>
          ) : (
            <span />
          )}
          {onClear !== undefined && hasSelection && (
            <button type="button" className="jp-clearlink" onClick={onClear}>
              {t("clear")}
            </button>
          )}
        </div>
      )}
      {help !== undefined && <p className="text-body-sm text-text-primary">{help}</p>}

      <div className="flex flex-col gap-1.5">
        <Label htmlFor={filterId}>{filterLabel}</Label>
        <Input
          id={filterId}
          type="text"
          value={filter}
          onChange={(e) => setFilter(e.target.value)}
          maxLength={80}
          aria-describedby={filterHint !== undefined ? filterHelpId : undefined}
        />
        {filterHint !== undefined && (
          <p id={filterHelpId} className="text-body-sm text-text-primary">
            {filterHint}
          </p>
        )}
      </div>

      {/* ONE persistent live region carries every filtering outcome, including zero matches.
          Deliberately not a region inserted per outcome: a live region that mounts with its content
          already in place is announced unreliably. That is a house RULING, not a measurement of ours
          — design-reviewer, a11y §6, and `jobb-hero-search.tsx` carries the same note. Mutating the
          text inside a region already in the DOM is the reliable form.

          It is the load-bearing half of the ceiling: without it a sighted user cannot see that a
          hundred rows sit below the fold, and a screen-reader user gets no signal that the list
          changed at all while typing.

          `min-h-6` (24px) reserves the slot so the COUNT text appearing and disappearing does not move the
          list. It does not cover the zero case — there the box below is removed entirely and the
          panel collapses by its list height, which is deliberate rather than a shift to absorb.

          Gated on there being a catalogue: with a degraded reference the box below says "Registret
          kunde inte laddas", and announcing "0 träffar" over it would claim a search ran against a
          catalogue that is not there. */}
      <p
        className="min-h-6 text-body-sm tabular-nums text-text-primary"
        role="status"
        aria-live="polite"
      >
        {isFiltering && nodes.length > 0
          ? filteredOptions.length === 0
            ? t("noMatch")
            : tooMany
              ? t("filterTooMany", { count: filteredOptions.length })
              : t("filterMatches", { count: filteredOptions.length })
          : ""}
      </p>

      {selectedCountLabel !== undefined && (
        // Polite: the number changes without focus moving — one click on a section takes it from
        // nothing to a whole subtree, and the row's own `aria-checked` does not carry the total.
        //
        // Rendered UNCONDITIONALLY and emptied rather than unmounted, for the same reason as the
        // region above: gating on `hasSelection` made the 0→1 transition — the FIRST pick, the one
        // that matters most — a region that mounts with its content already in place. `min-h-6`
        // (24px) covers the 21.7px line box body-sm inherits at line-height 1.55, so the empty state
        // reserves the full row rather than most of it.
        <p
          className="min-h-6 text-body-sm font-medium text-text-primary"
          aria-live="polite"
        >
          {hasSelection ? selectedCountLabel : ""}
        </p>
      )}

      {/* The list cap is viewport-relative so the panel it sits in stays a SINGLE scroller. Measured at
          1280×720: a flat `max-h-72` made the panel taller than `.jp-panel__body`'s 60vh cap, so both
          engaged and scrolling the list chained into scrolling the panel. At 900px height only one
          ever did, which is why the first round missed it. 32vh keeps the whole panel inside 60vh on a
          short screen and resolves to the same 18rem on a tall one. */}
      {/* No box at all when the filter matched nothing: the message above says so and says what to do,
          and an empty bordered rectangle under it would be a container for nothing. This is also why
          the zero case is not a second `role="status"` — one region, one message. */}
      {(!isFiltering || nodes.length === 0 || filteredOptions.length > 0) && (
        <div className="max-h-[min(18rem,32vh)] overflow-y-auto rounded-md border border-border">
          {nodes.length === 0 ? (
            <p className="px-4 py-3 text-body-sm text-text-primary">{optionsUnavailable}</p>
          ) : showFilterList ? (
            // No nested `role="group"` here: the <section> above already carries `groupAria`, and two
            // nested groups with the same label make AT announce the axis name three times over.
            filteredOptions.map(({ option, matchedAlias }) => {
              // Tri-state, not a boolean: a matched division is "mixed" when only some of its leaves
              // are selected, and rendering that as unchecked would let a click silently deselect the
              // part already chosen. `groupTriState` is the same derivation the tree rows use.
              const state = groupTriState(selected, option.leafCodes);
              return (
                <div
                  key={option.key}
                  role="checkbox"
                  aria-checked={state === "indeterminate" ? "mixed" : state === "checked"}
                  // Name from author, so the row announces the same string in every environment. Letting
                  // the name be computed from the two child spans depends on whose separator rule you
                  // get: MEASURED in Chromium's own AX tree, the space is inserted by the browser
                  // whether or not the JSX contains one, while jsdom concatenates without it and reports
                  // "68Fastighetsverksamhet". An explicit `{" "}` would therefore be a text node that
                  // exists only to satisfy the test environment — and a whitespace-only node directly in
                  // a flex container is not rendered anyway (CSS Flexbox L1 §4). The visible text is
                  // exactly this string, so WCAG 2.5.3 holds — and that is now a coupling to keep in
                  // mind: the name no longer tracks the JSX, so anything visible added to this row has
                  // to be added here too, or the label stops containing the visible text. The alias
                  // (#1115) is exactly such an addition, so it is appended here when it is rendered.
                  // The comma is for prosody: without it a screen reader runs the name and the
                  // annotation together into one sentence. The segment, not the whole term, keeps
                  // the label scannable by ear.
                  aria-label={
                    matchedAlias
                      ? `${option.code} ${option.name}, ${t("matchedVia", { term: matchedAlias })}`
                      : `${option.code} ${option.name}`
                  }
                  tabIndex={0}
                  onClick={() => onToggle(option.leafCodes)}
                  onKeyDown={(e) => {
                    if (e.key === " " || e.key === "Enter") {
                      e.preventDefault();
                      onToggle(option.leafCodes);
                    }
                  }}
                  // Indentation is a SECONDARY cue only. In a filtered list the ancestors are not
                  // rendered, so equal indent on two rows can suggest a sibling relationship that does
                  // not exist — and padding reaches no screen reader at all (WCAG 1.3.1). The CODE
                  // carries the level in text: its length says which level it is (`A` / `62` / `62100`),
                  // it lands in the row's accessible name, and two codes side by side settle whether the
                  // rows are related. SNI 2025 has "Dataprogrammering" at two levels; its codes differ.
                  style={{ paddingInlineStart: 12 + option.depth * 20 }}
                  className="jp-criterionrow flex cursor-pointer flex-wrap items-center gap-x-2.5 gap-y-0.5 border-b border-border py-2 pe-3 text-body-sm text-text-primary last:border-b-0 sm:flex-nowrap"
                >
                  <CheckBox state={state} />
                  <span className="jp-mono shrink-0 text-caption tabular-nums text-text-secondary">
                    {option.code}
                  </span>
                  {/* `min-w-0` so the flex row may shrink it, but NOT `truncate`: the name is the
                      primary content, and an unclipped name is already this component's own form — the tree
                      view renders full names on rows measuring 59 px. Clipping it made `reparation` cut
                      7 of 25 names and `partihandel` 4 of 56, on rows carrying no annotation at
                      all, which is a regression on a surface this delta only passes through. */}
                  <span className="min-w-0 break-words">{option.name}</span>
                  {/* Why this row is here at all. Without it a row appears containing none of the
                      typed characters — a result with no visible reason, which AGENTS.md §5 rules
                      out for match surfaces ("matched/missing keywords are always surfaced"). It
                      also keeps the alias honest: the SNI concept stays the row, and the alias is
                      shown as the search word that led to it, never as a claim about the concept.

                      It renders the matched SEGMENT (see matchedSegment above), so it contains the
                      typed word by construction. Truncation is the last resort for the segments
                      that are still long: 113 of the 335 shipped terms exceed 90 characters and the
                      longest is 240, so some clipping is unavoidable — but it now clips a clause
                      that already showed the answer, not the opening of a sentence that never did.

                      Two arms, both measured. From `sm` up it sits at the end of the row and does
                      NOT shrink: letting it give way clipped 9 of 10 rows on `undersköterska` at
                      1280, and holding it firm makes the name wrap instead — 0 clipped, 0 overflow.
                      Below `sm` there is no width to share, so it wraps onto its OWN line rather
                      than being squeezed: keeping it inline at 390 left `träff …` visible and
                      nothing else, which is the same "row with no visible reason" the annotation
                      exists to prevent, and forcing it inline with `shrink-0` blew rows to 255 px.

                      `break-words` on the name is what makes the narrow arm safe: without it the
                      longest word painted 24-30 px INTO this box at 390, which the old `truncate`
                      had been hiding rather than preventing. */}
                  {matchedAlias && (
                    <span className="w-full min-w-0 truncate text-caption text-text-secondary sm:ms-auto sm:w-auto sm:max-w-[45%] sm:shrink-0 sm:ps-2">
                      {t("matchedVia", { term: matchedAlias })}
                    </span>
                  )}
                </div>
              );
            })
          ) : (
            <CriterionTree
              nodes={nodes}
              selected={selected}
              onToggle={onToggle}
              groupAriaLabel={groupAria}
              expandAria={expandAria}
              collapseAria={collapseAria}
            />
          )}
        </div>
      )}
    </section>
  );
}

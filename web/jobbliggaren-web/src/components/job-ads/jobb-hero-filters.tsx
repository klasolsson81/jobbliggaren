"use client";

import {
  useMemo,
  useOptimistic,
  useRef,
  useState,
  useTransition,
} from "react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { ChevronDown } from "lucide-react";
import { ToggleRow } from "@/components/ui/toggle-row";
import type { JobAdSortBy } from "@/lib/dto/job-ads";
import type { TaxonomyTree } from "@/lib/dto/taxonomy";
import { buildJobbHref } from "@/lib/job-ads/search-params";
import { JobbKlass2Panel } from "./jobb-klass2-panel";
import {
  applyMunicipalityChange,
  toggleMunicipalityInRegion,
  toggleWholeRegion,
  clearRegionColumn,
  type OrtSelection,
} from "@/lib/job-ads/ort-selection";
import { useFacetCounts } from "@/lib/hooks/use-facet-counts";
import { useTotalCount } from "@/lib/job-ads/total-count-store";
import {
  JobbFilterPopover,
  type PopoverGroup,
} from "./jobb-filter-popover";
import { JobbToolbarPopover } from "./jobb-toolbar-popover";
import { JobbMatchGradeFilter } from "./jobb-match-grade-filter";

/**
 * The filter pills under the hero search, with Platsbanken-style popovers (ADR 0055,
 * ADR 0067): place (region, then municipality, in two columns), occupation (field,
 * then occupation group), a "Filter" panel for employment type and working hours,
 * matching, and "Dölj ansökta".
 *
 * Place is one dimension at two granularities: the "whole region" row toggles one
 * region id (`?region=`), never the region's municipality ids (which keeps the URL
 * short and gives one chip), and a municipality row toggles `?municipality=`. The
 * backend combines them, together with remote work. The per-region normalisation in
 * `lib/job-ads/ort-selection.ts` only keeps the URL minimal; correctness does not
 * depend on it.
 *
 * Every change navigates at once with `router.push` in a transition, and every other
 * parameter is carried through `buildJobbHref`, so a click never drops a filter.
 */

interface JobbHeroFiltersProps {
  taxonomy: TaxonomyTree | null;
  initialOccupationGroup: ReadonlyArray<string>;
  initialRegion: ReadonlyArray<string>;
  initialMunicipality: ReadonlyArray<string>;
  /**
   * `?distans=on`: the place dimension's third granularity. The backend combines
   * municipality, region and remote, so it widens the place choice.
   */
  initialRemote: boolean;
  // Employment type (several) and working hours (one), shown in the "Filter" panel.
  initialEmploymentType: ReadonlyArray<string>;
  initialWorktimeExtent: ReadonlyArray<string>;
  /** The active grade filter, edited in the matching popover. */
  initialMatchGrades: ReadonlyArray<string>;
  /**
   * View toggles: `matchningOff` (`?matchning=off`), `includeRelated`
   * (`?relaterade=on`) and `hideApplied` (`?doljAnsokta=on`).
   */
  initialMatchningOff: boolean;
  initialIncludeRelated: boolean;
  initialHideApplied: boolean;
  /** `?baraMatchade=on`: only ads with a positive grade. Its checkbox is in the matching popover. */
  initialOnlyMatched: boolean;
  /**
   * True once the user has stated at least one occupation. Without one no grade can
   * be computed, so the matching pill is hidden.
   */
  hasStatedDesiredOccupation: boolean;
  /**
   * True when the user has a job-seeker profile. Gates "Dölj ansökta", which needs
   * applications to hide but not a stated occupation.
   */
  hasSeeker: boolean;
  /** The search text, carried so a filter click keeps it. */
  q: string;
  /** The employer filter; here it is carried along. */
  employer: ReadonlyArray<string>;
  sortBy: JobAdSortBy;
  pageSize?: string;
}

type OpenPop = "ort" | "yrke" | "filter" | "match" | null;

// Everything the pills can change. The base is the props (the URL); during a
// router.push transition an optimistic overlay shows the new value at once.
interface FilterSelection {
  occupationGroup: string[];
  region: string[];
  municipality: string[];
  remote: boolean;
  employmentType: string[];
  worktimeExtent: string[];
  matchGrades: string[];
  matchningOff: boolean;
  includeRelated: boolean;
  hideApplied: boolean;
  onlyMatched: boolean;
}

export function JobbHeroFilters({
  taxonomy,
  initialOccupationGroup,
  initialRegion,
  initialMunicipality,
  initialRemote,
  initialEmploymentType,
  initialWorktimeExtent,
  initialMatchGrades,
  initialMatchningOff,
  initialIncludeRelated,
  initialHideApplied,
  initialOnlyMatched,
  hasStatedDesiredOccupation,
  hasSeeker,
  q,
  employer,
  sortBy,
  pageSize,
}: JobbHeroFiltersProps) {
  const router = useRouter();
  const t = useTranslations("jobads.ui");
  // Separate namespaces, since next-intl types `t()` against each namespace's keys.
  const tGrade = useTranslations("jobads.ui.gradeFilter");
  const tStatus = useTranslations("jobads.ui.statusFilter");
  const [, startTransition] = useTransition();

  // The URL, through props, is the only source of truth for the selected filters.
  // This component sits outside the Suspense boundary and is never remounted, so
  // local useState copies would miss external URL changes (a chip removed in the
  // toolbar, "clear all", a recent search). useOptimistic answers a click at once and
  // falls back to the fresh props when the navigation lands.
  const base = useMemo<FilterSelection>(
    () => ({
      occupationGroup: [...initialOccupationGroup],
      region: [...initialRegion],
      municipality: [...initialMunicipality],
      remote: initialRemote,
      employmentType: [...initialEmploymentType],
      worktimeExtent: [...initialWorktimeExtent],
      matchGrades: [...initialMatchGrades],
      matchningOff: initialMatchningOff,
      includeRelated: initialIncludeRelated,
      hideApplied: initialHideApplied,
      onlyMatched: initialOnlyMatched,
    }),
    [
      initialOccupationGroup,
      initialRegion,
      initialMunicipality,
      initialRemote,
      initialEmploymentType,
      initialWorktimeExtent,
      initialMatchGrades,
      initialMatchningOff,
      initialIncludeRelated,
      initialHideApplied,
      initialOnlyMatched,
    ],
  );
  const [selection, setOptimisticSelection] = useOptimistic(
    base,
    (_current, next: FilterSelection) => next,
  );
  const occupationGroup = selection.occupationGroup;
  const ort: OrtSelection = selection;

  const [openPop, setOpenPop] = useState<OpenPop>(null);

  const ortBtnRef = useRef<HTMLButtonElement>(null);
  const yrkeBtnRef = useRef<HTMLButtonElement>(null);
  const filterBtnRef = useRef<HTMLButtonElement>(null);
  const matchBtnRef = useRef<HTMLButtonElement>(null);

  // The taxonomy in popover form: regions with their municipalities, and occupation
  // fields with their SSYK level-4 occupation groups.
  const regionGroups: PopoverGroup[] = (taxonomy?.regions ?? []).map((r) => ({
    conceptId: r.conceptId,
    label: r.label,
    items: r.municipalities.map((m) => ({
      conceptId: m.conceptId,
      label: m.label,
    })),
  }));
  const occupationFieldGroups: PopoverGroup[] = (
    taxonomy?.occupationFields ?? []
  ).map((f) => ({
    conceptId: f.conceptId,
    label: f.label,
    items: f.occupationGroups.map((g) => ({
      conceptId: g.conceptId,
      label: g.label,
    })),
  }));

  // Lookups for the per-region normalisation in ort-selection.ts.
  const regionOfMunicipality = useMemo(() => {
    const map = new Map<string, string>();
    for (const r of taxonomy?.regions ?? [])
      for (const m of r.municipalities) map.set(m.conceptId, r.conceptId);
    return map;
  }, [taxonomy]);
  const municipalityIdsOfRegion = useMemo(() => {
    const map = new Map<string, string[]>();
    for (const r of taxonomy?.regions ?? [])
      map.set(
        r.conceptId,
        r.municipalities.map((m) => m.conceptId),
      );
    return map;
  }, [taxonomy]);

  // The optimistic overlay and the navigation share one transition: React discards
  // an optimistic update made outside a transition. Filter changes navigate without
  // the commit flag, so they are never saved as recent searches; only a deliberate
  // search and toolbar actions are. The whole state is carried into the URL.
  function commit(next: FilterSelection) {
    startTransition(() => {
      setOptimisticSelection(next);
      router.push(
        buildJobbHref({
          q,
          occupationGroup: next.occupationGroup,
          region: next.region,
          municipality: next.municipality,
          remote: next.remote,
          employmentType: next.employmentType,
          worktimeExtent: next.worktimeExtent,
          matchGrades: next.matchGrades,
          matchningOff: next.matchningOff,
          includeRelated: next.includeRelated,
          hideApplied: next.hideApplied,
          onlyMatched: next.onlyMatched,
          employer,
          sortBy,
          pageSize,
        }),
      );
    });
  }

  function changeOccupationGroup(next: string[]) {
    commit({ ...selection, occupationGroup: next });
  }
  function commitOrt(next: OrtSelection) {
    commit({
      ...selection,
      region: [...next.region],
      municipality: [...next.municipality],
    });
  }
  // Remote work has no region-to-municipality hierarchy to normalise, so it bypasses
  // ort-selection.ts.
  function toggleRemote() {
    commit({ ...selection, remote: !selection.remote });
  }
  function changeEmploymentType(next: string[]) {
    commit({ ...selection, employmentType: next });
  }
  function changeWorktimeExtent(next: string[]) {
    commit({ ...selection, worktimeExtent: next });
  }
  // Required by the popover's onChange contract. With two axes, item clicks go
  // through toggleMunicipality below, so this path is not reached today.
  function changeMunicipality(nextMunicipality: string[]) {
    commitOrt(
      applyMunicipalityChange(ort, nextMunicipality, regionOfMunicipality),
    );
  }
  // Platsbanken's semantics: removing one municipality from a whole region selects
  // the region's other municipalities, and selecting the last missing one collapses
  // back to the region id. This component owns it because it needs both axes.
  function toggleMunicipality(
    municipalityConceptId: string,
    regionConceptId: string,
  ) {
    commitOrt(
      toggleMunicipalityInRegion(
        ort,
        municipalityConceptId,
        regionConceptId,
        municipalityIdsOfRegion.get(regionConceptId) ?? [],
      ),
    );
  }
  function toggleRegion(regionConceptId: string) {
    commitOrt(
      toggleWholeRegion(
        ort,
        regionConceptId,
        municipalityIdsOfRegion.get(regionConceptId) ?? [],
      ),
    );
  }
  function clearOrtColumn(regionConceptId: string) {
    commitOrt(
      clearRegionColumn(
        ort,
        regionConceptId,
        municipalityIdsOfRegion.get(regionConceptId) ?? [],
      ),
    );
  }

  // Matching is active exactly when an occupation is stated and matching is not
  // switched off, as JobbResults derives it. An empty grade list means every grade
  // is shown; only matchningOff turns matching off. Turning it off also forgets the
  // grade choice and the toggles that depend on matching.
  const matchActive = hasStatedDesiredOccupation && !selection.matchningOff;
  const matchActiveCount = matchActive ? selection.matchGrades.length : 0;

  function onMatchGradesChange(nextGrades: string[]) {
    commit({ ...selection, matchGrades: nextGrades, matchningOff: false });
  }
  function onMatchTurnOff() {
    commit({
      ...selection,
      matchningOff: true,
      matchGrades: [],
      includeRelated: false,
      // Its checkbox is hidden while matching is off, so the flag must not survive.
      onlyMatched: false,
    });
  }
  function onMatchTurnOn() {
    commit({ ...selection, matchningOff: false, matchGrades: [] });
  }
  // Turning related occupations off removes `Related` from the chosen grades, since
  // its checkbox is then hidden.
  function onRelatedToggle(next: boolean) {
    commit({
      ...selection,
      includeRelated: next,
      matchGrades: next
        ? selection.matchGrades
        : selection.matchGrades.filter((g) => g !== "Related"),
    });
  }
  // Independent of matching; gated on hasSeeker.
  function toggleHideApplied() {
    commit({ ...selection, hideApplied: !selection.hideApplied });
  }
  // Unchecking "Visa bara matchade" means "show everything", ungraded ads included.
  // A chosen subset of grades implies only matched ads, so it is cleared too;
  // otherwise the checkbox would be derived as checked again and the click would do
  // nothing. Checking it keeps any chosen subset.
  function onOnlyMatchedToggle(next: boolean) {
    commit({
      ...selection,
      onlyMatched: next,
      matchGrades: next ? selection.matchGrades : [],
    });
  }

  // Remote work counts towards the place pill. It has no chip of its own, so without
  // this the filter would be visible nowhere while the popover is closed.
  const ortCount =
    ort.region.length + ort.municipality.length + (selection.remote ? 1 : 0);
  const filterCount =
    selection.employmentType.length + selection.worktimeExtent.length;

  // Per-option counts are fetched, debounced, only while their popover or panel is
  // open (ADR 0067 Beslut 4). The place popover needs two dimensions, municipality
  // rows and whole-region rows. The backend leaves out the dimension being counted
  // (for place, the whole place dimension).
  // Remote work matters for the occupation and panel counts: without it, a search
  // with only remote work selected would count the whole corpus.
  const facetFilter = {
    occupationGroup,
    municipality: ort.municipality,
    region: ort.region,
    employmentType: selection.employmentType,
    worktimeExtent: selection.worktimeExtent,
    remote: selection.remote,
    q,
  };
  const municipalityCounts = useFacetCounts(
    "Municipality",
    facetFilter,
    openPop === "ort",
  );
  const regionCounts = useFacetCounts("Region", facetFilter, openPop === "ort");
  const occupationGroupCounts = useFacetCounts(
    "OccupationGroup",
    facetFilter,
    openPop === "yrke",
  );
  const employmentTypeCounts = useFacetCounts(
    "EmploymentType",
    facetFilter,
    openPop === "filter",
  );
  const worktimeExtentCounts = useFacetCounts(
    "WorktimeExtent",
    facetFilter,
    openPop === "filter",
  );

  // The close button's N is the list's own total, which the toolbar publishes: no
  // extra request, and never a sum of facet counts. Before the first list response
  // it reads "Visa annonser". The catalogue handles singular and plural.
  const totalCount = useTotalCount();
  const showResultsLabel =
    totalCount !== null
      ? t("heroFilters.showResults", {
          // count drives both the plural form and the locale-aware number
          // (ICU {count, number}) in the catalog — no pre-formatting here.
          count: totalCount,
        })
      : t("heroFilters.showResultsEmpty");
  const showResultsFooter = (
    <button
      type="button"
      className="jp-btn jp-btn--primary jp-btn--sm"
      onClick={() => setOpenPop(null)}
    >
      {showResultsLabel}
    </button>
  );

  return (
    <div className="jp-hero__pills">
      <button
        ref={ortBtnRef}
        type="button"
        className="jp-hero-pill"
        data-active={openPop === "ort" || ortCount > 0}
        aria-haspopup="dialog"
        aria-expanded={openPop === "ort"}
        onClick={() => setOpenPop(openPop === "ort" ? null : "ort")}
      >
        {ortCount > 0 && (
          <span className="jp-hero-pill__dot" aria-hidden="true" />
        )}
        {t("heroFilters.ort")}
        {ortCount > 0 && (
          <span className="jp-hero-pill__count">{ortCount}</span>
        )}
        <ChevronDown size={14} aria-hidden="true" />
      </button>

      <button
        ref={yrkeBtnRef}
        type="button"
        className="jp-hero-pill"
        data-active={openPop === "yrke" || occupationGroup.length > 0}
        aria-haspopup="dialog"
        aria-expanded={openPop === "yrke"}
        onClick={() => setOpenPop(openPop === "yrke" ? null : "yrke")}
      >
        {occupationGroup.length > 0 && (
          <span className="jp-hero-pill__dot" aria-hidden="true" />
        )}
        {t("heroFilters.yrke")}
        {occupationGroup.length > 0 && (
          <span className="jp-hero-pill__count">{occupationGroup.length}</span>
        )}
        <ChevronDown size={14} aria-hidden="true" />
      </button>

      {/* "Filter" because the pill holds two dimensions, employment type and working
          hours; either name alone would mislead. */}
      <button
        ref={filterBtnRef}
        type="button"
        className="jp-hero-pill"
        data-active={openPop === "filter" || filterCount > 0}
        aria-haspopup="dialog"
        aria-expanded={openPop === "filter"}
        onClick={() => setOpenPop(openPop === "filter" ? null : "filter")}
      >
        {filterCount > 0 && (
          <span className="jp-hero-pill__dot" aria-hidden="true" />
        )}
        {t("heroFilters.filter")}
        {filterCount > 0 && (
          <span className="jp-hero-pill__count">{filterCount}</span>
        )}
        <ChevronDown size={14} aria-hidden="true" />
      </button>

      {/* Rendered whenever an occupation is stated, so the switch inside can turn
          matching back on. The count is the number of chosen grades; none means every
          grade is shown. Help sits beside each control inside the popover. */}
      {hasStatedDesiredOccupation && (
        <button
          ref={matchBtnRef}
          type="button"
          className="jp-hero-pill"
          data-active={openPop === "match" || matchActive}
          aria-haspopup="dialog"
          aria-expanded={openPop === "match"}
          onClick={() => setOpenPop(openPop === "match" ? null : "match")}
        >
          {matchActive && (
            <span className="jp-hero-pill__dot" aria-hidden="true" />
          )}
          {tGrade("toggleLabel")}
          {matchActiveCount > 0 && (
            <span className="jp-hero-pill__count">{matchActiveCount}</span>
          )}
          <ChevronDown size={14} aria-hidden="true" />
        </button>
      )}

      {hasSeeker && (
        <div className="jp-hero-hide-applied">
          <ToggleRow
            label={tStatus("hideApplied")}
            checked={selection.hideApplied}
            onChange={toggleHideApplied}
          />
        </div>
      )}

      {/* The key remounts the popover on opening, so the right column starts empty
          until a region is picked, as on Platsbanken, without a setState in an effect. */}
      <JobbFilterPopover
        key={openPop === "ort" ? "ort-open" : "ort-closed"}
        open={openPop === "ort"}
        leftTitle={t("heroFilters.ortLeftTitle")}
        dialogLabel={t("heroFilters.ort")}
        rightTitle={t("heroFilters.ortRightTitle")}
        selectAllLabel={(g) => t("heroFilters.ortSelectAll", { label: g.label })}
        emptyText={t("heroFilters.ortEmpty")}
        rightEmptyText={t("heroFilters.ortRightEmpty")}
        groups={regionGroups}
        selected={ort.municipality}
        onChange={changeMunicipality}
        groupAxis={{
          selected: ort.region,
          onToggleGroup: toggleRegion,
          onClearColumn: clearOrtColumn,
          onToggleItem: toggleMunicipality,
        }}
        booleanAxis={{
          label: t("heroFilters.ortDistans"),
          hint: t("heroFilters.ortDistansHint"),
          checked: selection.remote,
          onToggle: toggleRemote,
        }}
        counts={municipalityCounts}
        groupCounts={regionCounts}
        footer={showResultsFooter}
        onClose={() => setOpenPop(null)}
        onClearAll={() =>
          commit({ ...selection, region: [], municipality: [], remote: false })
        }
        triggerRef={ortBtnRef}
      />

      <JobbFilterPopover
        key={openPop === "yrke" ? "yrke-open" : "yrke-closed"}
        open={openPop === "yrke"}
        leftTitle={t("heroFilters.yrkeLeftTitle")}
        dialogLabel={t("heroFilters.yrke")}
        rightTitle={t("heroFilters.yrkeRightTitle")}
        selectAllLabel={() => t("heroFilters.yrkeSelectAll")}
        emptyText={t("heroFilters.yrkeEmpty")}
        rightEmptyText={t("heroFilters.yrkeRightEmpty")}
        groups={occupationFieldGroups}
        selected={occupationGroup}
        onChange={changeOccupationGroup}
        counts={occupationGroupCounts}
        footer={showResultsFooter}
        onClose={() => setOpenPop(null)}
        onClearAll={() => changeOccupationGroup([])}
        triggerRef={yrkeBtnRef}
      />

      {/* One column: working hours (radio) and employment type (checkboxes). */}
      <JobbKlass2Panel
        open={openPop === "filter"}
        employmentTypeOptions={taxonomy?.employmentTypes ?? []}
        worktimeExtentOptions={taxonomy?.worktimeExtents ?? []}
        employmentType={selection.employmentType}
        worktimeExtent={selection.worktimeExtent}
        employmentTypeCounts={employmentTypeCounts}
        worktimeExtentCounts={worktimeExtentCounts}
        onEmploymentTypeChange={changeEmploymentType}
        onWorktimeExtentChange={changeWorktimeExtent}
        emptyText={t("heroFilters.klass2Empty")}
        footer={showResultsFooter}
        onClose={() => setOpenPop(null)}
        triggerRef={filterBtnRef}
      />

      {/* The matching switch, the related-occupations toggle, "Visa bara matchade" and
          the grade checkboxes. */}
      {hasStatedDesiredOccupation && (
        <JobbToolbarPopover
          open={openPop === "match"}
          title={tGrade("toggleLabel")}
          triggerRef={matchBtnRef}
          onClose={() => setOpenPop(null)}
        >
          <JobbMatchGradeFilter
            active={matchActive}
            selected={selection.matchGrades}
            includeRelated={selection.includeRelated}
            onChange={onMatchGradesChange}
            onTurnOff={onMatchTurnOff}
            onTurnOn={onMatchTurnOn}
            onRelatedToggle={onRelatedToggle}
            onlyMatched={selection.onlyMatched}
            onOnlyMatchedToggle={onOnlyMatchedToggle}
          />
        </JobbToolbarPopover>
      )}
    </div>
  );
}

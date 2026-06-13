"use client";

import {
  useMemo,
  useOptimistic,
  useRef,
  useState,
  useTransition,
} from "react";
import { useRouter } from "next/navigation";
import { ChevronDown } from "lucide-react";
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

/**
 * Hero-filter-pills + Platsbanken-popovers (HANDOVER-v3.md §5.4/§5.5,
 * ADR 0055 + ADR 0067 Fas E2b). Client-island under hero-sökrutan:
 * `Ort ▾` (tvåkolumns Län→Kommuner, dual-axis per CTO VAL 3) ·
 * `Yrke ▾` (tvåkolumns Yrkesområde→Yrkesgrupper, enaxel).
 *
 * Ort-semantik (CTO VAL 1, docs/reviews/2026-06-11-sok-paritet-e2b-cto.md):
 * "Hela länet"-raden togglar ETT region-conceptId (`?region=`, aldrig
 * materialiserade kommun-ids — 414-skydd + en chip); kommun-rader togglar
 * `?municipality=`. Backend unionerar region∪municipality (Ort = EN
 * dimension i två granulariteter). Per-län-normaliseringen
 * (lib/job-ads/ort-selection.ts) håller URL:en minimal — ren UX-kosmetik,
 * ingen korrekthets-bärare.
 *
 * RSC→client-kontrakt: tar serialiserbara props (taxonomy-träd, valda
 * conceptId string[], q, sortBy, pageSize) från jobb/page.tsx (RSC).
 * Live-commit: varje markering → `router.push` i `useTransition`, övriga
 * params bevaras symmetriskt via `buildJobbHref` (ADR 0042 Beslut B,
 * OFÖRÄNDRAT; samma param-bevarande-disciplin som F3 B-FIX).
 */

interface JobbHeroFiltersProps {
  taxonomy: TaxonomyTree | null;
  initialOccupationGroup: ReadonlyArray<string>;
  initialRegion: ReadonlyArray<string>;
  initialMunicipality: ReadonlyArray<string>;
  // Klass 2 (2026-06-13) — anställningsform (checkbox-multi) + omfattning
  // (radio-single). Driver "Filter"-pillen + Klass-2-panelen.
  initialEmploymentType: ReadonlyArray<string>;
  initialWorktimeExtent: ReadonlyArray<string>;
  /** Hero-sökordet — bärs vidare så filter-klick inte raderar q. */
  q: string;
  sortBy: JobAdSortBy;
  pageSize?: string;
}

type OpenPop = "ort" | "yrke" | "filter" | null;

// Öns filterval-vy (E2g): bas = props (URL-sanningen), optimistiskt
// overlay under pågående router.push-transition.
interface FilterSelection {
  occupationGroup: string[];
  region: string[];
  municipality: string[];
  // Klass 2 — anställningsform + omfattning bärs i samma optimistiska overlay
  // så pill-count + panel-markeringar svarar omedelbart under transitionen.
  employmentType: string[];
  worktimeExtent: string[];
}

export function JobbHeroFilters({
  taxonomy,
  initialOccupationGroup,
  initialRegion,
  initialMunicipality,
  initialEmploymentType,
  initialWorktimeExtent,
  q,
  sortBy,
  pageSize,
}: JobbHeroFiltersProps) {
  const router = useRouter();
  const [, startTransition] = useTransition();

  // E2g (CTO-dom 2026-06-11, Variant A — useOptimistic): URL:en (via props)
  // är ENDA sanningen för valda filter; öns tidigare useState-kopior synkade
  // aldrig vid EXTERNA URL-ändringar (toolbar-chippens ×, "Rensa alla
  // filter", recent-search-navigering) eftersom ön medvetet aldrig remountas
  // (utanför Suspense — F6 P4 B1). useOptimistic ger omedelbar egen-toggle-
  // respons (overlay inuti router.push-transitionen) och faller garanterat
  // tillbaka till färska props när RSC-navigeringen landat.
  const base = useMemo<FilterSelection>(
    () => ({
      occupationGroup: [...initialOccupationGroup],
      region: [...initialRegion],
      municipality: [...initialMunicipality],
      employmentType: [...initialEmploymentType],
      worktimeExtent: [...initialWorktimeExtent],
    }),
    [
      initialOccupationGroup,
      initialRegion,
      initialMunicipality,
      initialEmploymentType,
      initialWorktimeExtent,
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

  // Taxonomi → popover-form. Län→Kommuner (E2b-kaskad) + Yrkesområde→
  // Yrkesgrupper (ssyk-level-4, E2a nivå-skifte).
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

  // Lookups för per-län-normaliseringen (ort-selection.ts).
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

  // Optimistiskt overlay + navigering i SAMMA transition (CTO-krav 1 —
  // setOptimisticSelection utanför en transition kastas direkt av React).
  function commit(next: FilterSelection) {
    startTransition(() => {
      setOptimisticSelection(next);
      router.push(
        buildJobbHref({
          q,
          occupationGroup: next.occupationGroup,
          region: next.region,
          municipality: next.municipality,
          employmentType: next.employmentType,
          worktimeExtent: next.worktimeExtent,
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
  // Klass 2 — anställningsform (checkbox-multi) + omfattning (radio-single).
  // Speglar changeOccupationGroup: byt EN axel, bevara resten via spread.
  function changeEmploymentType(next: string[]) {
    commit({ ...selection, employmentType: next });
  }
  function changeWorktimeExtent(next: string[]) {
    commit({ ...selection, worktimeExtent: next });
  }
  // Defensiv list-väg (popoverns onChange-kontrakt) — i dual-axis-läget går
  // item-klick via toggleMunicipality nedan; denna nås aldrig vid runtime
  // men håller kontraktet semantiskt korrekt om popovern någonsin emitterar.
  function changeMunicipality(nextMunicipality: string[]) {
    commitOrt(
      applyMunicipalityChange(ort, nextMunicipality, regionOfMunicipality),
    );
  }
  // E2f — per-kommun-toggle med Platsbanken-semantik ("hela länet minus en
  // kommun" materialiserar länets övriga; komplettering kollapsar tillbaka
  // till region-id:t). Föräldern äger semantiken — den kräver båda axlarna.
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

  const ortCount = ort.region.length + ort.municipality.length;
  // Klass 2 — "Filter"-pillens count = summan av aktiva anställningsform-
  // + omfattning-val (omfattning bär 0–1, anställningsform 0–8).
  const filterCount =
    selection.employmentType.length + selection.worktimeExtent.length;

  // E2c (ADR 0067 Beslut 4, CTO VAL 2 = A) — per-option-counts hämtas
  // debouncat när respektive popover är öppen (enabled-gated, ingen
  // bakgrunds-poll). Ort-popovern behöver två dimensioner (kommun-rader +
  // "Hela länet"-raden); Yrke en. Backend exkluderar den facetterade
  // dimensionen själv (ort-facetterna HELA ort-dimensionen — VAL 4).
  const facetFilter = {
    occupationGroup,
    municipality: ort.municipality,
    region: ort.region,
    // PR-3 — Klass 2 ingår i facett-filtret så Ort/Yrke-facetterna reflekterar
    // anställningsform/omfattning OCH vice versa (backend exkluderar egen dim).
    employmentType: selection.employmentType,
    worktimeExtent: selection.worktimeExtent,
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
  // PR-3 — Klass 2-facetter, gated på "Filter"-panelen öppen.
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

  // "Visa N annonser"-stängknappen (CTO VAL 2): N = list-svarets totalCount
  // som toolbaren publicerar (SPOT — noll extra requests; ALDRIG en summa av
  // facett-counts). null innan första list-svaret → "Visa annonser".
  const totalCount = useTotalCount();
  // Singular-böjning (design-reviewer Major 1 E2c) — samma grammatikregel
  // som träffräknaren ("träff"/"träffar").
  const showResultsLabel =
    totalCount !== null
      ? `Visa ${totalCount.toLocaleString("sv-SE")} ${totalCount === 1 ? "annons" : "annonser"}`
      : "Visa annonser";
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
        Ort
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
        Yrke
        {occupationGroup.length > 0 && (
          <span className="jp-hero-pill__count">{occupationGroup.length}</span>
        )}
        <ChevronDown size={14} aria-hidden="true" />
      </button>

      {/* Klass-2-pillen (ADR 0067 Fas E rad 109 "Filter-panel"). "Filter"
          valt som tydligast civic-label: pillen samlar två dimensioner
          (anställningsform + omfattning) — en enskild dimensions-label hade
          varit missvisande. Speglar Ort/Yrke-pillarnas dot+count-mönster. */}
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
        Filter
        {filterCount > 0 && (
          <span className="jp-hero-pill__count">{filterCount}</span>
        )}
        <ChevronDown size={14} aria-hidden="true" />
      </button>

      {/* key-remount vid öppning → activeLeft re-initieras till TOM (E2f
          Platsbanken-paritet — höger kolumn tom tills län valts) utan
          setState-i-effect. */}
      <JobbFilterPopover
        key={openPop === "ort" ? "ort-open" : "ort-closed"}
        open={openPop === "ort"}
        leftTitle="Län"
        dialogLabel="Ort"
        rightTitle="Kommuner"
        selectAllLabel={(g) => `Hela ${g.label}`}
        emptyText="Län kunde inte laddas just nu. Du kan söka på sökord ändå."
        rightEmptyText="Välj ett län till vänster."
        groups={regionGroups}
        selected={ort.municipality}
        onChange={changeMunicipality}
        groupAxis={{
          selected: ort.region,
          onToggleGroup: toggleRegion,
          onClearColumn: clearOrtColumn,
          onToggleItem: toggleMunicipality,
        }}
        counts={municipalityCounts}
        groupCounts={regionCounts}
        footer={showResultsFooter}
        onClose={() => setOpenPop(null)}
        onClearAll={() => commitOrt({ region: [], municipality: [] })}
        triggerRef={ortBtnRef}
      />

      <JobbFilterPopover
        key={openPop === "yrke" ? "yrke-open" : "yrke-closed"}
        open={openPop === "yrke"}
        leftTitle="Yrkesområde"
        dialogLabel="Yrke"
        rightTitle="Yrkesgrupper"
        selectAllLabel={() => "Välj alla yrkesgrupper"}
        emptyText="Yrkesområden kunde inte laddas just nu. Du kan söka på sökord ändå."
        rightEmptyText="Välj ett yrkesområde till vänster."
        groups={occupationFieldGroups}
        selected={occupationGroup}
        onChange={changeOccupationGroup}
        counts={occupationGroupCounts}
        footer={showResultsFooter}
        onClose={() => setOpenPop(null)}
        onClearAll={() => changeOccupationGroup([])}
        triggerRef={yrkeBtnRef}
      />

      {/* Klass-2-panel (enkelkolumn): Omfattning (radio) + Anställningsform
          (checkbox). Live-commit per val (changeWorktimeExtent/
          changeEmploymentType → router.push i transition, samma mönster som
          popovrarna). Footer = samma "Visa N annonser"-knapp (SPOT). */}
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
        emptyText="Filter kunde inte laddas just nu. Du kan söka på sökord ändå."
        footer={showResultsFooter}
        onClose={() => setOpenPop(null)}
        triggerRef={filterBtnRef}
      />
    </div>
  );
}

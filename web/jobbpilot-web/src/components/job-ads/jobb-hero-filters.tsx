"use client";

import { useMemo, useRef, useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { ChevronDown } from "lucide-react";
import type { JobAdSortBy } from "@/lib/dto/job-ads";
import type { TaxonomyTree } from "@/lib/dto/taxonomy";
import { buildJobbHref } from "@/lib/job-ads/search-params";
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
  /** Hero-sökordet — bärs vidare så filter-klick inte raderar q. */
  q: string;
  sortBy: JobAdSortBy;
  pageSize?: string;
}

type OpenPop = "ort" | "yrke" | null;

export function JobbHeroFilters({
  taxonomy,
  initialOccupationGroup,
  initialRegion,
  initialMunicipality,
  q,
  sortBy,
  pageSize,
}: JobbHeroFiltersProps) {
  const router = useRouter();
  const [, startTransition] = useTransition();

  const [occupationGroup, setOccupationGroup] = useState<string[]>([
    ...initialOccupationGroup,
  ]);
  const [ort, setOrt] = useState<OrtSelection>({
    region: [...initialRegion],
    municipality: [...initialMunicipality],
  });
  const [openPop, setOpenPop] = useState<OpenPop>(null);

  const ortBtnRef = useRef<HTMLButtonElement>(null);
  const yrkeBtnRef = useRef<HTMLButtonElement>(null);

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

  function push(nextOccupationGroup: string[], nextOrt: OrtSelection) {
    startTransition(() => {
      router.push(
        buildJobbHref({
          q,
          occupationGroup: nextOccupationGroup,
          region: nextOrt.region,
          municipality: nextOrt.municipality,
          sortBy,
          pageSize,
        }),
      );
    });
  }

  function changeOccupationGroup(next: string[]) {
    setOccupationGroup(next);
    push(next, ort);
  }
  function commitOrt(next: OrtSelection) {
    setOrt(next);
    push(occupationGroup, next);
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

  // E2c (ADR 0067 Beslut 4, CTO VAL 2 = A) — per-option-counts hämtas
  // debouncat när respektive popover är öppen (enabled-gated, ingen
  // bakgrunds-poll). Ort-popovern behöver två dimensioner (kommun-rader +
  // "Hela länet"-raden); Yrke en. Backend exkluderar den facetterade
  // dimensionen själv (ort-facetterna HELA ort-dimensionen — VAL 4).
  const facetFilter = {
    occupationGroup,
    municipality: ort.municipality,
    region: ort.region,
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

      {/* key-remount vid öppning → activeLeft re-initieras till första
          länet (src-v3 FilterPopover-paritet) utan setState-i-effect. */}
      <JobbFilterPopover
        key={openPop === "ort" ? "ort-open" : "ort-closed"}
        open={openPop === "ort"}
        leftTitle="Län"
        rightTitle="Kommuner"
        selectAllLabel="Hela länet"
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
        rightTitle="Yrkesgrupper"
        selectAllLabel="Välj alla yrkesgrupper"
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
    </div>
  );
}

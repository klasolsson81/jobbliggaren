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
  /**
   * STEG 5 (grade-filter, 2026-06-23) — aktivt matchningsgrad-filter. Ön
   * redigerar ALDRIG grader (de bor i toolbaren), men måste bära dem vidare i
   * varje buildJobbHref-commit så ett filter-pill-klick inte raderar ett aktivt
   * grad-filter (samma param-bevarande-disciplin som q/sort/Klass-2).
   */
  initialMatchGrades: ReadonlyArray<string>;
  /**
   * Matchnings-/status-axeln (2026-06-30 — flyttad hit från resultat-toolbaren
   * så hela filterraden delar EN form på ETT ställe, Klas). Runtime-view-state
   * (navigerar utan commit-flaggan, paritet matchGrades): `matchningOff` =
   * huvudbrytaren av (`?matchning=off`); `includeRelated` = "Visa relaterade
   * också" (`?relaterade=on`); `hideApplied` = "Dölj ansökta" (`?doljAnsokta=on`).
   */
  initialMatchningOff: boolean;
  initialIncludeRelated: boolean;
  initialHideApplied: boolean;
  /**
   * #419 pt1 (CTO Approach A) — "Visa bara matchade" (`?baraMatchade=on`). Runtime-
   * view-state (paritet matchGrades/includeRelated): visa ENDAST annonser med positiv
   * matchningsgrad. Kontrollen (kryssrutan) bor i Matchning-popovern.
   */
  initialOnlyMatched: boolean;
  /**
   * F4-16 (CTO D8) — server-härlett: true så snart minst en yrkesgrupp angetts i
   * matchnings-preferenserna. Gatar Matchning-pillen (utan angivet yrke kan
   * graden inte beräknas). Härlett i page.tsx via cache():ad getMyProfile.
   */
  hasStatedDesiredOccupation: boolean;
  /**
   * #383 — true när användaren har en seeker (lyckad profil-läsning). Gatar
   * "Dölj ansökta"-toggle:n (den gallrar mot seekerns ansökta; utan seeker finns
   * inget att dölja). Skild från `hasStatedDesiredOccupation`: status kräver
   * INTE ett angivet yrke.
   */
  hasSeeker: boolean;
  /** Hero-sökordet — bärs vidare så filter-klick inte raderar q. */
  q: string;
  sortBy: JobAdSortBy;
  pageSize?: string;
}

type OpenPop = "ort" | "yrke" | "filter" | "match" | null;

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
  // Matchnings-/status-axeln (2026-06-30 — flyttad hit från toolbaren). I SAMMA
  // optimistiska overlay så Matchning-pillen/popovern + Dölj ansökta-toggle:n
  // svarar omedelbart, och varje facett-commit bär dem vidare (param-bevarande,
  // ADR 0042 Beslut B). Runtime-view-state (navigerar utan commit-flaggan).
  matchGrades: string[];
  matchningOff: boolean;
  includeRelated: boolean;
  hideApplied: boolean;
  // #419 pt1 — "Visa bara matchade" i samma optimistiska overlay (paritet hideApplied)
  // så kryssrutan svarar omedelbart och varje facett-commit bär den vidare.
  onlyMatched: boolean;
}

export function JobbHeroFilters({
  taxonomy,
  initialOccupationGroup,
  initialRegion,
  initialMunicipality,
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
  sortBy,
  pageSize,
}: JobbHeroFiltersProps) {
  const router = useRouter();
  const t = useTranslations("jobads.ui");
  // Skilda namespaces (next-intl typar `t()` mot den literala message-key-unionen).
  const tGrade = useTranslations("jobads.ui.gradeFilter");
  const tStatus = useTranslations("jobads.ui.statusFilter");
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
  // Alla hero-ändringar (facetter OCH matchning/status) navigerar UTAN commit-
  // flaggan — hero:n auto-capturar aldrig till Senaste sökningar (det gör bara
  // explicit Sök/Enter/förslags-val + toolbar-handlingar). matchGrades/matchning/
  // related/hideApplied är dessutom uttryckligen runtime-view-state (#292/#383).
  // Hela `next`-staten bärs ut (param-bevarande symmetri, ADR 0042 Beslut B).
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
          matchGrades: next.matchGrades,
          matchningOff: next.matchningOff,
          includeRelated: next.includeRelated,
          hideApplied: next.hideApplied,
          onlyMatched: next.onlyMatched,
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

  // Matchnings-axeln (flyttad hit från toolbaren 2026-06-30). SSOT-härledning =
  // toolbarens: PÅ exakt när yrke angetts OCH huvudbrytaren inte är av. Handlers
  // speglar toolbaren 1:1 (commit = navigera utan flagga); en tom matchGrades-
  // lista = "alla grader visas" (ej av), av styrs av matchningOff (#292). AV nollar
  // grader + relaterade ("forget"-semantik, CTO-bind).
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
      // #419 pt1 — "forget"-semantik: matchningen av ⇒ "Visa bara matchade" nollas också
      // (kontrollen göms med PÅ-blocket; en kvarvarande flagga utan synlig kryssruta vore
      // state/URL-divergens, paritet matchGrades/includeRelated).
      onlyMatched: false,
    });
  }
  function onMatchTurnOn() {
    commit({ ...selection, matchningOff: false, matchGrades: [] });
  }
  // #300 PR-5 — vid AV droppas `Related` ur den valda grad-listan (kontrollen
  // dold ⇒ inget kvarvarande filter på en grad utan kryssruta).
  function onRelatedToggle(next: boolean) {
    commit({
      ...selection,
      includeRelated: next,
      matchGrades: next
        ? selection.matchGrades
        : selection.matchGrades.filter((g) => g !== "Related"),
    });
  }
  // #383 → förenklat — "Dölj ansökta"-toggle:n (en enda boolean). Ortogonal mot
  // matchningen (gatad på hasSeeker, inte på matchnings-axeln).
  function toggleHideApplied() {
    commit({ ...selection, hideApplied: !selection.hideApplied });
  }
  // #419 pt1 — "Visa bara matchade"-kryssrutan i Matchning-popovern. PÅ ⇒ visa bara
  // annonser med positiv grad (rank > 0). AV ⇒ nolla onlyMatched OCH ev. grad-delmängd:
  // att "kryssa ur bara matchade" betyder "visa allt" (även otaggade), och en grad-delmängd
  // implicerar bara-matchade så den måste rensas med (annars vore kryssrutan derive-checkad
  // direkt igen och klicket ett no-op). PÅ bevarar ev. delmängd (en redan smalnad vy är
  // fortfarande bara-matchade, nu smalare). Navigerar utan commit-flaggan (runtime-view-state).
  function onOnlyMatchedToggle(next: boolean) {
    commit({
      ...selection,
      onlyMatched: next,
      matchGrades: next ? selection.matchGrades : [],
    });
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
        {t("heroFilters.filter")}
        {filterCount > 0 && (
          <span className="jp-hero-pill__count">{filterCount}</span>
        )}
        <ChevronDown size={14} aria-hidden="true" />
      </button>

      {/* [Matchning ▾]-pillen (2026-06-30, Klas: en form, en plats). Samma
          .jp-hero-pill som Ort/Yrke/Filter. Renderas på hasStatedDesiredOccupation
          (så switchen i popovern kan slå PÅ matchningen igen även när den är av).
          data-active = matchActive; prick när PÅ; count-badge = antal smalnade grad-val
          (0 = alla visas, ingen badge). #419 pt7 (Klas) — den tidigare EXTERNA "?" bredvid
          pillen är borttagen; hjälpen bor nu per kontroll INNE i Matchning-popovern
          (JobbMatchGradeFilter), där varje kontroll-rad har sin egen kontextuella "?". */}
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

      {/* [Dölj ansökta] — en enda toggle-pill (#383 → förenklat, Klas: droppat
          "Visa sparade"/"Visa bara ansökta"). Samma pill-form men INGEN chevron
          (öppnar ingen meny) och `aria-pressed` (toggle, inte dialog-trigger).
          Renderas på hasSeeker. data-active + prick visar att den är på. */}
      {hasSeeker && (
        <button
          type="button"
          className="jp-hero-pill"
          data-active={selection.hideApplied}
          aria-pressed={selection.hideApplied}
          onClick={toggleHideApplied}
        >
          {selection.hideApplied && (
            <span className="jp-hero-pill__dot" aria-hidden="true" />
          )}
          {tStatus("hideApplied")}
        </button>
      )}

      {/* key-remount vid öppning → activeLeft re-initieras till TOM (E2f
          Platsbanken-paritet — höger kolumn tom tills län valts) utan
          setState-i-effect. */}
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
        emptyText={t("heroFilters.klass2Empty")}
        footer={showResultsFooter}
        onClose={() => setOpenPop(null)}
        triggerRef={filterBtnRef}
      />

      {/* [Matchning ▾]-popovern (flyttad hit från resultat-toolbaren 2026-06-30).
          Samma enkelkolumns JobbToolbarPopover-skal; JobbMatchGradeFilter-kroppen
          bär switch + relaterad-toggle + "Visa bara matchade"-kryssrutan (#419 pt1) +
          grad-kryssrutor, wired till hero-handlers (commit = navigera utan commit-flaggan,
          #292/#300/#419-semantiken bevarad). */}
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

"use client";

import {
  useEffect,
  useMemo,
  useOptimistic,
  useTransition,
} from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useFormatter, useTranslations } from "next-intl";
import { formatNumber } from "@/lib/i18n/format";
import {
  Briefcase,
  Clock,
  FileText,
  MapPin,
  Search,
  SlidersHorizontal,
  X,
} from "lucide-react";
import type { LucideIcon } from "lucide-react";
import type { JobAdSortBy } from "@/lib/dto/job-ads";
import {
  isListMatchGrade,
  type ListMatchGrade,
} from "@/lib/dto/job-ad-match";
import { jobAdSortLabel } from "@/lib/job-ads/status";
import {
  buildJobbHref,
  DEFAULT_SORT_BY,
  withCommitFlag,
  type JobbUrlState,
} from "@/lib/job-ads/search-params";
import {
  buildChipModels,
  removeChipFromState,
  type SearchChip,
} from "@/lib/job-ads/chip-models";
import { publishTotalCount } from "@/lib/job-ads/total-count-store";

/**
 * Result-toolbar för /jobb (HANDOVER-v3.md §7.2, ADR 0055).
 *
 * 2026-06-30 (Klas: symmetri + EN form + ingen hårdkodning + läsbarhet) —
 * Matchning + Dölj ansökta flyttades UPP i hero-filterraden (bredvid Ort/Yrke/
 * Filter, samma `.jp-hero-pill`). Toolbaren är nu:
 * - ROW 1: `N träffar` vänster; `Sortera [select ▾]` höger — den ENDA kontrollen
 *   kvar nära jobben. Sort-selecten läser de delade kontroll-tokenen
 *   (`--jp-control-h/-fs`) via `.jp-sortfield`; ingen inline-px längre.
 * - ROW 2 (chips): sök/q-chipsen (SPOT via buildChipModels) + toolbar-lokala
 *   grad-chips (vald matchningsgrad) så filter-staten alltid syns. Grad läggs
 *   ALDRIG i buildChipModels (SPOT med hero-fältets in-field-chips) — härleds
 *   lokalt här. Grad-chip-× navigerar via samma buildJobbHref-väg som
 *   hero-popovern (matchnings-kontrollen ägs nu av hero:n; toolbaren behåller
 *   bara chip-spegeln + dess ×). Status-chips borttagna med "Visa sparade"/
 *   "Visa bara ansökta"; "Dölj ansökta" syns på sin egen hero-toggle.
 *
 * E2h: chips deriveras ur props (URL-sanningen) via delade
 * `buildChipModels`/`removeChipFromState`. useOptimistic-overlay = omedelbar
 * ×-respons; URL är enda sanningen.
 *
 * Labels: server-resolverad conceptId→label (ADR 0043 Beslut B, "Okänd
 * kod (<id>)"-fallback). Toolbar-× PUSHAR.
 *
 * Sort: native `<select>`. Relevance disablad när q < 2 tecken
 * (ADR 0042 Beslut D — härledd ur q-searchParam-propen, EJ lokal state).
 */

interface JobbResultsToolbarProps {
  totalCount: number;
  occupationGroup: ReadonlyArray<string>;
  region: ReadonlyArray<string>;
  municipality: ReadonlyArray<string>;
  // Klass 2 (2026-06-13) — anställningsform + omfattning. Renderas som
  // borttagbara chips i samma rad (server-resolverade labels via
  // /taxonomy/labels, kind-agnostisk sedan PR-1).
  employmentType: ReadonlyArray<string>;
  worktimeExtent: ReadonlyArray<string>;
  /**
   * STEG 5 (grade-filter, 2026-06-23) — valda matchningsgrader (enum-namn,
   * delmängd av Basic/Good/Strong). Tom lista = "Matchning av" (inget filter).
   * Runtime-view-state: ändring navigerar UTAN commit-flaggan (matchGrades hör
   * inte till recent-search-capturen, till skillnad från chip/sort).
   */
  matchGrades: ReadonlyArray<string>;
  /**
   * #300 PR-5 (ADR 0084) — "Visa relaterade också"-toggle:ns på/av (URL:
   * `?relaterade=on`). Bärs vidare i toolbar-navigeringar (grad-chip-×, sort,
   * Rensa) så de inte raderar toggle:n. Matchnings-kontrollen ägs nu av hero:n.
   */
  includeRelated: boolean;
  /**
   * issue #292 — matchnings-huvudbrytaren (`?matchning=off`). Bärs i toolbarens
   * bas-URL-state så sort/chip-×/Rensa bevarar den (förut tappades den eftersom
   * basen saknade fältet). Toolbaren togglar den ALDRIG — det gör hero-popovern.
   */
  matchningOff: boolean;
  /**
   * #383 → förenklat — "Dölj ansökta" (`?doljAnsokta=on`). Bärs i toolbarens
   * bas-URL-state så sort/chip-×/Rensa bevarar den (samma param-bevarande-skäl som
   * matchningOff — annars tappar en toolbar-navigering tyst toggle:n). Toolbaren
   * togglar den ALDRIG — toggle:n ägs av hero-filterraden.
   */
  hideApplied: boolean;
  /** conceptId → visningsnamn (server-resolverad, fallback redan ifylld). */
  resolvedLabels: Record<string, string>;
  q: string;
  sortBy: JobAdSortBy;
  pageSize?: string;
  /**
   * F4-16 (ADR 0076 Decision 7, CTO D8) — server-härlett: true så snart minst
   * en yrkesgrupp angetts i matchnings-preferenserna. Driver in-/jobb-
   * disclosuren: när användaren valt "Sortera efter matchning" UTAN angivet
   * yrke faller listan honest tillbaka till nyaste-först — disclosuren förklarar
   * det på sidan där kontrollen lever (F4-14 FAS-DEFERRAL-MANIFEST).
   */
  hasStatedDesiredOccupation: boolean;
  /**
   * issue #292 (senior-cto-advisor-bind) — matchnings-axelns SSOT, härledd i
   * `jobb-results.tsx`: `matchActive = hasStatedDesiredOccupation &&
   * !matchningOff`. Driver de tre gaterna i toolbaren: (b) match-sort-alternativet
   * + select-koercion, (c) grad-filtrets på/av + help-note, samt match-sort-
   * disclosuren. Skild från `hasStatedDesiredOccupation`: grad-filtret RENDERAS
   * när yrke är angett (så switchen kan slå PÅ matchningen igen) men är AV när
   * `matchActive` är false.
   */
  matchActive: boolean;
}

// Sort-alternativ i denna ordning. Labels per Klas-prompt E2e 2026-06-11 +
// F4-14 (match-sort). "(CV-match)"-suffixet UTGICK på Relevance — Relevance är
// ts_rank-FTS-relevans (ADR 0062), inte matchning. ExpiresAtAsc-mappningen
// on-disk-verifierad: ORDER BY ExpiresAt ASC NULLS LAST (JobAdSearchQuery.
// ApplySort) = kortast kvar till sista ansökningsdag först.
//
// "Sortera efter matchning" (F4-14, ADR 0076 Decision 4/5) placeras direkt
// efter "Relevans" så de två avsikts-styrda ordningarna (vad som passar dig)
// sitter samlade, före de rena datum-ordningarna. Den disablas ALDRIG på q
// (till skillnad från Relevance): match-sorten kräver ingen söktext och faller
// honest tillbaka till nyaste-ordning utan yrkespreferens (Decision 7).
//
// Etiketterna resolveras via next-intl inne i komponenten. `MatchDesc` är en
// DELAD enum-label (`enums.sort.MatchDesc`, via `jobAdSortLabel`); `uiKey: null`
// markerar det. De övriga är toolbar-egna ui-strängar (`ui.toolbar.sort*`) som
// EJ delar enum-katalogen — Relevance-enumlabeln är "Mest relevant" medan
// toolbaren visar "Relevans" (medveten divergens, Klas-prompt E2e).
const SORT_OPTIONS = [
  { value: "Relevance", uiKey: "toolbar.sortRelevance" },
  { value: "MatchDesc", uiKey: null },
  { value: "PublishedAtDesc", uiKey: "toolbar.sortPublishedDesc" },
  { value: "ExpiresAtAsc", uiKey: "toolbar.sortExpiresAsc" },
] as const satisfies ReadonlyArray<{
  value: JobAdSortBy;
  uiKey: string | null;
}>;

// Ikon per chip-axel (civic-restrained, lucide). yrke = Briefcase, ort =
// MapPin, anställningsform = FileText, omfattning = Clock, fritext = Search.
// Briefcase är upptaget av yrke → Klass-2-axlarna får egna ikoner (FileText
// för "form/avtal", Clock för "tid/omfattning").
const CHIP_ICON: Record<SearchChip["axis"], LucideIcon> = {
  region: MapPin,
  municipality: MapPin,
  occupationGroup: Briefcase,
  employmentType: FileText,
  worktimeExtent: Clock,
  q: Search,
};

export function JobbResultsToolbar({
  totalCount,
  occupationGroup,
  region,
  municipality,
  employmentType,
  worktimeExtent,
  matchGrades,
  includeRelated,
  matchningOff,
  hideApplied,
  resolvedLabels,
  q,
  sortBy,
  pageSize,
  hasStatedDesiredOccupation,
  matchActive,
}: JobbResultsToolbarProps) {
  const tEnum = useTranslations("jobads.enums");
  const t = useTranslations("jobads.ui");
  // Grad-labels för toolbar-lokala grad-chips. Eget namespace (next-intl typar
  // `t()` mot den literala message-key-unionen).
  const tGrade = useTranslations("jobads.ui.gradeFilter");
  const format = useFormatter();
  const router = useRouter();
  const [, startTransition] = useTransition();

  // q ägs av hero-fältet; toolbaren härleder bara Relevance-gaten
  // ur searchParam-propen (ADR 0042 Beslut D).
  const qReady = q.trim().length >= 2;

  // E2c (CTO VAL 2) — publicera list-svarets totalCount till hero-öns
  // "Visa N annonser"-knapp (total-count-store; SPOT — talet ägs av
  // PagedResult.TotalCount, aldrig en facett-summa).
  useEffect(() => {
    publishTotalCount(totalCount);
  }, [totalCount]);

  // URL-sanningen som bas + optimistiskt overlay (E2g/E2h — ersätter de
  // tidigare lokala useState-kopiorna).
  const base = useMemo<JobbUrlState>(
    () => ({
      q,
      occupationGroup: [...occupationGroup],
      region: [...region],
      municipality: [...municipality],
      employmentType: [...employmentType],
      worktimeExtent: [...worktimeExtent],
      matchGrades: [...matchGrades],
      // #300 PR-5 — bär "Visa relaterade också" i URL-state-basen så ALLA
      // toolbar-navigeringar (chip-×, Rensa, sort, grad-justeringar) bevarar
      // toggle:n (paritet matchGrades/matchningOff).
      includeRelated,
      // issue #292 / #383 — bär matchnings-huvudbrytaren OCH "Dölj ansökta" i
      // basen så sort/chip-×/Rensa bevarar `?matchning=off`/`?doljAnsokta=on`
      // (förut tappades de; toolbaren togglar dem ej — det gör hero-raden).
      matchningOff,
      hideApplied,
      sortBy,
      pageSize,
    }),
    [
      q,
      occupationGroup,
      region,
      municipality,
      employmentType,
      worktimeExtent,
      matchGrades,
      includeRelated,
      matchningOff,
      hideApplied,
      sortBy,
      pageSize,
    ],
  );
  const [urlState, setOptimisticState] = useOptimistic(
    base,
    (_current, next: JobbUrlState) => next,
  );

  // issue #292 — gate (b) (UI-sidan): match-sort-alternativet erbjuds BARA när
  // matchningen är aktiv. När den är av/saknar yrke droppas MatchDesc ur de
  // renderade alternativen helt (kontroll-paritet: ingen ordning man inte kan
  // motivera). jobb-results.tsx coercerar samtidigt list-queryns sort på SIN
  // sida (effectiveSortBy) så URL-sanning och faktisk ordning aldrig divergerar.
  const sortOptions = matchActive
    ? SORT_OPTIONS
    : SORT_OPTIONS.filter((o) => o.value !== "MatchDesc");

  // Om URL bär en sort utanför de RENDERADE alternativen (t.ex. PublishedAtAsc,
  // eller MatchDesc medan matchningen är av) visar select:en default men
  // toolbaren emitterar bara de erbjudna. Bevarar annars det riktiga sortBy-
  // värdet i URL-build tills användaren aktivt byter (annars skulle render
  // tvinga ett byte). issue #292: när matchningen är av faller en MatchDesc-URL
  // hit → selectValue blir DEFAULT_SORT_BY (visar nyaste-först, paritet med
  // jobb-results-koercionen).
  const selectValue = sortOptions.some((o) => o.value === urlState.sortBy)
    ? urlState.sortBy
    : DEFAULT_SORT_BY;

  // F4-16 (CTO D8) — in-/jobb-disclosure: visas BARA när match-sort är aktiv
  // OCH inget yrke angetts. Self-clearing — `selectValue` läser optimistiskt
  // urlState, så noten försvinner direkt när användaren byter sort (eller när
  // ett yrke ställs in → propen blir true vid nästa render). Icke-avfärdbar
  // (paritet Översikt-nudgen): den enda förklaringen på sidan till varför
  // ordningen är datum-baserad.
  //
  // issue #292 — match-sort-koercionen (gate (b)) gör att `selectValue` ALDRIG
  // är `MatchDesc` när matchningen är av/saknar yrke (MatchDesc droppas ur
  // sortOptions). Disclosuren — vars villkor är `!hasStatedDesiredOccupation` —
  // kan därför inte längre visas (utan yrke finns ingen valbar match-sort att
  // disclosera). Logiken behålls som SSOT för förklaringsraden men är i praktiken
  // vilande under den nya gateringen.
  const showMatchSortDisclosure =
    selectValue === "MatchDesc" && !hasStatedDesiredOccupation;

  // E2j (Klas-val 2026-06-12 = ja): toolbar-handlingar (ta bort chip / Rensa /
  // byt sort) är avsiktliga, diskreta sökningar → bär commit-intent (?commit=1)
  // så de auto-capturas till Senaste sökningar. commit-flaggan ligger UTANFÖR
  // JobbUrlState (transient suffix på push-strängen) och strippas efter mount.
  function commit(next: JobbUrlState) {
    startTransition(() => {
      setOptimisticState(next);
      router.push(withCommitFlag(buildJobbHref(next)));
    });
  }

  // STEG 5 (grade-filter) — navigering UTAN commit-intent. matchGrades är
  // runtime-view-state, INTE en avsiktlig sökning: en grad-justering ska aldrig
  // auto-capturas till Senaste sökningar (Klas: håll matchGrades utanför
  // recent-search/commit-angelägenheten). Samma optimistiska overlay som commit
  // för omedelbar respons; ren `buildJobbHref` (ingen ?commit=true).
  function navigate(next: JobbUrlState) {
    startTransition(() => {
      setOptimisticState(next);
      router.push(buildJobbHref(next));
    });
  }

  // Grad-chip-× navigerar via samma väg som hero-popovern (matchnings-kontrollen
  // ägs nu av hero:n; toolbaren behåller bara grad-chip-spegeln + dess ×). En tom
  // lista = "alla grader visas", INTE "av" (av styrs av hero-huvudbrytaren).
  function onMatchGradesChange(next: string[]) {
    navigate({ ...urlState, matchGrades: next, matchningOff: false });
  }

  function removeChip(chip: SearchChip) {
    commit(removeChipFromState(urlState, chip));
  }

  // E2i (Klas-beslut 2026-06-11, ersätter E2e-domen "q bevaras"): q-orden
  // visas nu som taggar i samma rad → "Rensa alla filter" nollar ALLT
  // inkl. sökorden (least surprise — allt med × i raden försvinner; hero-
  // fältet töms via extern-divergens-synken).
  function clearAllFilters() {
    commit({
      ...urlState,
      occupationGroup: [],
      region: [],
      municipality: [],
      // Klass 2 — "Rensa sökord och filter" nollar ALLA axlar inkl.
      // anställningsform/omfattning (least surprise — allt med × försvinner).
      employmentType: [],
      worktimeExtent: [],
      q: "",
    });
  }

  function onSortChange(e: React.ChangeEvent<HTMLSelectElement>) {
    commit({ ...urlState, sortBy: e.target.value as JobAdSortBy });
  }

  // Chips-ordning: region → municipality → occupationGroup → q-ord
  // (ordningen ägs av buildChipModels). E2i (Klas-spec): ALLA taggar —
  // även fritext-sökorden — visas här med ×; raden är sökets TOTALA spegel
  // (hero-fältet är best-effort, C′-modellen).
  const chips = buildChipModels(
    urlState,
    (_axis, conceptId) =>
      resolvedLabels[conceptId] ?? t("toolbar.unknownCode", { code: conceptId }),
    { includeQ: true },
  );

  // #408 — toolbar-lokala chips för popover-valen (grad + status). DERIVERAS
  // lokalt (INTE via buildChipModels — den är SPOT med hero-fältets in-field-
  // chips som inte får visa grad/status). Varje × kör samma navigate-väg som
  // popover-kontrollen (onMatchGradesChange / onStatusChange) så chip-× och
  // popover-avmarkering är samma state-operation, två renderingar.
  const activeMatchGrades = urlState.matchGrades;
  // Grad-chips: en per vald grad NÄR matchningen är PÅ och en delmängd är vald.
  // Tom lista ([] = alla visas) eller matchningen av → inga grad-chips. Filtrera
  // till de FILTRERBARA graderna (isListMatchGrade) så `grade.${G}` typar mot den
  // literala message-key-unionen (next-intl) — en stale icke-grad-token i URL:en
  // får aldrig en chip (samma drop-unknown-disciplin som page-validatorn).
  const matchGradeChips: ReadonlyArray<{
    grade: ListMatchGrade;
    label: string;
  }> =
    matchActive && activeMatchGrades.length > 0
      ? activeMatchGrades
          .filter(isListMatchGrade)
          .map((grade) => ({ grade, label: tGrade(`grade.${grade}`) }))
      : [];
  // Chips-rad syns när det finns sök/q-chips ELLER smalnade grad-chips. Status-
  // chips borttagna (Dölj ansökta syns på sin egen hero-toggle, inte som chip).
  const hasAnyToolbarChips =
    chips.length > 0 || matchGradeChips.length > 0;

  return (
    <>
    {/* ROW 1 — höjd-stabil canvas-rad: träffräknaren vänster, Sortera höger.
        Matchning + Dölj ansökta bor nu i hero-filterraden (2026-06-30, Klas), så
        sort är den enda kontrollen kvar nära jobben. Återbrukar .jp-results-toolbar
        flex-space-between-idiomet. */}
    <div className="jp-results-toolbar">
      <div
        className="jp-results-count"
        role="status"
        aria-live="polite"
      >
        {totalCount === 0 ? (
          t("toolbar.noHits")
        ) : (
          <>
            <b>{formatNumber(format, totalCount)}</b>{" "}
            {t("toolbar.hits", { count: totalCount })}
          </>
        )}
      </div>

      {/* Sortering — label + select läser de delade kontroll-tokenen
          (.jp-sortfield → --jp-control-h/-fs), ingen inline-px. "Sortera"-labeln
          behåller htmlFor-associationen (a11y). */}
      <div className="jp-sortfield">
        <label htmlFor="jobb-sort" className="jp-sortfield__label">
          {t("toolbar.sortLabel")}
        </label>
        <select
          id="jobb-sort"
          className="jp-sortfield__select"
          value={selectValue}
          onChange={onSortChange}
        >
          {sortOptions.map((opt) => (
            <option
              key={opt.value}
              value={opt.value}
              disabled={opt.value === "Relevance" && !qReady}
            >
              {opt.uiKey ? t(opt.uiKey) : jobAdSortLabel(tEnum, opt.value)}
            </option>
          ))}
        </select>
      </div>
    </div>

    {/* ROW 2 — chips-rad: de befintliga sök/q-chipsen (SPOT via buildChipModels)
        + toolbar-lokala grad/status-chips, så filter-staten ALLTID syns utan att
        öppna en popover (kriterium 5). role="group" exponerar aria-label på den
        generiska containern; namnet täcker sökord + filter (M2). */}
    {hasAnyToolbarChips && (
      <div
        className="jp-filterchips"
        role="group"
        aria-label={t("toolbar.activeFiltersLabel")}
      >
        {chips.map((chip) => {
          // Ikon per tagg-typ (CHIP_ICON — Klass 2 lade employmentType/
          // worktimeExtent; E2i — q-taggar särskiljs som Search).
          const ChipIcon = CHIP_ICON[chip.axis];
          return (
            <span key={`${chip.axis}-${chip.value}`} className="jp-filterchip">
              <ChipIcon size={12} aria-hidden="true" />
              {chip.label}
              <button
                type="button"
                className="jp-filterchip__rm"
                onClick={() => removeChip(chip)}
                aria-label={
                  chip.axis === "q"
                    ? t("toolbar.removeSearchTerm", { label: chip.label })
                    : t("toolbar.removeFilter", { label: chip.label })
                }
              >
                <X size={12} aria-hidden="true" />
              </button>
            </span>
          );
        })}

        {/* Grad-chips (toolbar-lokala, #408): en per smalnad grad. × kör samma
            navigate-väg som popover-avmarkering (onMatchGradesChange med graden
            borttagen). Civic-återhållen ikon: SlidersHorizontal (filter-justering). */}
        {matchGradeChips.map((c) => (
          <span key={`grade-${c.grade}`} className="jp-filterchip">
            <SlidersHorizontal size={12} aria-hidden="true" />
            {c.label}
            <button
              type="button"
              className="jp-filterchip__rm"
              onClick={() =>
                onMatchGradesChange(
                  activeMatchGrades.filter((g) => g !== c.grade),
                )
              }
              aria-label={t("toolbar.removeFilter", { label: c.label })}
            >
              <X size={12} aria-hidden="true" />
            </button>
          </span>
        ))}

        {/* "Rensa sökord och filter" — visas bara när det finns sök/q-chips att
            rensa (oförändrad clearAll: nollar dimensioner + q via commit). */}
        {chips.length > 0 && (
          <button
            type="button"
            className="jp-clearlink"
            onClick={clearAllFilters}
          >
            {t("toolbar.clearAll")}
          </button>
        )}
      </div>
    )}
    {/* F4-16 (CTO D8) — in-/jobb-disclosure. Platt civic info-not (ingen
        boxad alert, ingen skugga/gradient — design §3.D). Namnger orsak
        (yrke saknas) + beteende (nyaste först) + handling (kanonisk länk,
        IDENTISK med Översikt-nudgen). role="status" så skärmläsare aviseras
        när noten dyker upp efter sort-bytet (aria-live). */}
    {showMatchSortDisclosure && (
      <p className="jp-matchsort-note" role="status">
        {t("toolbar.matchSortDisclosure")}{" "}
        <Link href="/installningar#matchning" className="jp-matchsort-note__link">
          {t("toolbar.matchSortDisclosureLink")}
        </Link>
      </p>
    )}
    </>
  );
}

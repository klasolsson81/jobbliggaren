"use client";

import {
  useEffect,
  useMemo,
  useOptimistic,
  useRef,
  useState,
  useTransition,
} from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useFormatter, useTranslations } from "next-intl";
import { formatNumber } from "@/lib/i18n/format";
import {
  Bookmark,
  Briefcase,
  ChevronDown,
  Clock,
  EyeOff,
  FileText,
  MapPin,
  Search,
  Send,
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
import { InfoDialog } from "@/components/common/info-dialog";
import { JobbMatchGradeFilter } from "./jobb-match-grade-filter";
import {
  JobbStatusFilter,
  type StatusFilterState,
} from "./jobb-status-filter";
import { JobbToolbarPopover } from "./jobb-toolbar-popover";

/**
 * Result-toolbar för /jobb (HANDOVER-v3.md §7.2, ADR 0055).
 *
 * #408 (IA-konsolidering) — filter-arean är EN höjd-stabil rad + en chips-rad:
 * - ROW 1: `N träffar` (mono) vänster; höger-kluster `[Matchning ▾]`-pill + en
 *   ikon-only "?"-InfoDialog + `[Status ▾]`-pill + `Sortera [select ▾]`. Pillarna
 *   återbrukar hero-öns `.jp-hero-pill`-mönster (dot + mono count-badge +
 *   ChevronDown) och öppnar var sin enkelkolumns-popover (`JobbToolbarPopover`)
 *   som hyser de befintliga `JobbMatchGradeFilter`/`JobbStatusFilter`-kropparna.
 *   Matchnings-pillen renderas på `hasStatedDesiredOccupation`, Status-pillen på
 *   `hasSeeker` — annars är raden bara count + sort (fortfarande höjd-stabil).
 * - ROW 2 (chips): de befintliga sök/q-chipsen (SPOT via buildChipModels) PLUS
 *   toolbar-lokala chips för popover-valen (vald grad / aktiv status-facett) så
 *   filter-staten alltid syns utan att öppna en popover. Grad/status läggs
 *   ALDRIG i den delade buildChipModels (SPOT med hero-fältets in-field-chips,
 *   som inte får visa grad/status) — de härleds lokalt här.
 *
 * Tidigare (#378) tre staplade block (status-rad + match-rad + 387-tecken
 * hjälp-`<p>`) är ersatta; hjälptexten lever nu BARA i pillens "?"-InfoDialog.
 *
 * E2h: chips deriveras ur props (URL-sanningen) via delade
 * `buildChipModels`/`removeChipFromState` (lib/job-ads/chip-models —
 * SPOT med hero-fältets in-field-chips; × är SAMMA operation i båda
 * renderingarna). Tidigare lokala useState-kopior (E2g-divergent mönster
 * som bara överlevde via Suspense-remounten) ersatta med useOptimistic —
 * URL är enda sanningen, overlay:t ger omedelbar ×-respons.
 *
 * Labels: server-resolverad conceptId→label (ADR 0043 Beslut B, "Okänd
 * kod (<id>)"-fallback). Toolbar-× PUSHAR (CTO E2h VAL 2-asymmetrin:
 * fältet = pågående komposition → replace; toolbaren = redigering av
 * etablerad sökning → push).
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
   * `?relaterade=on`). Driver toggle:ns aria-checked + om `Related`-kryssrutan
   * renderas i grad-filtret. Runtime-view-state (navigerar utan commit-flaggan,
   * paritet matchGrades).
   */
  includeRelated: boolean;
  /**
   * #383 (CTO-bind cto-7f3a9c2e1b4d8a6f) — status-facetterna (URL:
   * `?sparade/?ansokta/?doljAnsokta=on`). Driver status-kontrollens kryssrutor.
   * ORTOGONALA mot matchningen — kontrollen renderas på `hasSeeker`, inte på
   * matchnings-axeln. Runtime-view-state (navigerar utan commit-flaggan, paritet
   * matchGrades).
   */
  savedOnly: boolean;
  appliedOnly: boolean;
  hideApplied: boolean;
  /**
   * #383 — true när användaren har en seeker (`getMyProfile().kind === "ok"`).
   * Gatar enbart status-kontrollens rendering (status filtrerar mot seekerns
   * sparade/ansökta; utan seeker finns inget att filtrera). Skild från
   * `hasStatedDesiredOccupation`: status kräver INTE ett angivet yrke.
   */
  hasSeeker: boolean;
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
  savedOnly,
  appliedOnly,
  hideApplied,
  hasSeeker,
  resolvedLabels,
  q,
  sortBy,
  pageSize,
  hasStatedDesiredOccupation,
  matchActive,
}: JobbResultsToolbarProps) {
  const tEnum = useTranslations("jobads.enums");
  const t = useTranslations("jobads.ui");
  // #408 — grad/status-labels för toolbar-lokala chips + InfoDialog. Skilda
  // namespaces (next-intl typar `t()` mot den literala message-key-unionen).
  const tGrade = useTranslations("jobads.ui.gradeFilter");
  const tStatus = useTranslations("jobads.ui.statusFilter");
  const format = useFormatter();
  const router = useRouter();
  const [, startTransition] = useTransition();

  // #408 — vilken toolbar-popover som är öppen (en i taget, samma mönster som
  // hero-öns `openPop`). Triggerns ref driver popoverns position + fokus-retur.
  const [openPop, setOpenPop] = useState<"match" | "status" | null>(null);
  const matchBtnRef = useRef<HTMLButtonElement>(null);
  const statusBtnRef = useRef<HTMLButtonElement>(null);

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
      // #383 — bär status-facetterna i URL-state-basen så ALLA toolbar-
      // navigeringar (chip-×, Rensa, sort, grad-/status-justeringar) bevarar dem.
      savedOnly,
      appliedOnly,
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
      savedOnly,
      appliedOnly,
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

  function onMatchGradesChange(next: string[]) {
    // issue #292 — grad-justeringar sker BARA när matchningen redan är PÅ
    // (matchningOff=false). En tom lista betyder här "alla grader visas",
    // INTE "av" (av styrs av huvudbrytaren). Behåll matchningOff=false.
    navigate({ ...urlState, matchGrades: next, matchningOff: false });
  }

  // issue #292 — huvudbrytaren AV: skriv `?matchning=off` + TÖM matchGrades
  // ("forget"-semantik, CTO-bind: en senare PÅ återställer till alla grader,
  // inte den tidigare smalnade delmängden).
  // #300 PR-5 — samma "forget"-semantik för related: AV nollar `includeRelated`
  // (den subordinerade toggle:n försvinner med matchningen ⇒ `?relaterade=on` får
  // inte ligga kvar inert i URL:en). `Related` försvinner ur matchGrades med
  // tömningen ovan, så ingen state/URL-divergens kvarstår.
  function onMatchTurnOff() {
    navigate({
      ...urlState,
      matchningOff: true,
      matchGrades: [],
      includeRelated: false,
    });
  }

  // issue #292 — huvudbrytaren PÅ: ta bort off-flaggan + lämna matchGrades tomt
  // (= alla grader visas). Renderas av grad-filtret som ALLA-ikryssade.
  function onMatchTurnOn() {
    navigate({ ...urlState, matchningOff: false, matchGrades: [] });
  }

  // #300 PR-5 — "Visa relaterade också"-toggle:n. Skriver/tar bort `?relaterade=on`.
  // STATE-MODEL FLOW-TRAP (design-reviewer): vid AV MÅSTE `Related` droppas ur den
  // valda grad-listan — en kvarvarande `Related`-token filtrerar på en grad vars
  // kontroll (kryssrutan) inte längre renderas (state/URL-divergens). Navigerar
  // utan commit-flaggan (runtime-view-state, paritet matchGrades).
  function onRelatedToggle(next: boolean) {
    navigate({
      ...urlState,
      includeRelated: next,
      matchGrades: next
        ? urlState.matchGrades
        : urlState.matchGrades.filter((g) => g !== "Related"),
    });
  }

  // #383 — status-facetterna. Runtime-view-state (paritet matchGrades): navigerar
  // UTAN commit-flaggan (ingen recent-search-capture). Komponenten upprätthåller
  // mutex:en (Ansökta XOR Dölj ansökta) och levererar hela nästa status-läget; vi
  // trådar bara vidare det till URL:en via den optimistiska overlay:n.
  function onStatusChange(next: StatusFilterState) {
    navigate({ ...urlState, ...next });
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
  // Status-chips: en per aktiv facett (Sparade/Ansökta/Dölj ansökta).
  const statusChips: ReadonlyArray<{
    key: "saved" | "applied" | "hideApplied";
    label: string;
    icon: LucideIcon;
    next: StatusFilterState;
  }> = [
    ...(urlState.savedOnly
      ? [
          {
            key: "saved" as const,
            label: tStatus("saved"),
            icon: Bookmark,
            // × stänger av just denna facett (övriga bevaras).
            next: {
              savedOnly: false,
              appliedOnly: urlState.appliedOnly ?? false,
              hideApplied: urlState.hideApplied ?? false,
            },
          },
        ]
      : []),
    ...(urlState.appliedOnly
      ? [
          {
            key: "applied" as const,
            label: tStatus("applied"),
            icon: Send,
            next: {
              savedOnly: urlState.savedOnly ?? false,
              appliedOnly: false,
              hideApplied: urlState.hideApplied ?? false,
            },
          },
        ]
      : []),
    ...(urlState.hideApplied
      ? [
          {
            key: "hideApplied" as const,
            label: tStatus("hideApplied"),
            icon: EyeOff,
            next: {
              savedOnly: urlState.savedOnly ?? false,
              appliedOnly: urlState.appliedOnly ?? false,
              hideApplied: false,
            },
          },
        ]
      : []),
  ];

  // Pill-tillstånd. Matchnings-pillen: data-active = matchningen PÅ; count-badge
  // = antal smalnade grad-val (0 = "alla visas", ingen badge). Visa prick när
  // PÅ även utan smalning. Status-pillen: data-active = någon facett på; count =
  // antal aktiva facetter.
  // Gatat på matchActive (paritet med matchGradeChips): en stale URL
  // (`?matchning=off&matchGrades=Good`) får aldrig en count-badge på en pill som
  // inte är aktiv och inte har någon chip — badge, prick och chips berättar samma.
  const matchActiveCount = matchActive ? activeMatchGrades.length : 0;
  const statusActiveCount = statusChips.length;
  const hasAnyToolbarChips =
    chips.length > 0 || matchGradeChips.length > 0 || statusChips.length > 0;

  return (
    <>
    {/* ROW 1 — höjd-stabil canvas-rad: träffräknaren vänster, höger-kluster
        (Matchning-pill + "?" + Status-pill + Sortera). Pillarna renderas bara när
        deras axel är relevant; raden faller annars tillbaka till count + sort
        (fortfarande höjd-stabil). Återbrukar .jp-results-toolbar flex-space-
        between-idiomet. */}
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

      <div className="flex flex-wrap items-center gap-3">
        {/* [Matchning ▾] — renderas på hasStatedDesiredOccupation (så switchen
            i popovern kan slå PÅ matchningen igen även när den är av). data-active
            = matchActive; prick visas när PÅ; count-badge = antal smalnade
            grad-val (0 = alla visas, ingen badge). Hero-pill-mönstret (dot +
            mono-count + ChevronDown, aria-haspopup="dialog"/aria-expanded). */}
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

        {/* "?" — ikon-only InfoDialog OMEDELBART till höger om Matchnings-pillen
            (entydig referent). Texten är verbatim gradeFilter.help +
            relatedToggleHelp (#408 kriterium 9 — ingen omformulering). Renderas
            tillsammans med pillen (samma gate) så den inte står referent-lös. */}
        {hasStatedDesiredOccupation && (
          <InfoDialog
            iconOnly
            title={tGrade("helpTitle")}
            paragraphs={[tGrade("help"), tGrade("relatedToggleHelp")]}
          />
        )}

        {/* [Status ▾] — renderas på hasSeeker (status filtrerar mot seekerns
            sparade/ansökta; utan seeker finns inget att filtrera). data-active =
            någon facett på; count-badge = antal aktiva facetter. */}
        {hasSeeker && (
          <button
            ref={statusBtnRef}
            type="button"
            className="jp-hero-pill"
            data-active={openPop === "status" || statusActiveCount > 0}
            aria-haspopup="dialog"
            aria-expanded={openPop === "status"}
            onClick={() => setOpenPop(openPop === "status" ? null : "status")}
          >
            {statusActiveCount > 0 && (
              <span className="jp-hero-pill__dot" aria-hidden="true" />
            )}
            {tStatus("label")}
            {statusActiveCount > 0 && (
              <span className="jp-hero-pill__count">{statusActiveCount}</span>
            )}
            <ChevronDown size={14} aria-hidden="true" />
          </button>
        )}

        {/* Sortering — "Sortera"-labeln behåller htmlFor-associationen (a11y). */}
        <div style={{ display: "flex", flexDirection: "column", gap: 4 }}>
          <label
            htmlFor="jobb-sort"
            style={{
              display: "block",
              fontSize: 14,
              color: "var(--jp-ink-2)",
            }}
          >
            {t("toolbar.sortLabel")}
          </label>
          <select
            id="jobb-sort"
            className="jp-select"
            style={{ height: 40, width: "auto", minWidth: 180 }}
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
    </div>

    {/* #408 — Matchnings-popovern: enkelkolumns-skal (JobbToolbarPopover) som
        hyser den befintliga JobbMatchGradeFilter-kroppen, wired till SAMMA
        parent-handlers (commit-/navigate-semantiken oförändrad). */}
    {hasStatedDesiredOccupation && (
      <JobbToolbarPopover
        open={openPop === "match"}
        title={tGrade("toggleLabel")}
        triggerRef={matchBtnRef}
        onClose={() => setOpenPop(null)}
      >
        <JobbMatchGradeFilter
          active={matchActive}
          selected={urlState.matchGrades}
          includeRelated={urlState.includeRelated ?? false}
          onChange={onMatchGradesChange}
          onTurnOff={onMatchTurnOff}
          onTurnOn={onMatchTurnOn}
          onRelatedToggle={onRelatedToggle}
        />
      </JobbToolbarPopover>
    )}

    {/* #408 — Status-popovern: samma skal, egen triggerRef. JobbStatusFilter
        bevarar mutex-logiken (Ansökta ⊕ Dölj ansökta). */}
    {hasSeeker && (
      <JobbToolbarPopover
        open={openPop === "status"}
        title={tStatus("label")}
        triggerRef={statusBtnRef}
        onClose={() => setOpenPop(null)}
      >
        <JobbStatusFilter
          savedOnly={urlState.savedOnly ?? false}
          appliedOnly={urlState.appliedOnly ?? false}
          hideApplied={urlState.hideApplied ?? false}
          onChange={onStatusChange}
        />
      </JobbToolbarPopover>
    )}

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

        {/* Status-chips (toolbar-lokala, #408): en per aktiv facett. × kör samma
            onStatusChange-väg (mutex bevaras i state-objektet). Egen civic-ikon
            per facett (Bookmark/Send/EyeOff). */}
        {statusChips.map((c) => {
          const ChipIcon = c.icon;
          return (
            <span key={`status-${c.key}`} className="jp-filterchip">
              <ChipIcon size={12} aria-hidden="true" />
              {c.label}
              <button
                type="button"
                className="jp-filterchip__rm"
                onClick={() => onStatusChange(c.next)}
                aria-label={t("toolbar.removeFilter", { label: c.label })}
              >
                <X size={12} aria-hidden="true" />
              </button>
            </span>
          );
        })}

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

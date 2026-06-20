"use client";

import { useEffect, useMemo, useOptimistic, useTransition } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import {
  Briefcase,
  Clock,
  FileText,
  MapPin,
  Search,
  X,
} from "lucide-react";
import type { LucideIcon } from "lucide-react";
import type { JobAdSortBy } from "@/lib/dto/job-ads";
import { MATCH_SORT_LABEL } from "@/lib/job-ads/status";
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
 * Result-toolbar för /jobb (HANDOVER-v3.md §7.2, ADR 0055). En rad:
 * `N träffar` (mono) vänster + aktiva filter-chips + `Sortera [select ▾]`
 * höger. INGET separat sorterings-block (Klas F4-spec).
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
const SORT_OPTIONS: ReadonlyArray<{ value: JobAdSortBy; label: string }> = [
  { value: "Relevance", label: "Relevans" },
  // F4-16 — kanonisk match-sort-label (SPOT med JOB_AD_SORT_LABELS.MatchDesc;
  // de kan aldrig drifta isär längre).
  { value: "MatchDesc", label: MATCH_SORT_LABEL },
  { value: "PublishedAtDesc", label: "Datum (nyast)" },
  { value: "ExpiresAtAsc", label: "Ansökningsdatum (sista ansökan)" },
];

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
  resolvedLabels,
  q,
  sortBy,
  pageSize,
  hasStatedDesiredOccupation,
}: JobbResultsToolbarProps) {
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
      sortBy,
      pageSize,
    ],
  );
  const [urlState, setOptimisticState] = useOptimistic(
    base,
    (_current, next: JobbUrlState) => next,
  );

  // Om URL bär en sort utanför de tre locked alternativen (t.ex.
  // PublishedAtAsc), visar select:en default men toolbaren emitterar
  // bara de tre. Bevarar ändå det riktiga sortBy-värdet i URL-build tills
  // användaren aktivt byter (annars skulle render tvinga ett byte).
  const selectValue = SORT_OPTIONS.some((o) => o.value === urlState.sortBy)
    ? urlState.sortBy
    : DEFAULT_SORT_BY;

  // F4-16 (CTO D8) — in-/jobb-disclosure: visas BARA när match-sort är aktiv
  // OCH inget yrke angetts. Self-clearing — `selectValue` läser optimistiskt
  // urlState, så noten försvinner direkt när användaren byter sort (eller när
  // ett yrke ställs in → propen blir true vid nästa render). Icke-avfärdbar
  // (paritet Översikt-nudgen): den enda förklaringen på sidan till varför
  // ordningen är datum-baserad.
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
      resolvedLabels[conceptId] ?? `Okänd kod (${conceptId})`,
    { includeQ: true },
  );

  return (
    <>
    <div className="jp-results-toolbar">
      <div>
        <div
          className="jp-results-count"
          role="status"
          aria-live="polite"
        >
          {totalCount === 0 ? (
            "Inga träffar"
          ) : (
            <>
              <b>{totalCount.toLocaleString("sv-SE")}</b>{" "}
              {totalCount === 1 ? "träff" : "träffar"}
            </>
          )}
        </div>
        {/* role="group" krävs för att aria-label ska exponeras på en
            generisk container (design Mi3 E2i); namnet täcker både
            sökord och filter (M2). */}
        {chips.length > 0 && (
          <div
            className="jp-filterchips"
            role="group"
            aria-label="Aktiva sökord och filter"
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
                      ? `Ta bort sökordet ${chip.label}`
                      : `Ta bort filter ${chip.label}`
                  }
                >
                  <X size={12} aria-hidden="true" />
                </button>
              </span>
              );
            })}
            {/* Länktexten säger vad den gör (design M2 E2i — ADR 0047:
                handlingen raderar även egenskrivna sökord och ska
                kommunicera det före klicket). */}
            <button
              type="button"
              className="jp-clearlink"
              onClick={clearAllFilters}
            >
              Rensa sökord och filter
            </button>
          </div>
        )}
      </div>

      <div style={{ display: "flex", gap: 12, alignItems: "center" }}>
        <label
          htmlFor="jobb-sort"
          style={{ fontSize: 14, color: "var(--jp-ink-2)" }}
        >
          Sortera
        </label>
        <select
          id="jobb-sort"
          className="jp-select"
          style={{ height: 40, width: "auto", minWidth: 180 }}
          value={selectValue}
          onChange={onSortChange}
        >
          {SORT_OPTIONS.map((opt) => (
            <option
              key={opt.value}
              value={opt.value}
              disabled={opt.value === "Relevance" && !qReady}
            >
              {opt.label}
            </option>
          ))}
        </select>
      </div>
    </div>
    {/* F4-16 (CTO D8) — in-/jobb-disclosure. Platt civic info-not (ingen
        boxad alert, ingen skugga/gradient — design §3.D). Namnger orsak
        (yrke saknas) + beteende (nyaste först) + handling (kanonisk länk,
        IDENTISK med Översikt-nudgen). role="status" så skärmläsare aviseras
        när noten dyker upp efter sort-bytet (aria-live). */}
    {showMatchSortDisclosure && (
      <p className="jp-matchsort-note" role="status">
        Matchningssortering kräver att du anger vilka yrken du söker inom. Listan
        visas med nyaste först tills du ställt in det.{" "}
        <Link href="/installningar#matchning" className="jp-matchsort-note__link">
          Ställ in matchning
        </Link>
      </p>
    )}
    </>
  );
}

"use client";

import { useEffect, useMemo, useOptimistic, useTransition } from "react";
import { useRouter } from "next/navigation";
import { Briefcase, MapPin, X } from "lucide-react";
import type { JobAdSortBy } from "@/lib/dto/job-ads";
import {
  buildJobbHref,
  DEFAULT_SORT_BY,
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
  /** conceptId → visningsnamn (server-resolverad, fallback redan ifylld). */
  resolvedLabels: Record<string, string>;
  q: string;
  sortBy: JobAdSortBy;
  pageSize?: string;
}

// Exakt tre alternativ, i denna ordning. Labels per Klas-prompt E2e
// 2026-06-11. "(CV-match)"-suffixet UTGICK — Relevance är ts_rank-FTS-
// relevans (ADR 0062), inte CV-matchning (ADR 0040 Fas 4+, ADR 0042
// Beslut F: ingen CV-match-placeholder i UI). ExpiresAtAsc-mappningen
// on-disk-verifierad: ORDER BY ExpiresAt ASC NULLS LAST (JobAdSearchQuery.
// ApplySort) = kortast kvar till sista ansökningsdag först.
const SORT_OPTIONS: ReadonlyArray<{ value: JobAdSortBy; label: string }> = [
  { value: "Relevance", label: "Relevans" },
  { value: "PublishedAtDesc", label: "Datum (nyast)" },
  { value: "ExpiresAtAsc", label: "Ansökningsdatum (sista ansökan)" },
];

export function JobbResultsToolbar({
  totalCount,
  occupationGroup,
  region,
  municipality,
  resolvedLabels,
  q,
  sortBy,
  pageSize,
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
      sortBy,
      pageSize,
    }),
    [q, occupationGroup, region, municipality, sortBy, pageSize],
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

  function commit(next: JobbUrlState) {
    startTransition(() => {
      setOptimisticState(next);
      router.push(buildJobbHref(next));
    });
  }

  function removeChip(chip: SearchChip) {
    commit(removeChipFromState(urlState, chip));
  }

  // E2e (ADR 0067 rad 109): "Rensa alla filter" = röd text-länk (ej knapp)
  // som nollar alla tre filter-axlarna. q bevaras — söktermen ägs av
  // hero-fältet och representeras inte i toolbarens chips.
  function clearAllFilters() {
    commit({ ...urlState, occupationGroup: [], region: [], municipality: [] });
  }

  function onSortChange(e: React.ChangeEvent<HTMLSelectElement>) {
    commit({ ...urlState, sortBy: e.target.value as JobAdSortBy });
  }

  // Chips-ordning: region → municipality → occupationGroup (geografin
  // samlad — E2b-architect-dom fråga 5; ordningen ägs av buildChipModels).
  // q-orden visas INTE här (söktermen är inte ett filter-chip i toolbaren).
  const chips = buildChipModels(
    urlState,
    (_axis, conceptId) =>
      resolvedLabels[conceptId] ?? `Okänd kod (${conceptId})`,
    { includeQ: false },
  );

  return (
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
        {chips.length > 0 && (
          <div
            className="jp-filterchips"
            aria-label="Aktiva filter"
          >
            {chips.map((chip) => (
              <span key={`${chip.axis}-${chip.value}`} className="jp-filterchip">
                {chip.axis === "occupationGroup" ? (
                  <Briefcase size={12} aria-hidden="true" />
                ) : (
                  <MapPin size={12} aria-hidden="true" />
                )}
                {chip.label}
                <button
                  type="button"
                  className="jp-filterchip__rm"
                  onClick={() => removeChip(chip)}
                  aria-label={`Ta bort filter ${chip.label}`}
                >
                  <X size={12} aria-hidden="true" />
                </button>
              </span>
            ))}
            <button
              type="button"
              className="jp-clearlink"
              onClick={clearAllFilters}
            >
              Rensa alla filter
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
  );
}

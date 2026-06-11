"use client";

import { useRef, useState, useTransition } from "react";
import { useRouter } from "next/navigation";
import { Briefcase, MapPin, X } from "lucide-react";
import type { JobAdSortBy } from "@/lib/dto/job-ads";
import {
  buildJobbHref,
  DEFAULT_SORT_BY,
} from "@/lib/job-ads/search-params";

/**
 * Result-toolbar för /jobb (HANDOVER-v3.md §7.2, ADR 0055). En rad:
 * `N träffar` (mono) vänster + aktiva filter-chips + `Sortera [select ▾]`
 * höger. INGET separat sorterings-block (Klas F4-spec).
 *
 * Chips: label via server-resolverad conceptId→label (ADR 0043 Beslut B,
 * "Okänd kod (<id>)"-fallback redan applicerad i page.tsx). × tar bort
 * den conceptId ur rätt searchParam-axel, övriga params bevaras
 * (`buildJobbHref` — symmetriskt med hero-pills).
 *
 * Sort: native `<select>`. Relevance disablad när q < 2 tecken
 * (ADR 0042 Beslut D — icke-förhandlingsbar invariant, härledd ur
 * q-searchParam-propen, EJ lokal state).
 */

interface ChipModel {
  conceptId: string;
  label: string;
  axis: "occupationGroup" | "region" | "municipality";
}

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

function labelFor(
  conceptId: string,
  resolvedLabels: Record<string, string>,
): string {
  return resolvedLabels[conceptId] ?? `Okänd kod (${conceptId})`;
}

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

  // q ägs av hero-GET-formuläret; toolbaren härleder bara Relevance-gaten
  // ur searchParam-propen (ADR 0042 Beslut D). Lokal state hålls för
  // chips/sort så UI:t inte hoppar innan RSC-omrendering landat.
  const qReady = q.trim().length >= 2;

  const [occupationGroupState, setOccupationGroup] = useState<string[]>([
    ...occupationGroup,
  ]);
  const [regionState, setRegion] = useState<string[]>([...region]);
  const [municipalityState, setMunicipality] = useState<string[]>([
    ...municipality,
  ]);
  const selectRef = useRef<HTMLSelectElement>(null);

  // Om URL bär en sort utanför de tre locked alternativen (t.ex.
  // PublishedAtAsc), visar select:en default men toolbaren emitterar
  // bara de tre. Bevarar ändå det riktiga sortBy-värdet i URL-build tills
  // användaren aktivt byter (annars skulle render tvinga ett byte).
  const selectValue = SORT_OPTIONS.some((o) => o.value === sortBy)
    ? sortBy
    : DEFAULT_SORT_BY;

  function pushState(
    nextOccupationGroup: string[],
    nextRegion: string[],
    nextMunicipality: string[],
  ) {
    startTransition(() => {
      router.push(
        buildJobbHref({
          q,
          occupationGroup: nextOccupationGroup,
          region: nextRegion,
          municipality: nextMunicipality,
          sortBy,
          pageSize,
        }),
      );
    });
  }

  function removeChip(chip: ChipModel) {
    if (chip.axis === "occupationGroup") {
      const next = occupationGroupState.filter((v) => v !== chip.conceptId);
      setOccupationGroup(next);
      pushState(next, regionState, municipalityState);
    } else if (chip.axis === "region") {
      const next = regionState.filter((v) => v !== chip.conceptId);
      setRegion(next);
      pushState(occupationGroupState, next, municipalityState);
    } else {
      const next = municipalityState.filter((v) => v !== chip.conceptId);
      setMunicipality(next);
      pushState(occupationGroupState, regionState, next);
    }
  }

  // E2e (ADR 0067 rad 109): "Rensa alla filter" = röd text-länk (ej knapp)
  // som nollar alla tre filter-axlarna. q bevaras — söktermen ägs av
  // hero-formuläret och är inte ett filter-chip.
  function clearAllFilters() {
    setOccupationGroup([]);
    setRegion([]);
    setMunicipality([]);
    pushState([], [], []);
  }

  function onSortChange(e: React.ChangeEvent<HTMLSelectElement>) {
    const next = e.target.value as JobAdSortBy;
    startTransition(() => {
      router.push(
        buildJobbHref({
          q,
          occupationGroup: occupationGroupState,
          region: regionState,
          municipality: municipalityState,
          sortBy: next,
          pageSize,
        }),
      );
    });
  }

  // Chips-ordning: region → municipality → occupationGroup (geografin
  // samlad — E2b-architect-dom fråga 5). Kommun delar MapPin med region:
  // chipen representerar dimensionen Ort; län/kommun är granulariteter.
  const chips: ChipModel[] = [
    ...regionState.map<ChipModel>((conceptId) => ({
      conceptId,
      label: labelFor(conceptId, resolvedLabels),
      axis: "region",
    })),
    ...municipalityState.map<ChipModel>((conceptId) => ({
      conceptId,
      label: labelFor(conceptId, resolvedLabels),
      axis: "municipality",
    })),
    ...occupationGroupState.map<ChipModel>((conceptId) => ({
      conceptId,
      label: labelFor(conceptId, resolvedLabels),
      axis: "occupationGroup",
    })),
  ];

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
              <span key={`${chip.axis}-${chip.conceptId}`} className="jp-filterchip">
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
          ref={selectRef}
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

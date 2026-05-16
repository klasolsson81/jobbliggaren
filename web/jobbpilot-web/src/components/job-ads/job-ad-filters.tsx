"use client";

import { useRouter } from "next/navigation";
import { useId, useState, useTransition } from "react";
import { ChevronDown } from "lucide-react";
import {
  jobAdFiltersSchema,
  type JobAdFiltersValues,
  type JobAdSortBy,
} from "@/lib/dto/job-ads";
import { JOB_AD_SORT_LABELS } from "@/lib/job-ads/status";
import { Button } from "@/components/ui/button";
import { JobAdMultiSelect } from "./job-ad-multi-select";
import { JobAdTypeahead } from "./job-ad-typeahead";

interface JobAdFiltersProps {
  initial: JobAdFiltersValues;
  // Antal aktiva taxonomi-/sort-filter (för disclosure-räknaren). Beräknas i
  // page.tsx (Server Component) så disclosuren kan visa "Filter (2)".
  activeFilterCount: number;
}

const SORT_OPTIONS: ReadonlyArray<JobAdSortBy> = [
  "PublishedAtDesc",
  "PublishedAtAsc",
  "ExpiresAtDesc",
  "ExpiresAtAsc",
  "Relevance",
];

type FieldErrors = Partial<Record<keyof JobAdFiltersValues, string>>;

/**
 * URL-driven sök-yta. ADR 0042:
 * - Beslut A: kollaps-filteryta. Sökfältet (q + typeahead) är alltid synligt
 *   ovanför resultatet (resultat-först, regel 3). Taxonomi-filter + sortering
 *   ligger bakom en disclosure (regel 7, undvik power-tool-täthet) — inte en
 *   alltid-expanderad panel.
 * - Beslut B: ssyk/region är multi-select (chips), URL-driven (router.push
 *   med upprepade query-params).
 * - Beslut C: q har live-typeahead; valt förslag tillämpas direkt som sökning.
 * - Beslut D: Relevance i sorteringen endast valbar med söktext (≥2 tecken).
 *
 * Submit triggar `router.push('/jobb?...')` → Server Component re-render med
 * ny searchParams. Ingen useEffect-fetch för listan (CLAUDE.md §5.2).
 * State hålls i useState (kontrollerade fält, ej stort RHF-formulär — speglar
 * codebase-konventionen för raw control utan resolver).
 */
export function JobAdFilters({ initial, activeFilterCount }: JobAdFiltersProps) {
  const router = useRouter();
  const panelId = useId();
  const [isPending, startTransition] = useTransition();
  const [errors, setErrors] = useState<FieldErrors>({});
  const [open, setOpen] = useState(activeFilterCount > 0);

  const [q, setQ] = useState(initial.q);
  const [ssyk, setSsyk] = useState<string[]>([...initial.ssyk]);
  const [region, setRegion] = useState<string[]>([...initial.region]);
  const [sortBy, setSortBy] = useState<JobAdSortBy>(initial.sortBy);

  const qReady = q.trim().length >= 2;

  function applyValues(values: JobAdFiltersValues) {
    const parsed = jobAdFiltersSchema.safeParse(values);
    if (!parsed.success) {
      const next: FieldErrors = {};
      for (const issue of parsed.error.issues) {
        const key = issue.path[0];
        if (typeof key === "string" && !next[key as keyof JobAdFiltersValues]) {
          next[key as keyof JobAdFiltersValues] = issue.message;
        }
      }
      setErrors(next);
      return;
    }
    setErrors({});

    const params = new URLSearchParams();
    for (const v of parsed.data.ssyk) params.append("ssyk", v);
    for (const v of parsed.data.region) params.append("region", v);
    if (parsed.data.q) params.set("q", parsed.data.q);
    if (parsed.data.sortBy !== "PublishedAtDesc") {
      params.set("sortBy", parsed.data.sortBy);
    }
    // Filter-ändring nollställer pagineringen — annars riskerar användaren en
    // sida som inte längre finns i det nya, smalare resultatet.
    const qs = params.toString();
    startTransition(() => {
      router.push(qs.length > 0 ? `/jobb?${qs}` : "/jobb");
    });
  }

  function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    applyValues({ q, ssyk, region, sortBy });
  }

  function onReset() {
    setQ("");
    setSsyk([]);
    setRegion([]);
    setSortBy("PublishedAtDesc");
    setErrors({});
    startTransition(() => {
      router.push("/jobb");
    });
  }

  return (
    <form
      onSubmit={onSubmit}
      className="flex flex-col gap-4 border-y border-border-default px-1 py-4.5"
      aria-label="Sök och filtrera jobbannonser"
    >
      <div className="flex flex-col gap-1.5">
        <label
          htmlFor="filter-q"
          className="text-label font-medium text-text-primary"
        >
          Sökord
        </label>
        <JobAdTypeahead
          id="filter-q"
          value={q}
          onChange={setQ}
          onSelect={(term) => {
            setQ(term);
            applyValues({ q: term, ssyk, region, sortBy });
          }}
          ariaInvalid={errors.q ? true : undefined}
          ariaDescribedBy={errors.q ? "filter-q-error" : undefined}
        />
        {errors.q && (
          <p
            id="filter-q-error"
            role="alert"
            className="text-body-sm text-danger-700"
          >
            {errors.q}
          </p>
        )}
      </div>

      <div className="flex flex-col gap-4 border-t border-border-default pt-4">
        <button
          type="button"
          onClick={() => setOpen((v) => !v)}
          aria-expanded={open}
          aria-controls={panelId}
          className="flex items-center gap-2 self-start text-label font-medium text-text-primary"
        >
          <ChevronDown
            className={`size-4 transition-transform duration-150 ${open ? "rotate-180" : ""}`}
            aria-hidden="true"
          />
          {activeFilterCount > 0
            ? `Filter (${activeFilterCount} aktiva)`
            : "Filter"}
        </button>

        {open && (
          <div id={panelId} className="flex flex-col gap-4">
            <div className="grid gap-4 md:grid-cols-2">
              <JobAdMultiSelect
                label="Yrkesområde"
                hint="JobTech-yrkeskod (concept-id), t.ex. MVqp_eS8_kDZ. Lägg till flera för OR-bevakning."
                values={ssyk}
                onChange={setSsyk}
              />
              <JobAdMultiSelect
                label="Region"
                hint="JobTech-region (concept-id), t.ex. CifL_Rzy_Mku. Lägg till flera för OR-bevakning."
                values={region}
                onChange={setRegion}
              />
            </div>

            <div className="flex flex-col gap-1.5">
              <label
                htmlFor="filter-sort"
                className="text-label font-medium text-text-primary"
              >
                Sortering
              </label>
              <select
                id="filter-sort"
                value={sortBy}
                onChange={(e) => setSortBy(e.target.value as JobAdSortBy)}
                aria-describedby={
                  errors.sortBy ? "filter-sort-error" : "filter-sort-hint"
                }
                className="h-11 rounded-md border border-border-default bg-surface-primary px-2.5 text-body text-text-primary focus:outline-2 focus:outline-offset-2 focus:outline-ring"
              >
                {SORT_OPTIONS.map((opt) => (
                  <option
                    key={opt}
                    value={opt}
                    // Beslut D — Relevance kräver söktext. Disablad utan q
                    // så användaren aldrig kan trigga backend-400:n.
                    disabled={opt === "Relevance" && !qReady}
                  >
                    {JOB_AD_SORT_LABELS[opt]}
                  </option>
                ))}
              </select>
              {errors.sortBy ? (
                <p
                  id="filter-sort-error"
                  role="alert"
                  className="text-body-sm text-danger-700"
                >
                  {errors.sortBy}
                </p>
              ) : (
                <p
                  id="filter-sort-hint"
                  className="text-body-sm text-text-secondary"
                >
                  Mest relevant kan väljas när du har ett sökord på minst 2
                  tecken.
                </p>
              )}
            </div>
          </div>
        )}
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <Button type="submit" disabled={isPending}>
          {isPending ? "Söker…" : "Sök"}
        </Button>
        <Button
          type="button"
          variant="outline"
          onClick={onReset}
          disabled={isPending}
        >
          Återställ
        </Button>
      </div>
    </form>
  );
}

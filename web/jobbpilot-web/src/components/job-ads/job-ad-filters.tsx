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
import type { TaxonomyTree } from "@/lib/dto/taxonomy";
import { Button } from "@/components/ui/button";
import { RegionPicker } from "./region-picker";
import { OccupationPicker } from "./occupation-picker";

interface JobAdFiltersProps {
  initial: JobAdFiltersValues;
  // Antal aktiva taxonomi-/sort-filter (för disclosure-räknaren). Beräknas i
  // page.tsx (Server Component) så disclosuren kan visa "Filter (2)".
  activeFilterCount: number;
  // ADR 0043 — picker-träd (Län + Yrkesområde→Yrke) hämtas server-side i
  // page.tsx och passas ned. Null om träd-hämtningen misslyckades — då
  // degraderar väljarna civilt (tomma listor, sök på sökord fungerar ändå).
  taxonomy: TaxonomyTree | null;
  // Reverse-lookup-namn för redan-valda/sparade concept-id (ssyk + region).
  // conceptId → visningsnamn ("Okänd kod (<id>)" vid stale snapshot).
  resolvedLabels: ReadonlyMap<string, string>;
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
 * - Beslut A: kollaps-filteryta. Fritextsökningen (q) ligger i hero-
 *   formuläret ovanför denna form (F3 B-FIX 2026-05-19), alltid synlig
 *   ovanför resultatet (resultat-först, regel 3). Taxonomi-*filter*
 *   (Yrkesområde/Region) ligger bakom en disclosure (regel 7, undvik
 *   power-tool-täthet) — inte en alltid-expanderad panel.
 *   Sortering är INTE ett filter (det smalnar inte av resultatet, det
 *   ordnar det) → egen alltid-synlig kontroll, separerad från disclosuren
 *   (Klas produktägar-direktiv 2026-05-17, jämför Platsbankens "Sortera
 *   efter"). Beslut A låser endast *filter* bakom disclosure; sort-
 *   placeringen var ett Batch 6-implementationsval, inte ADR-brödtext.
 *   Den djupare sort-modell-frågan (5→3 Platsbanken-stil) ligger i
 *   senior-cto-advisor-underlaget (docs/reviews/2026-05-17-soktyta-*).
 * - Beslut B: ssyk/region är multi-select (chips), URL-driven (router.push
 *   med upprepade query-params).
 * - Beslut C: q-fritextsökningen ÄGS av hero-formuläret i jobb/page.tsx
 *   (F3 B-FIX, CTO-beslut Variant A 2026-05-19). Denna form äger INTE
 *   längre `q` — det fanns två levande auktoritativa q-input-ytor (hero +
 *   detta typeahead-fält) bundna till samma `q`-searchParam, vilket
 *   blockerade task-completion (ADR 0047). `q` bärs nu vidare från
 *   `initial.q` (searchParam-prop) i submit så taxonomi-/sort-ändringar
 *   inte raderar användarens hero-sökord ur URL:en.
 * - Beslut D: Relevance i sorteringen endast valbar med söktext (≥2 tecken).
 *   Härleds från `initial.q` (searchParam) — denna form har ingen lokal q.
 *
 * Submit triggar `router.push('/jobb?...')` → Server Component re-render med
 * ny searchParams. Ingen useEffect-fetch för listan (CLAUDE.md §5.2).
 * State hålls i useState (kontrollerade fält, ej stort RHF-formulär — speglar
 * codebase-konventionen för raw control utan resolver).
 */
export function JobAdFilters({
  initial,
  activeFilterCount,
  taxonomy,
  resolvedLabels,
}: JobAdFiltersProps) {
  const router = useRouter();
  const panelId = useId();
  const [isPending, startTransition] = useTransition();
  const [errors, setErrors] = useState<FieldErrors>({});
  const [open, setOpen] = useState(activeFilterCount > 0);

  const [ssyk, setSsyk] = useState<string[]>([...initial.ssyk]);
  const [region, setRegion] = useState<string[]>([...initial.region]);
  const [sortBy, setSortBy] = useState<JobAdSortBy>(initial.sortBy);

  // F3 B-FIX — q ägs av hero-formuläret (jobb/page.tsx), inte denna form.
  // Relevance-sort-gaten (ADR 0042 Beslut D, icke-förhandlingsbar invariant)
  // härleds från searchParam-värdet `initial.q`, inte lokal state: Relevance
  // får aldrig erbjudas utan en söktext ≥2 tecken (backend 400-skydd
  // ListJobAdsQueryValidator). Carry-through-värdet för q i submit/reset.
  const carriedQ = initial.q;
  const qReady = carriedQ.trim().length >= 2;

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
    // F3 B-FIX — bär vidare hero-sökordet (initial.q via carriedQ). Denna
    // form sätter/nollar inte q själv, men får inte radera ett aktivt
    // hero-sökord ur URL:en när användaren ändrar taxonomi/sortering.
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
    applyValues({ q: carriedQ, ssyk, region, sortBy });
  }

  function onReset() {
    // F3 B-FIX — Återställ gäller endast filtren denna form äger
    // (taxonomi + sortering). `q` ägs av hero-formuläret; dess reset hör
    // till hero, så vi bevarar hero-sökordet i URL:en här (carriedQ).
    setSsyk([]);
    setRegion([]);
    setSortBy("PublishedAtDesc");
    setErrors({});
    const params = new URLSearchParams();
    if (carriedQ) params.set("q", carriedQ);
    const qs = params.toString();
    startTransition(() => {
      router.push(qs.length > 0 ? `/jobb?${qs}` : "/jobb");
    });
  }

  return (
    <form
      onSubmit={onSubmit}
      className="flex flex-col gap-4 border-y border-border-default px-1 py-4.5"
      aria-label="Sök och filtrera jobbannonser"
    >
      {/* Sortering — egen alltid-synlig kontroll, ej inne i Filter-
          disclosuren. Sortering ordnar resultatet, filter smalnar av det:
          två olika saker (Klas 2026-05-17, jämför Platsbankens "Sortera
          efter"). F3 B-FIX: q-fältet (som detta block tidigare separerades
          från via border-t) ägs nu av hero-formuläret → ingen leading
          divider här, Sortering är formens första block. */}
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
            Mest relevant kan väljas när du har ett sökord på minst 2 tecken.
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
          <div id={panelId} className="flex flex-col gap-5">
            {taxonomy === null && (
              <p
                role="status"
                className="text-body-sm text-text-secondary"
              >
                Län- och yrkesval kunde inte laddas just nu. Du kan söka på
                sökord ändå och försöka igen om en stund.
              </p>
            )}
            <OccupationPicker
              occupationFields={taxonomy?.occupationFields ?? []}
              values={ssyk}
              onChange={setSsyk}
              resolvedLabels={resolvedLabels}
            />
            <RegionPicker
              regions={taxonomy?.regions ?? []}
              values={region}
              onChange={setRegion}
              resolvedLabels={resolvedLabels}
            />
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

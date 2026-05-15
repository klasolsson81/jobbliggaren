"use client";

import { useRouter } from "next/navigation";
import { useState, useTransition } from "react";
import { useForm } from "react-hook-form";
import {
  jobAdFiltersSchema,
  type JobAdFiltersValues,
  type JobAdSortBy,
} from "@/lib/dto/job-ads";
import { JOB_AD_SORT_LABELS } from "@/lib/job-ads/status";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";

interface JobAdFiltersProps {
  initial: JobAdFiltersValues;
}

const SORT_OPTIONS: ReadonlyArray<JobAdSortBy> = [
  "PublishedAtDesc",
  "PublishedAtAsc",
  "ExpiresAtDesc",
  "ExpiresAtAsc",
];

type FieldErrors = Partial<Record<keyof JobAdFiltersValues, string>>;

/**
 * URL-driven filter-form (CTO-rond 2026-05-13 Q2-A). Submit triggar
 * `router.push('/jobb?...')` — Server Component re-renders med ny
 * `searchParams`. Ingen useEffect-fetch (CLAUDE.md §5.2), ingen TanStack
 * (YAGNI för Fas 2-volym).
 *
 * Validation speglar backend `ListJobAdsQueryValidator` för defense-in-depth
 * (snabbare feedback + skyddar mot 400-rundor) — backend förblir källan.
 * Manuell `safeParse` matchar codebase-konvention (delete-account-dialog,
 * me-profile-form använder också raw RHF utan resolver).
 */
export function JobAdFilters({ initial }: JobAdFiltersProps) {
  const router = useRouter();
  const [isPending, startTransition] = useTransition();
  const [errors, setErrors] = useState<FieldErrors>({});

  const { register, handleSubmit, reset } = useForm<JobAdFiltersValues>({
    defaultValues: initial,
  });

  function onSubmit(values: JobAdFiltersValues) {
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
    if (parsed.data.ssyk) params.set("ssyk", parsed.data.ssyk);
    if (parsed.data.region) params.set("region", parsed.data.region);
    if (parsed.data.q) params.set("q", parsed.data.q);
    if (parsed.data.sortBy !== "PublishedAtDesc") {
      params.set("sortBy", parsed.data.sortBy);
    }
    // Filter-ändring nollställer pagineringen — användaren har troligen ett
    // helt annat resultat och vill se sida 1.
    const qs = params.toString();
    startTransition(() => {
      router.push(qs.length > 0 ? `/jobb?${qs}` : "/jobb");
    });
  }

  function onReset() {
    const empty: JobAdFiltersValues = {
      ssyk: "",
      region: "",
      q: "",
      sortBy: "PublishedAtDesc",
    };
    reset(empty);
    setErrors({});
    startTransition(() => {
      router.push("/jobb");
    });
  }

  return (
    <form
      onSubmit={handleSubmit(onSubmit)}
      className="flex flex-col gap-4 border-y border-border-default px-1 py-4.5"
      aria-label="Filtrera jobbannonser"
    >
      <div className="grid gap-4 md:grid-cols-2">
        <div className="flex flex-col gap-1.5">
          <label
            htmlFor="filter-q"
            className="text-label font-medium text-text-primary"
          >
            Sökord
          </label>
          <Input
            id="filter-q"
            type="search"
            inputMode="search"
            placeholder="t.ex. backend, sjuksköterska"
            aria-invalid={errors.q ? true : undefined}
            aria-describedby={errors.q ? "filter-q-error" : undefined}
            {...register("q")}
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

        <div className="flex flex-col gap-1.5">
          <label
            htmlFor="filter-sort"
            className="text-label font-medium text-text-primary"
          >
            Sortering
          </label>
          <select
            id="filter-sort"
            className="h-8 rounded-md border border-border-default bg-surface-primary px-2.5 text-body-sm text-text-primary focus:outline-2 focus:outline-offset-2 focus:outline-ring"
            {...register("sortBy")}
          >
            {SORT_OPTIONS.map((opt) => (
              <option key={opt} value={opt}>
                {JOB_AD_SORT_LABELS[opt]}
              </option>
            ))}
          </select>
        </div>

        <div className="flex flex-col gap-1.5">
          <label
            htmlFor="filter-ssyk"
            className="text-label font-medium text-text-primary"
          >
            SSYK-kod
          </label>
          <Input
            id="filter-ssyk"
            type="text"
            placeholder="JobTech occupation-concept-id"
            aria-invalid={errors.ssyk ? true : undefined}
            aria-describedby={
              errors.ssyk ? "filter-ssyk-error" : "filter-ssyk-hint"
            }
            {...register("ssyk")}
          />
          {errors.ssyk ? (
            <p
              id="filter-ssyk-error"
              role="alert"
              className="text-body-sm text-danger-700"
            >
              {errors.ssyk}
            </p>
          ) : (
            <p id="filter-ssyk-hint" className="text-body-sm text-text-secondary">
              JobTech-yrkeskod (concept-id), t.ex. MVqp_eS8_kDZ
            </p>
          )}
        </div>

        <div className="flex flex-col gap-1.5">
          <label
            htmlFor="filter-region"
            className="text-label font-medium text-text-primary"
          >
            Region
          </label>
          <Input
            id="filter-region"
            type="text"
            placeholder="JobTech location-concept-id"
            aria-invalid={errors.region ? true : undefined}
            aria-describedby={
              errors.region ? "filter-region-error" : "filter-region-hint"
            }
            {...register("region")}
          />
          {errors.region ? (
            <p
              id="filter-region-error"
              role="alert"
              className="text-body-sm text-danger-700"
            >
              {errors.region}
            </p>
          ) : (
            <p
              id="filter-region-hint"
              className="text-body-sm text-text-secondary"
            >
              JobTech-region (concept-id), t.ex. CifL_Rzy_Mku
            </p>
          )}
        </div>
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <Button type="submit" disabled={isPending}>
          {isPending ? "Söker…" : "Filtrera"}
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

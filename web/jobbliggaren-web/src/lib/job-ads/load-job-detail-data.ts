import { getJobAd } from "@/lib/api/job-ads";
import { isJobAdSaved } from "@/lib/api/saved-job-ads";
import { hasAppliedJobAd } from "@/lib/api/job-ad-status";
import { getCompanyWatchStatus } from "@/lib/api/company-follows";
import { getJobAdMatchDetail } from "@/lib/api/job-ad-match";
import { getEmployerApplicationCounts } from "@/lib/api/employer-application-counts";
import { getTaxonomyTree } from "@/lib/api/taxonomy";
import {
  buildOrtGranularityMap,
  type OrtGranularity,
} from "@/lib/job-ads/ort-granularity";
import type { JobAdDetailDto } from "@/lib/dto/job-ads";
import type { JobAdMatchDetail } from "@/lib/dto/job-ad-match";
import type { CompanyFollowState } from "@/lib/dto/company-follows";

/**
 * The fully-loaded data an ad-detail surface needs. Both the full page
 * (`/jobb/[id]`) and the intercepting modal (`@modal/(.)jobb/[id]`) render the
 * SAME `JobAdDetail` with the SAME data (ADR 0053 — one presentation component,
 * two contexts), so the fetch orchestration lives here rather than being copied
 * into two route files that could silently diverge (#596).
 */
export interface JobDetailData {
  jobAd: JobAdDetailDto;
  initialSaved: boolean;
  initialApplied: boolean;
  followState: CompanyFollowState;
  match: JobAdMatchDetail | null;
  /** Positive-only: `undefined` when the caller has no prior application at this employer. */
  previousApplicationCount: number | undefined;
  /** Built only when there is a match (the granularity map is match-gated); `undefined` otherwise. */
  ortGranularityByLabel: Record<string, OrtGranularity> | undefined;
}

/**
 * Discriminated result. `ok` carries the loaded data bundle; every other kind
 * mirrors `getJobAd`'s `ApiResult` so the callers keep their exhaustive switch
 * (unauthorized → login, notFound → 404, rateLimited → civil box, …).
 */
export type JobDetailLoad =
  | ({ kind: "ok" } & JobDetailData)
  | { kind: "unauthorized" }
  | { kind: "forbidden" }
  | { kind: "notFound" }
  | { kind: "rateLimited"; retryAfterSeconds: number }
  | { kind: "error" };

/**
 * Load everything an ad-detail surface renders for `id`. On a non-`ok` ad the
 * discriminated non-`ok` kind is returned unchanged for the caller to map.
 *
 * `includeRelated` (#300 PR-5) grades a related-occupation ad as `Related`
 * instead of ungraded; it is threaded from the caller's `?relaterade=on`.
 */
export async function loadJobDetailData(
  id: string,
  includeRelated: boolean,
): Promise<JobDetailLoad> {
  const result = await getJobAd(id);
  if (result.kind !== "ok") return result;

  // F6 P5 Punkt 2 PR5 / F4-16 — Spara + Har-ansökt + följ-state + matchnings-
  // detalj + arbetsgivar-räknare parallellt (ingen inbördes waterfall). Alla
  // degraderar civilt (false/null/tomt) vid fel.
  const [initialSaved, initialApplied, followState, match, employerCounts] =
    await Promise.all([
      isJobAdSaved(id),
      hasAppliedJobAd(id),
      getCompanyWatchStatus(id),
      getJobAdMatchDetail(id, includeRelated),
      // #593 (#446-uppföljning) — the caller's prior-application count for THIS
      // ad's employer. Positive-only; anon/error → empty.
      getEmployerApplicationCounts([id]),
    ]);
  // Positive-only map: an absent key means zero (no history line is rendered).
  const previousApplicationCount = employerCounts.countsByJobAdId[id];

  // Spår 3 PR-D — taxonomin behövs BARA när det finns en match (annars byggs
  // ingen granularitets-karta). Cachad 1h (statisk referensdata) så en match-
  // användare betalar en varm cache-läsning; kartan byggs FE-side (architect
  // NOTE-2), taxonomi-fel → null → generisk bevisform.
  const taxonomyResult = match != null ? await getTaxonomyTree() : null;
  const ortGranularityByLabel =
    match != null
      ? buildOrtGranularityMap(
          taxonomyResult?.kind === "ok" ? taxonomyResult.data : null,
        )
      : undefined;

  return {
    kind: "ok",
    jobAd: result.data,
    initialSaved,
    initialApplied,
    followState,
    match,
    previousApplicationCount,
    ortGranularityByLabel,
  };
}

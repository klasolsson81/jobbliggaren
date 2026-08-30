import { MAX_CONCEPT_IDS } from "@/lib/dto/job-ads";
import { WATCH_MATCHING_GRADES } from "@/lib/dto/job-ad-match";
import { DEFAULT_SORT_BY, buildJobbHref } from "./search-params";

/** Which of a watched company's ads the link should show. */
export type CompanyJobsScope = "all" | "matching";

/**
 * Whether an org.nr can be carried by the `?employer=` axis at all — the same shape
 * `parseEmployerParam` requires on the read path, which drops a mismatch SILENTLY.
 *
 * Exported so the row's link gate and this builder read ONE value. Before it, the row asked
 * "is the field non-null" while the builder asked "is it ten digits", and a row could have
 * ended up with a count, no link and no explanation. That state is contract-impossible today
 * (`OrganizationNumber.Create` enforces the same shape, and the one other on-wire form is an
 * HMAC token the handler masks to null) — which is exactly why closing it costs nothing.
 */
export function isLinkableOrgNr(organizationNumber: string): boolean {
  return /^\d{10}$/.test(organizationNumber);
}

/**
 * Builds the `/jobb` href that shows the ads of one or more watched companies (#1547).
 * Sibling of {@link buildRecentSearchHref} — one source of truth for "watched companies →
 * /jobb URL", so no link can drift from another or from the count beside it. Two callers:
 * the watch row passes a single org.nr, Översikt's summary passes the whole watch set.
 *
 * This is the ONLY originator of an `?employer=` value in the app. `search-params.ts`
 * recorded (2026-08-19) that there were none since `company-lookup.tsx` was deleted in
 * `aca39970`, which is why the `IsProtectedIdentity` gate it describes was guarding an
 * empty set. The gate is live again, and it lives at the CALLER: this function takes a
 * plain org.nr and cannot tell a masked one from a legal-entity one.
 *
 * `scope: "matching"` carries the grade subset, never `?baraMatchade=on`. The two are not
 * interchangeable: `baraMatchade` maps to `onlyMatched`, which
 * `ListJobAdsQueryHandler.cs:122-125` expands to the whole filterable band
 * `[Basic, Related, Good, Strong]` — WIDER than the `[Good, Strong]` the row's count is
 * computed at, so a user clicking "9 matchande" would land on more than nine. The deleted
 * `company-lookup.tsx` linked exactly that way; this half of the precedent is not revived.
 *
 * Residual, and it is the same staleness every count on the page has: the caller gates the
 * matching link on `matchingAdCount !== null`, which is a SERVER-rendered answer to "has
 * this user stated an occupation". Clear the occupations in another tab and the backend
 * ignores the grade subset entirely (`ListJobAdsQueryHandler.cs:110`) and answers with the
 * unfiltered employer list. Not closable without a client round-trip.
 *
 * Every other axis is deliberately empty: no ort, no yrke, no Klass-2 dimension, default
 * sort, and specifically no `matchning=off` — that one would filter the list while hiding
 * every visual trace of the filter (the grade chips render only when matching is active).
 */
export function buildCompanyJobsHref(
  organizationNumbers: ReadonlyArray<string>,
  scope: CompanyJobsScope
): string | null {
  // The producer keeps its own floor even though both callers share the predicate: a
  // second line of defence at the seam that emits the value, which is where `security-auditor`
  // asked for it. Deliberately NOT a personnummer discriminator — that would give
  // `IsPersonnummerShaped` a second home, which the house rejected once (#844).
  // Every value or none: a partial link would show fewer ads than the number beside it
  // promises, which is the divergence this whole route exists to avoid.
  if (organizationNumbers.length === 0) return null;
  if (!organizationNumbers.every(isLinkableOrgNr)) return null;

  // Over the backend cap the list query 400s and the link lands on the technical-error card, so
  // no link is the honest answer. Deliberately NOT truncated to the cap: the doctrine above is
  // every value or none, and a truncated link shows fewer ads than the number beside it promises
  // -- the exact divergence this route exists to avoid.
  if (organizationNumbers.length > MAX_CONCEPT_IDS) return null;

  return buildJobbHref({
    q: "",
    occupationGroup: [],
    region: [],
    municipality: [],
    employmentType: [],
    worktimeExtent: [],
    matchGrades: scope === "matching" ? WATCH_MATCHING_GRADES : [],
    remote: false,
    employer: organizationNumbers,
    sortBy: DEFAULT_SORT_BY,
  });
}

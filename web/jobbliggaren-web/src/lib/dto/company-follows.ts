import { z } from "zod";

/**
 * #311 #455 (ADR 0087 D8(c)) — per-ad follow-state overlay. Deliberately carries NO org.nr: the FE
 * needs only whether the ad's employer is followable and the opaque CompanyWatchId to unfollow. The raw
 * org.nr never leaves the backend (a sole-prop org.nr can be a personnummer).
 */
export const companyWatchStatusSchema = z.object({
  jobAdId: z.string(),
  companyWatchId: z.string().nullable(),
  followable: z.boolean(),
});
export type CompanyWatchStatus = z.infer<typeof companyWatchStatusSchema>;

export const companyWatchStatusBatchSchema = z.object({
  statuses: z.array(companyWatchStatusSchema).default([]),
});
export type CompanyWatchStatusBatch = z.infer<typeof companyWatchStatusBatchSchema>;

/**
 * #560 PR-C (ADR 0087 D8(c)) — the ORG.NR-keyed follow-state overlay for /foretag/sok. The backend
 * response is POSITIONAL: `statuses[i]` is the follow-state of the request's org.nr `i`, in the same
 * order, with no dedup. Like the jobAdId-keyed schema above it carries NO org.nr — the caller supplied
 * the org.nrs, so the response only needs the opaque `companyWatchId` (null = not followed). The FE zips
 * the array back to its request list by index.
 */
export const orgNrFollowStatusSchema = z.object({
  companyWatchId: z.string().nullable(),
});
export type OrgNrFollowStatus = z.infer<typeof orgNrFollowStatusSchema>;

export const companyWatchStatusByOrgNrBatchSchema = z.object({
  statuses: z.array(orgNrFollowStatusSchema).default([]),
});

/** POST /me/company-watches/by-job-ad/{id} returns the created/resurrected CompanyWatchId. */
export const followCompanyResultSchema = z.object({ id: z.string() });

/**
 * The single-ad follow-state the detail footer toggle needs. `companyWatchId` is null when the user
 * does not follow this employer; `followable` is false when the ad carries no employer org.nr (B2).
 */
export interface CompanyFollowState {
  companyWatchId: string | null;
  followable: boolean;
}

/**
 * Bevakning F4b — the per-watch notification filter (mirrors backend `WatchFilterDto`).
 *
 * The two geo axes are DISJOINT JobTech namespaces and are stored as picked: a whole-län selection
 * lives in `regions` and is never expanded into its municipalities, because an ad may be tagged at län
 * granularity with no municipality at all (see `WatchFilterSpec` — expanding would silently drop those
 * ads from the user's notifications). Labels are resolved from the taxonomy tree the ort picker already
 * holds; the wire carries only concept-ids. No org.nr, no grade value.
 */
export const watchFilterSchema = z.object({
  municipalities: z.array(z.string()).readonly(),
  regions: z.array(z.string()).readonly(),
  onlyMatched: z.boolean(),
});
export type WatchFilter = z.infer<typeof watchFilterSchema>;

/**
 * #311 #448 (ADR 0087 D2/D3/D8(c)) — one owner-facing followed-company row for the `/foretag` list.
 * Mirrors the backend `CompanyWatchDto`. The backend already applies the personnummer guard: a
 * sole-prop (personnummer-shaped) org.nr arrives with `organizationNumber: null` and
 * `isProtectedIdentity: true` — the raw value NEVER crosses the wire. The FE identifies the watch by
 * `companyName` (resolved server-side from public Platsbanken data) and renders org.nr only when it
 * is present (a legal-entity number). `activeAdCount` is a public open-role count (#447), carries no
 * PII, and is surfaced even when the org.nr is masked.
 *
 * `matchingAdCount` (#452, mirrors backend `CompanyWatchDto.MatchingAdCount`) is how many of the
 * employer's active ads match this user's profile at grade >= Good, computed at READ by the same
 * shared grade expression /jobb uses (never diverges from /jobb). It is a count of ADS over a named
 * grade threshold, never an opaque match score (Goodhart, ADR 0071). Nullable = honest not-assessed:
 * `null` when the user has stated no occupation (empty SSYK profile). The FE renders a nudge for
 * `null`, never "0" (parity /jobb + `GetMyMatchCount`); a non-null value (incl. `0`) means assessed.
 */
export const companyWatchSchema = z.object({
  id: z.string(),
  organizationNumber: z.string().nullable(),
  isProtectedIdentity: z.boolean(),
  companyName: z.string().nullable(),
  followedAt: z.string(),
  activeAdCount: z.number().int().nonnegative(),
  matchingAdCount: z.number().int().nullable(),
  // null = no filter (the domain's canonical representation — there is no separate hasFilter bool to
  // disagree with it). Required key: backend always projects it, and `undefined` would mask contract
  // drift by silently rendering every watch as unfiltered — which is exactly the silent-narrowing
  // failure the resting-state disclosure exists to prevent.
  filter: watchFilterSchema.nullable(),
});
export type CompanyWatch = z.infer<typeof companyWatchSchema>;

/** `GET /me/company-watches` returns a bare array (no pagination — the watch set is user-bounded). */
export const listCompanyWatchesResultSchema = z.array(companyWatchSchema);
export type ListCompanyWatchesResult = z.infer<typeof listCompanyWatchesResultSchema>;

/**
 * Bevakning F2 (#801, RF-6=6B) — the count of new ads from followed employers NEW since the user last
 * visited /foretag (mirrors backend `NewFollowedCompanyAdCountDto`). A bare count — carries NO org.nr
 * and no company name (D8; the Översikt row is a generic ledger row "från bevakade företag").
 * `count === 0` is an honest answer (no active follows, or nothing new since the last visit) — the
 * row renders "0", never a mock number. Drives the Översikt "Nya annonser från bevakade företag"-row.
 */
export const newFollowedCompanyAdCountSchema = z.object({
  count: z.number().int().nonnegative(),
});
export type NewFollowedCompanyAdCount = z.infer<typeof newFollowedCompanyAdCountSchema>;

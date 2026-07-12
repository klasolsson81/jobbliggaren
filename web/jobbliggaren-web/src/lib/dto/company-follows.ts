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

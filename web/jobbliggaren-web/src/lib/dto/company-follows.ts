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

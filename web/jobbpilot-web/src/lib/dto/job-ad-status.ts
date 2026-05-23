import { z } from "zod";

/**
 * ADR 0063 — batch-status-response för per-user-overlay på `/jobb`-listan.
 * Två set:n av JobAdId-IDs som FE lookup:ar O(1) per kort.
 */
export const jobAdStatusBatchSchema = z.object({
  savedIds: z.array(z.string()).default([]),
  appliedIds: z.array(z.string()).default([]),
});
export type JobAdStatusBatch = z.infer<typeof jobAdStatusBatchSchema>;

/** Single has-applied-svar för modal-yta (paritet isJobAdSaved). */
export const hasAppliedSchema = z.object({
  hasApplied: z.boolean(),
});
export type HasAppliedResult = z.infer<typeof hasAppliedSchema>;

import { z } from "zod";
import { jobAdStatusSchema, jobSourceSchema } from "./job-ads";

/**
 * F6 P5 Punkt 2 Del A — Zod-mirror av backend `SavedJobAdDto`
 * (`Jobbliggaren.Application.SavedJobAds.Queries`). ADR 0020 single-source.
 *
 * `jobAd` är nullable (ADR 0048 Beslut c — annons soft-deletad → null
 * via JobAd query filter). UI ska rendera "Annonsen är borttagen" eller
 * filtrera bort raden.
 *
 * JobAdSummaryDto-fält speglar backend `JobAdSummaryDto`-record
 * (`Jobbliggaren.Application.Applications.Queries`) — paritet `applications.ts`-
 * mirror men inte återanvänd direkt eftersom den dtot är `ApplicationDto`-
 * intern och kan ändra shape oberoende av denna konsumtion.
 */
export const savedJobAdJobAdSummarySchema = z.object({
  jobAdId: z.string().nullable(),
  title: z.string(),
  company: z.string(),
  url: z.string().nullable(),
  source: jobSourceSchema,
  publishedAt: z.string().nullable(),
  expiresAt: z.string().nullable(),
  // Status finns INTE på JobAdSummaryDto (det är en summary, inte detail).
  // Behåll schema-paritet med backend record — utöka vid backend-ändring.
});
export type SavedJobAdJobAdSummary = z.infer<typeof savedJobAdJobAdSummarySchema>;

export const savedJobAdDtoSchema = z.object({
  id: z.string(),
  jobAdId: z.string(),
  savedAt: z.string(),
  jobAd: savedJobAdJobAdSummarySchema.nullable(),
});
export type SavedJobAdDto = z.infer<typeof savedJobAdDtoSchema>;

export const listSavedJobAdsResultSchema = z.array(savedJobAdDtoSchema);
export type ListSavedJobAdsResult = z.infer<typeof listSavedJobAdsResultSchema>;

// Re-export schemas som används av Status-presentation (om FE behöver kolla)
export { jobAdStatusSchema };

import { z } from "zod";
import { applicationStatusSchema } from "./applications";

// #313 — application-statistics read model (BUILD.md §6.2: avslags-analys,
// pipeline-konvertering). Mirrors the backend ApplicationStatsDto. Every rate is
// a (numerator, denominator, percent) triple so the FE labels it truthfully and
// never shows a percentage without its base (§5 — a number is never opaque).

export const statusCountDtoSchema = z.object({
  status: applicationStatusSchema,
  count: z.number().int(),
});
export type StatusCountDto = z.infer<typeof statusCountDtoSchema>;

export const applicationRateDtoSchema = z.object({
  numerator: z.number().int(),
  denominator: z.number().int(),
  percent: z.number().int(),
});
export type ApplicationRateDto = z.infer<typeof applicationRateDtoSchema>;

// Stable funnel-stage contract keys (the backend's ApplicationStatsCalculator
// Stage* constants); the FE maps each to a Swedish label.
export const funnelStageKeySchema = z.enum([
  "Sent",
  "Responded",
  "Interview",
  "Offer",
  "Accepted",
]);
export type FunnelStageKey = z.infer<typeof funnelStageKeySchema>;

export const funnelStageDtoSchema = z.object({
  stage: funnelStageKeySchema,
  count: z.number().int(),
  percentOfSent: z.number().int(),
});
export type FunnelStageDto = z.infer<typeof funnelStageDtoSchema>;

export const monthlyApplicationCountDtoSchema = z.object({
  year: z.number().int(),
  month: z.number().int(),
  count: z.number().int(),
});
export type MonthlyApplicationCountDto = z.infer<
  typeof monthlyApplicationCountDtoSchema
>;

export const applicationStatsDtoSchema = z.object({
  totalApplications: z.number().int(),
  totalSent: z.number().int(),
  statusCounts: z.array(statusCountDtoSchema),
  responseRate: applicationRateDtoSchema,
  interviewRate: applicationRateDtoSchema,
  rejectionRate: applicationRateDtoSchema,
  funnel: z.array(funnelStageDtoSchema),
  // Count of sent applications that exited off the success funnel
  // (Rejected/Withdrawn/Ghosted). When > 0 the funnel may under-count mid-funnel
  // reach (the aggregate keeps no stage history) — the FE shows a footnote rather
  // than mis-report (§5).
  offFunnelExitCount: z.number().int(),
  monthlyApplications: z.array(monthlyApplicationCountDtoSchema),
});
export type ApplicationStatsDto = z.infer<typeof applicationStatsDtoSchema>;

import { z } from "zod";
import { pagedResult } from "./_helpers";

export const applicationStatusSchema = z.enum([
  "Draft",
  "Submitted",
  "Acknowledged",
  "InterviewScheduled",
  "Interviewing",
  "OfferReceived",
  "Accepted",
  "Rejected",
  "Withdrawn",
  "Ghosted",
]);
export type ApplicationStatus = z.infer<typeof applicationStatusSchema>;

export const followUpChannelSchema = z.enum([
  "Email",
  "LinkedIn",
  "Phone",
  "Other",
]);
export type FollowUpChannel = z.infer<typeof followUpChannelSchema>;

export const followUpOutcomeSchema = z.enum([
  "Pending",
  "Responded",
  "NoResponse",
]);
export type FollowUpOutcome = z.infer<typeof followUpOutcomeSchema>;

// ADR 0048 — jobb-metadata-sammanfattning projicerad i read-vägen.
// Källa: JobAd (kopplad ansökan) eller Application.ManualPosting (manuell).
// jobAdId null när källan är ManualPosting. publishedAt null för manuell (J1).
export const jobAdSummaryDtoSchema = z.object({
  jobAdId: z.string().nullable(),
  title: z.string(),
  company: z.string(),
  url: z.string().nullable(),
  source: z.string(),
  publishedAt: z.string().nullable(),
  expiresAt: z.string().nullable(),
});
export type JobAdSummaryDto = z.infer<typeof jobAdSummaryDtoSchema>;

export const applicationDtoSchema = z.object({
  id: z.string(),
  jobSeekerId: z.string(),
  jobAdId: z.string().nullable(),
  status: applicationStatusSchema,
  createdAt: z.string(),
  updatedAt: z.string(),
  // nullable + optional: backend skickar alltid jobAd (null|objekt), men
  // optional ger deploy-skew-resiliens (cachead/äldre svar utan fältet
  // kraschar ej parse — architect §6 deploy-säkerhets-intent).
  jobAd: jobAdSummaryDtoSchema.nullable().optional(),
});
export type ApplicationDto = z.infer<typeof applicationDtoSchema>;

export const followUpDtoSchema = z.object({
  id: z.string(),
  channel: followUpChannelSchema,
  scheduledAt: z.string(),
  note: z.string().nullable(),
  outcome: followUpOutcomeSchema,
  outcomeAt: z.string().nullable(),
  createdAt: z.string(),
});
export type FollowUpDto = z.infer<typeof followUpDtoSchema>;

export const noteDtoSchema = z.object({
  id: z.string(),
  content: z.string().nullable(),
  createdAt: z.string(),
});
export type NoteDto = z.infer<typeof noteDtoSchema>;

export const applicationDetailDtoSchema = applicationDtoSchema.extend({
  coverLetter: z.string().nullable(),
  followUps: z.array(followUpDtoSchema),
  notes: z.array(noteDtoSchema),
});
export type ApplicationDetailDto = z.infer<typeof applicationDetailDtoSchema>;

export const pipelineGroupDtoSchema = z.object({
  status: applicationStatusSchema,
  count: z.number().int().nonnegative(),
  applications: z.array(applicationDtoSchema),
});
export type PipelineGroupDto = z.infer<typeof pipelineGroupDtoSchema>;

export const pipelineResponseSchema = z.array(pipelineGroupDtoSchema);

export const getApplicationsResultSchema = pagedResult(applicationDtoSchema);
export type GetApplicationsResult = z.infer<typeof getApplicationsResultSchema>;

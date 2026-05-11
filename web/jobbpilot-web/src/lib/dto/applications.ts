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
  "Positive",
  "Negative",
  "Neutral",
]);
export type FollowUpOutcome = z.infer<typeof followUpOutcomeSchema>;

export const applicationDtoSchema = z.object({
  id: z.string(),
  jobSeekerId: z.string(),
  jobAdId: z.string().nullable(),
  status: applicationStatusSchema,
  createdAt: z.string(),
  updatedAt: z.string(),
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

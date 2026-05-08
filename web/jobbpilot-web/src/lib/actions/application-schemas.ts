import { z } from "zod";
import type { ApplicationStatus } from "@/lib/types/applications";

const GUID_REGEX = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

const APPLICATION_STATUSES = [
  "Draft", "Submitted", "Acknowledged", "InterviewScheduled",
  "Interviewing", "OfferReceived", "Accepted", "Rejected", "Withdrawn", "Ghosted",
] as const satisfies readonly ApplicationStatus[];

export const createApplicationSchema = z.object({
  coverLetter: z.string().max(5000, "Personligt brev får vara max 5 000 tecken.").optional(),
});

export const transitionStatusSchema = z.object({
  applicationId: z.string().regex(GUID_REGEX, "Ogiltigt ansöknings-ID."),
  targetStatus: z.enum(APPLICATION_STATUSES, { error: "Ogiltig status." }),
});

export const addFollowUpSchema = z.object({
  applicationId: z.string().regex(GUID_REGEX, "Ogiltigt ansöknings-ID."),
  channel: z.enum(["Email", "LinkedIn", "Phone", "Other"], {
    error: "Ogiltig kanal.",
  }),
  scheduledAt: z
    .string()
    .min(1, "Datum krävs.")
    .refine((v) => !isNaN(Date.parse(v)), "Ogiltigt datum."),
  note: z.string().max(1000, "Anteckning får vara max 1 000 tecken.").optional(),
});

export const addNoteSchema = z.object({
  applicationId: z.string().regex(GUID_REGEX, "Ogiltigt ansöknings-ID."),
  content: z
    .string()
    .min(1, "Notering får inte vara tom.")
    .max(5000, "Notering får vara max 5 000 tecken."),
});

export type CreateApplicationInput = z.infer<typeof createApplicationSchema>;
export type TransitionStatusInput = z.infer<typeof transitionStatusSchema>;
export type AddFollowUpInput = z.infer<typeof addFollowUpSchema>;
export type AddNoteInput = z.infer<typeof addNoteSchema>;

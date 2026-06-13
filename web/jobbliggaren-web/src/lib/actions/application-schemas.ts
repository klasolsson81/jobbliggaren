import { z } from "zod";
import type { ApplicationStatus } from "@/lib/types/applications";
import { GUID_REGEX } from "@/lib/validation/guid";

const APPLICATION_STATUSES = [
  "Draft", "Submitted", "Acknowledged", "InterviewScheduled",
  "Interviewing", "OfferReceived", "Accepted", "Rejected", "Withdrawn", "Ghosted",
] as const satisfies readonly ApplicationStatus[];

// Manuell ansökan (jobAdId == null): Jobbtitel + Företag obligatoriska,
// Annonslänk + Sista ansökningsdag frivilliga. Inget Källa-fält (Source
// struken — manuell ansökan är implicit Source=Manual, projiceras i
// read-vägen). coverLetter fortsatt frivillig.
export const createApplicationSchema = z.object({
  title: z
    .string()
    .trim()
    .min(1, "Jobbtitel krävs.")
    .max(200, "Jobbtitel får vara max 200 tecken."),
  company: z
    .string()
    .trim()
    .min(1, "Företag krävs.")
    .max(200, "Företag får vara max 200 tecken."),
  url: z
    .union([
      z
        .string()
        .trim()
        .url("Annonslänken måste vara en giltig webbadress.")
        .refine(
          (v) => v.startsWith("http://") || v.startsWith("https://"),
          "Annonslänken måste börja med http:// eller https://."
        ),
      z.literal("").transform(() => undefined),
    ])
    .optional(),
  expiresAt: z
    .union([
      z
        .string()
        .trim()
        .refine((v) => !isNaN(Date.parse(v)), "Ogiltigt datum."),
      z.literal("").transform(() => undefined),
    ])
    .optional(),
  coverLetter: z
    .string()
    .max(5000, "Personligt brev får vara max 5 000 tecken.")
    .optional(),
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

export const recordFollowUpOutcomeSchema = z.object({
  applicationId: z.string().regex(GUID_REGEX, "Ogiltigt ansöknings-ID."),
  followUpId: z.string().regex(GUID_REGEX, "Ogiltigt uppföljnings-ID."),
  outcome: z.enum(["Responded", "NoResponse"], { error: "Ogiltigt utfall." }),
});

export type CreateApplicationInput = z.infer<typeof createApplicationSchema>;
export type TransitionStatusInput = z.infer<typeof transitionStatusSchema>;
export type AddFollowUpInput = z.infer<typeof addFollowUpSchema>;
export type AddNoteInput = z.infer<typeof addNoteSchema>;
export type RecordFollowUpOutcomeInput = z.infer<typeof recordFollowUpOutcomeSchema>;

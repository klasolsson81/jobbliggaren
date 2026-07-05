import { z } from "zod";
import type { useTranslations } from "next-intl";
import type { ApplicationStatus } from "@/lib/types/applications";
import { GUID_REGEX } from "@/lib/validation/guid";

// next-intl translator scoped to the `validation` namespace. The same call
// signature works for both client (`useTranslations("validation")`) and server
// (`getTranslations("validation")`); callers acquire `t` and build the schema
// via the `make*`-factories below. The Swedish values live in
// `messages/sv/validation.json` (source of truth, typed via AppConfig).
export type ValidationTranslator = ReturnType<typeof useTranslations<"validation">>;

const APPLICATION_STATUSES = [
  "Draft", "Submitted", "Acknowledged", "InterviewScheduled",
  "Interviewing", "OfferReceived", "Accepted", "Rejected", "Withdrawn", "Ghosted",
] as const satisfies readonly ApplicationStatus[];

// Manuell ansökan (jobAdId == null): Jobbtitel + Företag obligatoriska,
// Annonslänk + Sista ansökningsdag frivilliga. Inget Källa-fält (Source
// struken — manuell ansökan är implicit Source=Manual, projiceras i
// read-vägen). coverLetter fortsatt frivillig.
export function makeCreateApplicationSchema(t: ValidationTranslator) {
  return z.object({
    title: z
      .string()
      .trim()
      .min(1, t("application.titleRequired"))
      .max(200, t("application.titleMax")),
    company: z
      .string()
      .trim()
      .min(1, t("application.companyRequired"))
      .max(200, t("application.companyMax")),
    url: z
      .union([
        z
          .string()
          .trim()
          .url(t("application.urlInvalid"))
          .refine(
            (v) => v.startsWith("http://") || v.startsWith("https://"),
            t("application.urlScheme")
          ),
        z.literal("").transform(() => undefined),
      ])
      .optional(),
    expiresAt: z
      .union([
        z
          .string()
          .trim()
          .refine((v) => !isNaN(Date.parse(v)), t("application.dateInvalid")),
        z.literal("").transform(() => undefined),
      ])
      .optional(),
    coverLetter: z
      .string()
      .max(5000, t("application.coverLetterMax"))
      .optional(),
  });
}

export function makeTransitionStatusSchema(t: ValidationTranslator) {
  return z.object({
    applicationId: z
      .string()
      .regex(GUID_REGEX, t("application.applicationIdInvalid")),
    targetStatus: z.enum(APPLICATION_STATUSES, {
      error: t("application.statusInvalid"),
    }),
  });
}

export function makeAddFollowUpSchema(t: ValidationTranslator) {
  return z.object({
    applicationId: z
      .string()
      .regex(GUID_REGEX, t("application.applicationIdInvalid")),
    channel: z.enum(["Email", "LinkedIn", "Phone", "Other"], {
      error: t("application.channelInvalid"),
    }),
    scheduledAt: z
      .string()
      .min(1, t("application.dateRequired"))
      .refine((v) => !isNaN(Date.parse(v)), t("application.dateInvalid")),
    note: z
      .string()
      .max(1000, t("application.noteMax"))
      .optional(),
  });
}

export function makeAddNoteSchema(t: ValidationTranslator) {
  return z.object({
    applicationId: z
      .string()
      .regex(GUID_REGEX, t("application.applicationIdInvalid")),
    content: z
      .string()
      .min(1, t("application.noteContentRequired"))
      .max(5000, t("application.noteContentMax")),
  });
}

// "Logga uppföljning" (#630 PR 7, design §9 / ADR 0092 D5): en redan UTFÖRD
// kontakt loggad med dagens datum (backend stämplar tidpunkten; outcome=Logged).
// Bara den frivilliga noteringen valideras — max 2000 speglar backend-
// LogFollowUpCommandValidator (skiljer sig medvetet från schemalagda
// uppföljningens 1000).
export function makeLogFollowUpSchema(t: ValidationTranslator) {
  return z.object({
    applicationId: z
      .string()
      .regex(GUID_REGEX, t("application.applicationIdInvalid")),
    note: z
      .string()
      .max(2000, t("application.noteMax"))
      .optional(),
  });
}

export function makeRecordFollowUpOutcomeSchema(t: ValidationTranslator) {
  return z.object({
    applicationId: z
      .string()
      .regex(GUID_REGEX, t("application.applicationIdInvalid")),
    followUpId: z
      .string()
      .regex(GUID_REGEX, t("application.followUpIdInvalid")),
    outcome: z.enum(["Responded", "NoResponse"], {
      error: t("application.outcomeInvalid"),
    }),
  });
}

export type CreateApplicationInput = z.infer<
  ReturnType<typeof makeCreateApplicationSchema>
>;
export type TransitionStatusInput = z.infer<
  ReturnType<typeof makeTransitionStatusSchema>
>;
export type AddFollowUpInput = z.infer<
  ReturnType<typeof makeAddFollowUpSchema>
>;
export type AddNoteInput = z.infer<ReturnType<typeof makeAddNoteSchema>>;
export type LogFollowUpInput = z.infer<
  ReturnType<typeof makeLogFollowUpSchema>
>;
export type RecordFollowUpOutcomeInput = z.infer<
  ReturnType<typeof makeRecordFollowUpOutcomeSchema>
>;

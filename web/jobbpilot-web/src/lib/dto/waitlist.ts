import { z } from "zod";

/**
 * Backend-svar från `POST /api/v1/waitlist` vid lyckad signup.
 * Per ADR 0005 amendment 2026-05-12 (invitations + waitlist).
 */
export const waitlistEntryResponseSchema = z.object({
  waitlistEntryId: z.string().uuid(),
  email: z.string(),
});

export type WaitlistEntryResponse = z.infer<typeof waitlistEntryResponseSchema>;

/**
 * Form-input för väntelista-signup. Validerings-regler speglar
 * backend-domain (`WaitlistEntry.Request`-invariants).
 *
 * Användarvillkor + nödvändiga cookies levereras under GDPR Art. 6(1)(b)
 * "performance of contract" (submit = acceptance) — ingen separat checkbox.
 * Endast `marketingEmailAccepted` är genuint Art. 7-samtycke (opt-in,
 * default `false`). CTO-dom 2026-05-24 Fynd 1 Approach B.
 */
export const waitlistFormSchema = z.object({
  name: z
    .string()
    .trim()
    .min(1, "Namn krävs.")
    .max(100, "Namn får vara max 100 tecken."),
  email: z
    .string()
    .trim()
    .min(1, "E-postadress krävs.")
    .max(254, "E-postadress får vara max 254 tecken.")
    .email("E-postadressen är inte giltig."),
  motivation: z
    .string()
    .trim()
    .min(10, "Motiveringen ska vara minst 10 tecken.")
    .max(1000, "Motiveringen får vara max 1000 tecken."),
  marketingEmailAccepted: z.boolean(),
});

export type WaitlistFormInput = z.infer<typeof waitlistFormSchema>;

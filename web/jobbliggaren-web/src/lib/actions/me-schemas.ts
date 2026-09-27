import { z } from "zod";
import type { useTranslations } from "next-intl";
import { challengeIdInputSchema, codeInputSchema } from "@/lib/auth/challenge-schemas";
import { digestCadenceSchema } from "@/lib/dto/me";

// next-intl translator scoped to the `validation` namespace (see
// `application-schemas.ts` for the shared rationale). Callers build the schema
// via the `make*`-factories; Swedish messages live in
// `messages/sv/validation.json`.
export type ValidationTranslator = ReturnType<typeof useTranslations<"validation">>;

/**
 * #1740 — the typed address of delete-account. Friction against the user's own mistake, never proof of
 * identity (the code is), so it carries no format rule at all: `z.email()` refused addresses the backend
 * admits (`björn@…`, #1781) and locked those accounts out of deleting themselves. The action compares it
 * with the SESSION's address before anything is spent (#822).
 */
export const deleteConfirmationSchema = z.string().trim().min(1).max(256);

/** A code and the challenge it answers, as a Server Action receives them from the dialog. */
export const codeProofSchema = z.object({
  challengeId: challengeIdInputSchema,
  code: codeInputSchema,
});

// The language is the only field /mina-sidor writes through this action, so it is required: a
// payload without it would PATCH nothing and the card would still stamp "Sparat".
export function makeUpdateMyProfileSchema(t: ValidationTranslator) {
  return z.object({
    language: z.enum(["sv", "en"], {
      message: t("profile.languageInvalid"),
    }),
    // TD-115: legacy emailNotifications/weeklySummary retired (gated no email path).
  });
}

export type UpdateMyProfileInput = z.infer<
  ReturnType<typeof makeUpdateMyProfileSchema>
>;

// The three notification-settings schemas are strict: a Server Action ID can outlive its build,
// so a tab on an earlier build may still post the combined `{ enabled, cadence }` body here. Refusing
// the unknown key keeps that body from being cut down to a consent write. Structural protection
// (the backend is the last barrier), so no user-facing validation text: the action answers with its
// own copy, and the translator is taken for factory consistency.

/** ADR 0080 Vag 4 PR-6 — `updateNotificationConsentAction`: the background-match consent alone. */
export function makeUpdateNotificationConsentSchema(_t: ValidationTranslator) {
  return z.strictObject({
    enabled: z.boolean(),
  });
}

export type UpdateNotificationConsentInput = z.infer<
  ReturnType<typeof makeUpdateNotificationConsentSchema>
>;

/** Bevakning F4 (#803) — `updateFollowedCompanyNotificationConsentAction`: that consent alone. */
export function makeUpdateFollowedCompanyNotificationConsentSchema(
  _t: ValidationTranslator
) {
  return z.strictObject({
    enabled: z.boolean(),
  });
}

export type UpdateFollowedCompanyNotificationConsentInput = z.infer<
  ReturnType<typeof makeUpdateFollowedCompanyNotificationConsentSchema>
>;

/**
 * ADR 0087 D2 — `updateDigestCadenceAction`: the cadence the two consents share, carrying no
 * consent value. Binds against the `DigestCadence` mirror (wire values `Daily`/`Weekly`).
 */
export function makeUpdateDigestCadenceSchema(_t: ValidationTranslator) {
  return z.strictObject({
    cadence: digestCadenceSchema,
  });
}

export type UpdateDigestCadenceInput = z.infer<
  ReturnType<typeof makeUpdateDigestCadenceSchema>
>;

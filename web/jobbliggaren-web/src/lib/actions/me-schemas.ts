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

/**
 * ADR 0080 Vag 4 PR-6 — input-schema för `updateNotificationConsentAction`.
 * Speglar backend `UpdateNotificationConsentCommand` (`{ enabled, cadence }`).
 * `enabled` är en ren bool (Domänen äger consent-stämplingen); `cadence` binds
 * mot `DigestCadence`-mirrorn (sträng-enum med wire-värdena `Daily`/`Weekly`).
 * Strukturellt skydd / defense-in-depth — backend är sista barriären — så ingen
 * användarvänd valideringstext behövs (translatorn tas för factory-konsekvens).
 */
export function makeUpdateNotificationConsentSchema(_t: ValidationTranslator) {
  return z.object({
    enabled: z.boolean(),
    cadence: digestCadenceSchema,
  });
}

export type UpdateNotificationConsentInput = z.infer<
  ReturnType<typeof makeUpdateNotificationConsentSchema>
>;

/**
 * Bevakning F4 (#803) — input-schema för
 * `updateFollowedCompanyNotificationConsentAction`. Speglar backend
 * `UpdateFollowedCompanyNotificationConsentCommand` (`{ enabled }`) — INGEN
 * kadens: den är delad med matchningsnotiserna (ADR 0087 D2) och skrivs via
 * `makeUpdateNotificationConsentSchema` ovan. Strukturellt skydd /
 * defense-in-depth (backend är sista barriären), därför ingen användarvänd
 * valideringstext; translatorn tas för factory-konsekvens.
 */
export function makeUpdateFollowedCompanyNotificationConsentSchema(
  _t: ValidationTranslator
) {
  return z.object({
    enabled: z.boolean(),
  });
}

export type UpdateFollowedCompanyNotificationConsentInput = z.infer<
  ReturnType<typeof makeUpdateFollowedCompanyNotificationConsentSchema>
>;

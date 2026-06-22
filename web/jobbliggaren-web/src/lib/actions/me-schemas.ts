import { z } from "zod";
import type { useTranslations } from "next-intl";

// next-intl translator scoped to the `validation` namespace (see
// `application-schemas.ts` for the shared rationale). Callers build the schema
// via the `make*`-factories; Swedish messages live in
// `messages/sv/validation.json`.
export type ValidationTranslator = ReturnType<typeof useTranslations<"validation">>;

/**
 * TD-28 — defense-in-depth typed-confirmation + re-auth innan DELETE /me.
 * Typed-confirmation = användarens egen e-postadress (matchar GitHub/Stripe-
 * mönstret; högre friktion än ett magiskt ord).
 *
 * Schemat validerar struktur — e-post-matchning mot inloggad användare sker
 * i `deleteAccountAction` så validation-feedback kan visas inline i modalen.
 */
export function makeDeleteMyAccountSchema(t: ValidationTranslator) {
  return z.object({
    confirmEmail: z.email(t("profile.confirmEmailInvalid")),
    password: z.string().min(1, t("profile.passwordRequired")),
  });
}

export type DeleteMyAccountInput = z.infer<
  ReturnType<typeof makeDeleteMyAccountSchema>
>;

export function makeUpdateMyProfileSchema(t: ValidationTranslator) {
  return z.object({
    displayName: z
      .string()
      .trim()
      .min(1, t("profile.displayNameRequired"))
      .max(200, t("profile.displayNameMax")),
    language: z.enum(["sv", "en"], {
      message: t("profile.languageInvalid"),
    }),
    emailNotifications: z.boolean(),
    weeklySummary: z.boolean(),
  });
}

export type UpdateMyProfileInput = z.infer<
  ReturnType<typeof makeUpdateMyProfileSchema>
>;

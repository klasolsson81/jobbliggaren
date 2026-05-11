import { z } from "zod";

/**
 * TD-28 — defense-in-depth typed-confirmation + re-auth innan DELETE /me.
 * Typed-confirmation = användarens egen e-postadress (matchar GitHub/Stripe-
 * mönstret; högre friktion än ett magiskt ord).
 *
 * Schemat validerar struktur — e-post-matchning mot inloggad användare sker
 * i `deleteAccountAction` så validation-feedback kan visas inline i modalen.
 */
export const deleteMyAccountSchema = z.object({
  confirmEmail: z.email("Ange en giltig e-postadress."),
  password: z.string().min(1, "Lösenord krävs."),
});

export type DeleteMyAccountInput = z.infer<typeof deleteMyAccountSchema>;

export const updateMyProfileSchema = z.object({
  displayName: z
    .string()
    .trim()
    .min(1, "Visningsnamn krävs.")
    .max(200, "Visningsnamn får vara max 200 tecken."),
  language: z.enum(["sv", "en"], {
    message: "Välj ett giltigt språk.",
  }),
  emailNotifications: z.boolean(),
  weeklySummary: z.boolean(),
});

export type UpdateMyProfileInput = z.infer<typeof updateMyProfileSchema>;

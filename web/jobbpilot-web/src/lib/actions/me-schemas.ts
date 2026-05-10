import { z } from "zod";

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

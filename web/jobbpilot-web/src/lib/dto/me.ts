import { z } from "zod";

/**
 * `GET /api/v1/me` — current authenticated user.
 *
 * Roles modelleras som `z.array(z.string())` (required, ej optional) för att
 * matcha backend `IReadOnlyList<string>`. Tom array tillåten — `undefined`
 * inte. Detta fångar TD-7-original-buggen där `roles?: string[]` tyst
 * accepterade saknad nyckel som tom lista.
 *
 * Schema:t är icke-strikt per ADR 0020 §4 — extra fält från backend ignoreras.
 * Säkerhetsmässigt OK eftersom `roles` är auth-beslutskällan och valideras
 * strikt; eventuella extra fält kan inte användas för privilege-escalation
 * från frontend-koden. Avvägningen prioriterar forward-compat (backend kan
 * lägga till fält utan att bryta frontend) över tight binding.
 */
export const currentUserSchema = z.object({
  userId: z.string(),
  email: z.string(),
  roles: z.array(z.string()).readonly(),
});

export type CurrentUserDto = z.infer<typeof currentUserSchema>;

/**
 * `GET /api/v1/me/profile` — JobSeeker-profil.
 *
 * `createdAt` är ISO 8601-string på wire (DateTimeOffset). Ingen Date-cast
 * här — UI-formatering är konsumentansvar. Se ADR 0020 §6.
 */
export const jobSeekerProfileSchema = z.object({
  id: z.string(),
  displayName: z.string(),
  language: z.string(),
  emailNotifications: z.boolean(),
  weeklySummary: z.boolean(),
  createdAt: z.string(),
});

export type JobSeekerProfileDto = z.infer<typeof jobSeekerProfileSchema>;

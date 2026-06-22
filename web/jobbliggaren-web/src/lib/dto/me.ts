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
 *
 * F4-12 PR-B (ADR 0076): profilen bär nu även matchnings-önskemålen +
 * `hasStatedDesiredOccupation` (härlett serverside: true så snart minst en
 * yrkesgrupp angetts — driver setup-nudgen på /oversikt). Backend returnerar
 * alltid fälten (required, ej optional — `IReadOnlyList<string>` aldrig null);
 * `undefined` skulle maskera kontraktsdrift. `.readonly()` speglar
 * `IReadOnlyList<string>`. Schemat förblir icke-strikt per ADR 0020 §4.
 */
export const jobSeekerProfileSchema = z.object({
  id: z.string(),
  displayName: z.string(),
  language: z.string(),
  emailNotifications: z.boolean(),
  weeklySummary: z.boolean(),
  createdAt: z.string(),
  hasStatedDesiredOccupation: z.boolean(),
  preferredOccupationGroups: z.array(z.string()).readonly(),
  preferredRegions: z.array(z.string()).readonly(),
  // Spår 3 PR-D (ADR 0076-amendment 2026-06-21): kommun-axeln. Backend
  // projicerar nu `PreferredMunicipalities` (`IReadOnlyList<string>`, aldrig
  // null) → required (ej optional), `.readonly()` speglar kontraktet. Läses
  // tillbaka för pre-fill så region + kommun submittas atomiskt (NOTE-1).
  preferredMunicipalities: z.array(z.string()).readonly(),
  preferredEmploymentTypes: z.array(z.string()).readonly(),
});

export type JobSeekerProfileDto = z.infer<typeof jobSeekerProfileSchema>;

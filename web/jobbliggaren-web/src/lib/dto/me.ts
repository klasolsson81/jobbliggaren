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
 * ADR 0080 Vag 4 PR-6 — digest-kadens för bakgrundsmatchnings-notiser. Speglar
 * backend `DigestCadence`-enumen, som serialiseras BY NAME på wire
 * (`[JsonConverter(typeof(JsonStringEnumConverter))]`) — så detta är ett
 * sträng-enum med de exakta wire-värdena `Daily`/`Weekly` (PascalCase, engelska
 * kod-identifierare per språkpolicyn §1). De svenska etiketterna
 * Daglig/Veckovis lever ENBART i UI-copyn (`messages/{sv,en}/settings.json`),
 * aldrig på wire. En okänd kadens får schemat att förkasta svaret (strikt enum),
 * parallellt med `me-matches` `grade`-mirrorn.
 */
export const digestCadenceSchema = z.enum(["Daily", "Weekly"]);

export type DigestCadence = z.infer<typeof digestCadenceSchema>;

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
  // ADR 0080 Vag 4 PR-6: background-match notification consent (opt-in, GDPR
  // Art. 6/7, default OFF per PR-1) + the digest cadence for accumulated Strong
  // matches. Backend always projects both additively on the JobSeekerProfileDto
  // → required keys; `undefined` would mask contract drift. Read back so the
  // consent card pre-fills the user's current state. The cadence is only
  // meaningful when the toggle is on; the wire still always carries it (the
  // engine default is Weekly). (TD-115: the legacy emailNotifications/
  // weeklySummary keys were retired — they gated no email path.)
  backgroundMatchNotificationsEnabled: z.boolean(),
  digestCadence: digestCadenceSchema,
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
  // STEG 3 / ADR 0079 (Beslut 1): the CV-seeded, editable, trusted skill chips
  // and the single profile-level experience-years field. Backend always
  // returns both additively (`IReadOnlyList<string>` never null; the int is
  // nullable) → required keys (`undefined` would mask contract drift).
  // `.readonly()` mirrors `IReadOnlyList<string>`. `experienceYears` is `null`
  // when not stated. Read back for pre-fill so saving any other dimension
  // never zeroes them (the full-replace page-wipe guard).
  preferredSkills: z.array(z.string()).readonly(),
  experienceYears: z.number().int().nullable(),
  // exp-per-occ (ADR 0079-amendment PR-4): the persisted per-occupation
  // experience overlay — a SPARSE subset of `preferredOccupationGroups`, each
  // entry `{conceptId, years}` with `years` a nullable int (`null` = stated but
  // not specified; the engine never scores it, ADR 0071). Backend always
  // returns the array (`IReadOnlyList<...>` never null) → required key, with a
  // tolerant `.default([])` so an older backend that omits it still parses
  // (forward-compat, ADR 0020 §4). Read back for pre-fill so the wizard/dialog
  // seed each occupation's year input and a save never zeroes the overlay
  // (full-replace page-wipe guard).
  preferredOccupationExperience: z
    .array(
      z.object({
        conceptId: z.string(),
        years: z.number().int().nullable(),
      })
    )
    .default([]),
});

export type JobSeekerProfileDto = z.infer<typeof jobSeekerProfileSchema>;

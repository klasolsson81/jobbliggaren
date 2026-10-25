import { z } from "zod";

/**
 * #2198 (ADR 0180) — the occupation picker's search answer, mirroring the backend
 * `OccupationSearchResult` (`Jobbliggaren.Application.JobAds.Abstractions`), camelCase per ADR 0020 §6.
 *
 * Both row kinds carry every key; the factories on the backend leave the other kind's keys null or
 * empty, never absent, so each branch below types them exactly that way. A variant and each of a
 * Group row's `matchedNames` carry their own `occupationNameConceptId` (#2199): /jobb ticks a name by
 * it, while Matchning and the onboarding rail still select the group. The server is the only
 * ordering authority for rows, `matchedNames` and `variants`; nothing here sorts.
 *
 * A Group row's names are objects since #2199 PR 3. The previous release's API answers with plain
 * strings, which this schema refuses: during a rollback's recreate, and in a tab opened before the
 * release that still runs the older client, the search reads as unavailable until the containers
 * agree or the tab navigates (ADR 0180, D5's degraded state).
 */

// The taxonomy DTO's concept-id shape (1–32 characters, [A-Za-z0-9_-]).
const conceptIdSchema = z.string().regex(/^[A-Za-z0-9_-]{1,32}$/);

export const occupationSearchMatchedNameSchema = z.object({
  occupationNameConceptId: conceptIdSchema,
  nameLabel: z.string().min(1),
});
export type OccupationSearchMatchedName = z.infer<typeof occupationSearchMatchedNameSchema>;

export const occupationSearchVariantSchema = occupationSearchMatchedNameSchema.extend({
  groupConceptId: conceptIdSchema,
  groupLabel: z.string().min(1),
});
export type OccupationSearchVariant = z.infer<typeof occupationSearchVariantSchema>;

const groupRowSchema = z.object({
  kind: z.literal("Group"),
  groupConceptId: conceptIdSchema,
  groupLabel: z.string().min(1),
  matchedNames: z.array(occupationSearchMatchedNameSchema),
  labelHit: z.boolean(),
  headWord: z.null(),
  variants: z.tuple([]),
  exactMatch: z.boolean(),
});

const wordRowSchema = z.object({
  kind: z.literal("Word"),
  groupConceptId: z.null(),
  groupLabel: z.null(),
  matchedNames: z.tuple([]),
  labelHit: z.literal(false),
  headWord: z.string().min(1),
  variants: z.array(occupationSearchVariantSchema).min(2),
  exactMatch: z.boolean(),
});

export const occupationSearchRowSchema = z.discriminatedUnion("kind", [
  groupRowSchema,
  wordRowSchema,
]);
export type OccupationSearchRow = z.infer<typeof occupationSearchRowSchema>;
export type OccupationSearchGroupRow = z.infer<typeof groupRowSchema>;
export type OccupationSearchWordRow = z.infer<typeof wordRowSchema>;

export const occupationSearchResultSchema = z.object({
  rows: z.array(occupationSearchRowSchema),
  totalRows: z.number().int().nonnegative(),
});
export type OccupationSearchResult = z.infer<typeof occupationSearchResultSchema>;

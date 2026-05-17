import { z } from "zod";
import { jobAdSortBySchema, type JobAdSortBy } from "./job-ads";
import { taxonomyLabelSchema } from "./taxonomy";

// Backend `JobAdSortBy` är en C#-enum. Inget globalt JsonStringEnumConverter
// är registrerat (medvetet, konsistent projektkontrakt — CTO-triage
// 2026-05-16, TD-84-kontext / test-writer OBSERVATION 2), så backend
// SERIALISERAR SortBy som heltal (0-3) i SavedSearchDto och DESERIALISERAR
// body-enum som heltal. Index = enum-ordinal nedan. Schemat accepterar även
// strängnamn (robust om ett globalt converter läggs till i framtiden).
// Index = backend `JobAdSortBy` enum-ordinal (PublishedAtDesc=0 …
// Relevance=4, ADR 0042 Beslut D). Måste hållas i exakt enum-ordning.
export const SAVED_SEARCH_SORT_ORDER: readonly JobAdSortBy[] = [
  "PublishedAtDesc",
  "PublishedAtAsc",
  "ExpiresAtDesc",
  "ExpiresAtAsc",
  "Relevance",
];

export function sortByToIndex(sortBy: JobAdSortBy): number {
  const i = SAVED_SEARCH_SORT_ORDER.indexOf(sortBy);
  return i >= 0 ? i : 0;
}

const sortByFromWire = z
  .union([z.number().int(), z.string()])
  .transform((v, ctx): JobAdSortBy => {
    if (typeof v === "number") {
      const name = SAVED_SEARCH_SORT_ORDER[v];
      if (name) return name;
      ctx.addIssue({ code: "custom", message: `Okänt SortBy-index: ${v}` });
      return z.NEVER;
    }
    const parsed = jobAdSortBySchema.safeParse(v);
    if (parsed.success) return parsed.data;
    ctx.addIssue({ code: "custom", message: `Okänt SortBy: ${v}` });
    return z.NEVER;
  });

// Backend SavedSearchDto (ADR 0042 Beslut B): Id, Name, Ssyk[], Region[],
// Q?, SortBy(int), NotificationEnabled, LastRunAt?, CreatedAt, UpdatedAt.
// Ssyk/Region är nu IReadOnlyList (aldrig null från VO:t — tom lista =
// inget filter). Datum är ISO 8601 på wire (ADR 0020 §6).
//
// ADR 0043 Approach A (additivt): ssykLabels/regionLabels är taxonomi-
// reverse-lookup ({conceptId, label}) som backend resolvar i listan
// (ListSavedSearches). Stale id → backend "Okänd kod (<id>)" (graceful
// degradation, aldrig null/throw). Råa ssyk/region (concept-id) är
// OFÖRÄNDRADE — labels är ett rent UI-presentationslager (ADR 0042
// Beslut B-domänkontraktet rörs ej). Detalj-endpointen GetSavedSearch
// returnerar tomma label-listor (CTO-scope: bara listan), därför
// `.default([])` — schemat är robust om fältet saknas helt på wire.
export const savedSearchDtoSchema = z.object({
  id: z.string(),
  name: z.string(),
  ssyk: z.array(z.string()),
  region: z.array(z.string()),
  ssykLabels: z.array(taxonomyLabelSchema).default([]),
  regionLabels: z.array(taxonomyLabelSchema).default([]),
  q: z.string().nullable(),
  sortBy: sortByFromWire,
  notificationEnabled: z.boolean(),
  lastRunAt: z.string().nullable(),
  createdAt: z.string(),
  updatedAt: z.string(),
});
export type SavedSearchDto = z.infer<typeof savedSearchDtoSchema>;

// ListSavedSearches returnerar en ren array (ej PagedResult — JobSeeker har
// i praktiken få sparade sökningar, backend-beslut KISS).
export const listSavedSearchesResultSchema = z.array(savedSearchDtoSchema);
export type ListSavedSearchesResult = z.infer<
  typeof listSavedSearchesResultSchema
>;

// Skapa sparad sökning. Speglar backend CreateSavedSearchCommandValidator
// (Name 1-120) + SearchCriteria.Create-invarianter (concept-id-mönster,
// q 2-100, minst ett kriterium) för defense-in-depth + snabb feedback.
const conceptIdPattern = /^[A-Za-z0-9_-]{1,32}$/;

export const NAME_MAX_LENGTH = 120;

// ADR 0042 Beslut B — Ssyk/Region är nu arrays. Per-element-regex +
// maxantal-cap speglar CreateSavedSearchCommandValidator + SearchCriteria
// (Domain = sanningskälla; detta = defense-in-depth).
export const MAX_CONCEPT_IDS = 10;

const conceptIdListSchema = z
  .array(z.string())
  .max(MAX_CONCEPT_IDS, `Max ${MAX_CONCEPT_IDS} val per lista.`)
  .refine((list) => list.every((v) => conceptIdPattern.test(v)), {
    message:
      "Varje kod måste vara 1–32 tecken (bokstäver, siffror, _ eller -).",
  });

export const createSavedSearchSchema = z
  .object({
    name: z
      .string()
      .trim()
      .min(1, "Namn är obligatoriskt.")
      .max(NAME_MAX_LENGTH, `Namn får vara max ${NAME_MAX_LENGTH} tecken.`),
    ssyk: conceptIdListSchema,
    region: conceptIdListSchema,
    q: z
      .string()
      .refine((v) => v === "" || (v.length >= 2 && v.length <= 100), {
        message: "Söktexten måste vara 2–100 tecken.",
      }),
    sortBy: jobAdSortBySchema,
  })
  // Tom-invariant (ADR 0042 Beslut B.3): minst en icke-tom lista ELLER q.
  .refine(
    (v) => v.ssyk.length > 0 || v.region.length > 0 || v.q !== "",
    {
      message:
        "Minst ett sökkriterium (sökord, yrkesområde eller region) måste anges för att spara sökningen.",
      path: ["name"],
    }
  );
export type CreateSavedSearchValues = z.infer<typeof createSavedSearchSchema>;

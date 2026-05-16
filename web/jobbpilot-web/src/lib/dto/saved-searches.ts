import { z } from "zod";
import { jobAdSortBySchema, type JobAdSortBy } from "./job-ads";

// Backend `JobAdSortBy` är en C#-enum. Inget globalt JsonStringEnumConverter
// är registrerat (medvetet, konsistent projektkontrakt — CTO-triage
// 2026-05-16, TD-84-kontext / test-writer OBSERVATION 2), så backend
// SERIALISERAR SortBy som heltal (0-3) i SavedSearchDto och DESERIALISERAR
// body-enum som heltal. Index = enum-ordinal nedan. Schemat accepterar även
// strängnamn (robust om ett globalt converter läggs till i framtiden).
export const SAVED_SEARCH_SORT_ORDER: readonly JobAdSortBy[] = [
  "PublishedAtDesc",
  "PublishedAtAsc",
  "ExpiresAtDesc",
  "ExpiresAtAsc",
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

// Backend SavedSearchDto: Id, Name, Ssyk?, Region?, Q?, SortBy(int),
// NotificationEnabled, LastRunAt?, CreatedAt, UpdatedAt. Datum är ISO 8601
// på wire (ADR 0020 §6).
export const savedSearchDtoSchema = z.object({
  id: z.string(),
  name: z.string(),
  ssyk: z.string().nullable(),
  region: z.string().nullable(),
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

export const createSavedSearchSchema = z
  .object({
    name: z
      .string()
      .trim()
      .min(1, "Namn är obligatoriskt.")
      .max(NAME_MAX_LENGTH, `Namn får vara max ${NAME_MAX_LENGTH} tecken.`),
    ssyk: z
      .string()
      .refine((v) => v === "" || conceptIdPattern.test(v), {
        message:
          "SSYK-koden måste vara 1–32 tecken (bokstäver, siffror, _ eller -).",
      }),
    region: z
      .string()
      .refine((v) => v === "" || conceptIdPattern.test(v), {
        message:
          "Regionkoden måste vara 1–32 tecken (bokstäver, siffror, _ eller -).",
      }),
    q: z
      .string()
      .refine((v) => v === "" || (v.length >= 2 && v.length <= 100), {
        message: "Söktexten måste vara 2–100 tecken.",
      }),
    sortBy: jobAdSortBySchema,
  })
  .refine(
    (v) => v.ssyk !== "" || v.region !== "" || v.q !== "",
    {
      message:
        "Minst ett sökkriterium (sökord, SSYK-kod eller region) måste anges för att spara sökningen.",
      path: ["name"],
    }
  );
export type CreateSavedSearchValues = z.infer<typeof createSavedSearchSchema>;

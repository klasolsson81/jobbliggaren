import { z } from "zod";
import { jobAdSortBySchema, type JobAdSortBy } from "./job-ads";
import { taxonomyLabelSchema } from "./taxonomy";

// Backend `JobAdSortBy` är en C#-enum. Inget globalt JsonStringEnumConverter
// är registrerat (medvetet, konsistent projektkontrakt — CTO-triage
// 2026-05-16, TD-84-kontext / test-writer OBSERVATION 2), så backend
// SERIALISERAR SortBy som heltal (0-3) i SavedSearchDto och DESERIALISERAR
// body-enum som heltal. Index = enum-ordinal nedan. Schemat accepterar även
// strängnamn (robust om ett globalt converter läggs till i framtiden).
// Index = backend DOMAIN-enumen `JobAdSortBy` enum-ordinal (PublishedAtDesc=0 …
// Relevance=4, ADR 0042 Beslut D). Måste hållas i exakt Domain-enum-ordning.
//
// MEDVETET 0–4: `MatchDesc` (F4-14, ADR 0076) tillhör read-side-sortytan
// `ListJobAdsSort`, INTE Domain-enumen. Match-sort persisteras aldrig som en
// anonym SavedSearch/recent-search-sort (backend mappar MatchDesc →
// PublishedAtDesc), så den får ALDRIG en ordinal här. Lägg inte till "MatchDesc"
// — det skulle skeva int↔namn-mappningen mot Domain-enumens ordinal.
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

// Backend SavedSearchDto (ADR 0042 Beslut B + ADR 0067): id, name,
// occupationGroup[], municipality[], region[], employmentType[],
// worktimeExtent[], q?, sortBy(int), notificationEnabled, lastRunAt?,
// createdAt, updatedAt. Alla list-fält är IReadOnlyList (aldrig null från
// VO:t — tom lista = inget filter). Datum är ISO 8601 på wire (ADR 0020 §6).
//
// ADR 0067 Fas C2 (2026-06-09): Ssyk (occupation-name) utgick → occupationGroup
// (ssyk-level-4) + municipality. Fas B2 (Beslut 6, 2026-06-12): employmentType
// (anställningsform) + worktimeExtent (omfattning) tillkom som råa concept-id-
// listor — MEDVETET UTAN *Labels (taxonomi-reverse-lookup för Klass 2 är ett
// Fas E presentations-concern; backend emitterar inga Klass 2-labels ännu).
//
// ADR 0043 Approach A (additivt): occupationGroupLabels/municipalityLabels/
// regionLabels är taxonomi-reverse-lookup ({conceptId, label}) som backend
// resolvar i listan (ListSavedSearches). Stale id → backend "Okänd kod (<id>)"
// (graceful degradation, aldrig null/throw). Råa concept-id-listor är
// OFÖRÄNDRADE — labels är ett rent UI-presentationslager. Detalj-endpointen
// GetSavedSearch returnerar tomma label-listor (CTO-scope: bara listan), därför
// `.default([])` — schemat är robust om fältet saknas helt på wire.
export const savedSearchDtoSchema = z.object({
  id: z.string(),
  name: z.string(),
  occupationGroup: z.array(z.string()),
  municipality: z.array(z.string()),
  region: z.array(z.string()),
  employmentType: z.array(z.string()),
  worktimeExtent: z.array(z.string()),
  occupationGroupLabels: z.array(taxonomyLabelSchema).default([]),
  municipalityLabels: z.array(taxonomyLabelSchema).default([]),
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

// ADR 0042 Beslut B — list-dimensioner är arrays. Per-element-regex +
// maxantal-cap speglar CreateSavedSearchCommandValidator + SearchCriteria
// (Domain = sanningskälla; detta = defense-in-depth).
// ADR 0042-amendment 2026-06-09 (ADR 0067 Fas C1): 10 → 400 (= ssyk-level-4-
// universumets storlek så "Välj alla yrkesgrupper" aldrig träffar taket;
// speglar Domain SearchCriteria.MaxConceptIds).
export const MAX_CONCEPT_IDS = 400;

const conceptIdListSchema = z
  .array(z.string())
  .max(MAX_CONCEPT_IDS, `Max ${MAX_CONCEPT_IDS} val per lista.`)
  .refine((list) => list.every((v) => conceptIdPattern.test(v)), {
    message:
      "Varje kod måste vara 1–32 tecken (bokstäver, siffror, _ eller -).",
  });

// ADR 0067 Fas C2: occupationGroup + municipality ersätter ssyk. Fas B2
// (Beslut 6): employmentType + worktimeExtent (Klass 2) tillkom.
export const createSavedSearchSchema = z
  .object({
    name: z
      .string()
      .trim()
      .min(1, "Namn är obligatoriskt.")
      .max(NAME_MAX_LENGTH, `Namn får vara max ${NAME_MAX_LENGTH} tecken.`),
    occupationGroup: conceptIdListSchema,
    municipality: conceptIdListSchema,
    region: conceptIdListSchema,
    employmentType: conceptIdListSchema,
    worktimeExtent: conceptIdListSchema,
    q: z
      .string()
      .refine((v) => v === "" || (v.length >= 2 && v.length <= 100), {
        message: "Söktexten måste vara 2–100 tecken.",
      }),
    sortBy: jobAdSortBySchema,
  })
  // Tom-invariant (ADR 0042 Beslut B.3): minst en icke-tom lista ELLER q.
  .refine(
    (v) =>
      v.occupationGroup.length > 0 ||
      v.municipality.length > 0 ||
      v.region.length > 0 ||
      v.employmentType.length > 0 ||
      v.worktimeExtent.length > 0 ||
      v.q !== "",
    {
      message:
        "Minst ett sökkriterium (sökord, yrkesgrupp, kommun, region, anställningsform eller omfattning) måste anges för att spara sökningen.",
      path: ["name"],
    }
  );
export type CreateSavedSearchValues = z.infer<typeof createSavedSearchSchema>;

import { z } from "zod";
import { type JobAdSortBy } from "./job-ads";
import { taxonomyLabelSchema } from "./taxonomy";
import { SAVED_SEARCH_SORT_ORDER } from "./saved-searches";

/**
 * ADR 0060 — RecentJobSearches (auto-fångade sökningar). Spegelt backend
 * `RecentJobSearchDto` (`JobbPilot.Application.RecentJobSearches.Queries`).
 * Skild från SavedSearch (manuell-spara, ADR 0039) — auto-capture-semantik
 * via post-handler-pipeline-behavior.
 *
 * Listan är cap=20 per JobSeeker (RecentJobSearch.MaxPerSeeker) — Capturer
 * evictar äldsta LastViewedAt vid overflow. `currentCount` = live-räknat
 * antal matchande job-ads just nu; `newCount` = `max(0, currentCount - lastSeenCount)`
 * driver "(N nya)"-affordance i hero-chip.
 *
 * SortBy serialiseras som heltal (samma konvention som SavedSearchDto;
 * SAVED_SEARCH_SORT_ORDER är auktoritativ ordinal-tabell).
 */
const sortByFromWire = z
  .union([z.number().int(), z.string()])
  .transform((v, ctx): JobAdSortBy => {
    if (typeof v === "number") {
      const name = SAVED_SEARCH_SORT_ORDER[v];
      if (name) return name;
      ctx.addIssue({ code: "custom", message: `Okänt SortBy-index: ${v}` });
      return z.NEVER;
    }
    const matched = SAVED_SEARCH_SORT_ORDER.find((name) => name === v);
    if (matched) return matched;
    ctx.addIssue({ code: "custom", message: `Okänt SortBy: ${v}` });
    return z.NEVER;
  });

export const recentJobSearchDtoSchema = z.object({
  id: z.string(),
  q: z.string().nullable(),
  // ADR 0067 Fas E2a — yrke-dimensionen är yrkesgrupp (ssyk-level-4), ej
  // occupation-name. Backend `RecentJobSearchDto` bär `occupationGroupList`
  // (C2-reverse-lookup-migrerade ids) + deprecated alltid-tomma `ssykList`;
  // FE konsumerar yrkesgrupp-fältet. (Municipality-dimensionen tillkommer
  // i E2b med Län→Kommun-kaskaden.)
  occupationGroupList: z.array(z.string()),
  regionList: z.array(z.string()),
  occupationGroupLabels: z.array(taxonomyLabelSchema).default([]),
  regionLabels: z.array(taxonomyLabelSchema).default([]),
  sortBy: sortByFromWire,
  label: z.string(),
  currentCount: z.number().int().nonnegative(),
  newCount: z.number().int().nonnegative(),
  lastViewedAt: z.string(),
});
export type RecentJobSearchDto = z.infer<typeof recentJobSearchDtoSchema>;

// ListRecentSearches returnerar ren array (paritet med ListSavedSearches —
// cap=20 betyder få rader, ingen paginering behövs).
export const listRecentSearchesResultSchema = z.array(recentJobSearchDtoSchema);
export type ListRecentSearchesResult = z.infer<
  typeof listRecentSearchesResultSchema
>;

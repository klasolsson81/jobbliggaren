"use server";

import { revalidatePath } from "next/cache";
import { getTranslations } from "next-intl/server";
import { z } from "zod";
import { captureRecentSearch, deleteRecentSearch } from "@/lib/api/recent-searches";
import { jobAdSortBySchema, Q_MAX_LENGTH } from "@/lib/dto/job-ads";

export type DeleteRecentSearchResult =
  | { success: true }
  | { success: false; error: string };

/**
 * ADR 0060 Beslut 8 — raderar en RecentJobSearch (hard-delete). 404
 * (okänt id ELLER cross-tenant, oskiljbart per ADR 0031) mappas till en
 * neutral felcopy.
 */
export async function deleteRecentSearchAction(
  id: string
): Promise<DeleteRecentSearchResult> {
  const t = await getTranslations("jobads.actions");
  const result = await deleteRecentSearch(id);
  switch (result.kind) {
    case "ok":
      revalidatePath("/sokningar");
      revalidatePath("/jobb");
      return { success: true };
    case "unauthorized":
      return { success: false, error: t("notLoggedIn") };
    case "notFound":
      return {
        success: false,
        error: t("recentSearchNotFound"),
      };
    case "forbidden":
    case "rateLimited":
    case "error":
      return {
        success: false,
        error: t("recentSearchDeleteFailed"),
      };
  }
}

// The backend's own caps (SearchCriteria.MaxConceptIds, the concept-id and org.nr grammars) are the authority; these
// bounds only keep an oversized argument from travelling further.
const conceptIds = z.array(z.string().max(32)).max(400);

const captureInputSchema = z.object({
  q: z.string().max(Q_MAX_LENGTH),
  occupationGroup: conceptIds,
  occupationName: conceptIds,
  region: conceptIds,
  municipality: conceptIds,
  employmentType: conceptIds,
  worktimeExtent: conceptIds,
  employer: z.array(z.string().max(10)).max(400).optional(),
  remote: z.boolean(),
  junior: z.boolean(),
  sortBy: jobAdSortBySchema,
});

/**
 * #2285 (ADR 0060 Amendment 2026-11-22) — stores the search the user just ran on purpose, or the one on screen when they
 * click "Spara sökningen". A Server Action, so a POST that Next checks against the request's Origin; no GET stores a
 * search. The /jobb hero and toolbar call it with their committed state, and the live preview never does.
 *
 * It takes a plain object only. FormData, which a form submitted without JavaScript would send, and anything the schema
 * refuses answer `stored: false` without reaching the backend (security-auditor, #2285 plan read). The argument is
 * never logged. Only a stored search revalidates the two pages that show recent searches.
 */
export async function captureRecentSearchAction(
  search: unknown
): Promise<{ stored: boolean }> {
  if (
    search === null ||
    typeof search !== "object" ||
    Object.getPrototypeOf(search) !== Object.prototype
  ) {
    return { stored: false };
  }
  const parsed = captureInputSchema.safeParse(search);
  if (!parsed.success) return { stored: false };

  const result = await captureRecentSearch(parsed.data);
  if (result.kind !== "ok" || !result.data.stored) return { stored: false };

  revalidatePath("/jobb");
  revalidatePath("/sokningar");
  return { stored: true };
}

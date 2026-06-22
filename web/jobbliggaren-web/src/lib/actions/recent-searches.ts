"use server";

import { revalidatePath } from "next/cache";
import { getTranslations } from "next-intl/server";
import { deleteRecentSearch } from "@/lib/api/recent-searches";

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

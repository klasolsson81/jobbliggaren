"use server";

import { revalidatePath } from "next/cache";
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
  const result = await deleteRecentSearch(id);
  switch (result.kind) {
    case "ok":
      revalidatePath("/sokningar");
      revalidatePath("/jobb");
      return { success: true };
    case "unauthorized":
      return { success: false, error: "Du är inte inloggad." };
    case "notFound":
      return {
        success: false,
        error: "Sökningen kunde inte hittas. Den kan ha tagits bort redan.",
      };
    case "forbidden":
    case "rateLimited":
    case "error":
      return {
        success: false,
        error: "Kunde inte ta bort sökningen. Försök igen.",
      };
  }
}

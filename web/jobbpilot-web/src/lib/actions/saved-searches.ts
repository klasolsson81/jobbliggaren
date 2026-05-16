"use server";

import { revalidatePath } from "next/cache";
import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import {
  createSavedSearchSchema,
  sortByToIndex,
} from "@/lib/dto/saved-searches";
import { jobAdSortBySchema } from "@/lib/dto/job-ads";
import { mapActionError } from "./_action-error";

function authHeaders(sessionId: string): HeadersInit {
  return {
    Authorization: `Bearer ${sessionId}`,
    "Content-Type": "application/json",
  };
}

export type ActionResult =
  | { success: true }
  | { success: false; error: string };

/**
 * Sparar nuvarande /jobb-filterläge som en namngiven sökning.
 * Konsumerar `POST /api/v1/saved-searches`. `sortBy` skickas som heltal
 * (backend-enum-body kräver numeriskt — projektkontrakt, se
 * lib/dto/saved-searches.ts). `notificationEnabled` är alltid false i
 * Fas 2 (ADR 0039 Beslut 4 — notiser levereras Fas 5; ingen UI-yta nu).
 */
export async function createSavedSearchAction(
  _prevState: ActionResult | null,
  formData: FormData
): Promise<ActionResult> {
  const sessionId = await getSessionId();
  if (!sessionId) return { success: false, error: "Du är inte inloggad." };

  const sortByRaw = jobAdSortBySchema.safeParse(formData.get("sortBy"));
  // ADR 0042 Beslut B — ssyk/region är multi: flera formData-värden under
  // samma nyckel (FormData.getAll). Tom lista = inget filter.
  const parsed = createSavedSearchSchema.safeParse({
    name: formData.get("name") ?? "",
    ssyk: formData.getAll("ssyk").map(String).filter((v) => v !== ""),
    region: formData.getAll("region").map(String).filter((v) => v !== ""),
    q: (formData.get("q") as string | null) ?? "",
    sortBy: sortByRaw.success ? sortByRaw.data : "PublishedAtDesc",
  });
  if (!parsed.success) {
    return {
      success: false,
      error: parsed.error.issues[0]?.message ?? "Ogiltiga uppgifter.",
    };
  }

  // null = ej angivet (backend SearchCriteria.Create normaliserar
  // null → tom lista; tom lista = inget filter, ADR 0042 Beslut B.3).
  const body = {
    name: parsed.data.name,
    ssyk: parsed.data.ssyk.length === 0 ? null : parsed.data.ssyk,
    region: parsed.data.region.length === 0 ? null : parsed.data.region,
    q: parsed.data.q === "" ? null : parsed.data.q,
    sortBy: sortByToIndex(parsed.data.sortBy),
    notificationEnabled: false,
  };

  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/saved-searches`, {
      method: "POST",
      headers: authHeaders(sessionId),
      body: JSON.stringify(body),
      cache: "no-store",
    });
    if (!res.ok) {
      return {
        success: false,
        error: mapActionError(res, "Kunde inte spara sökningen."),
      };
    }
  } catch {
    return { success: false, error: "Kunde inte nå servern. Försök igen." };
  }

  revalidatePath("/sokningar");
  return { success: true };
}

/**
 * Raderar (soft-delete) en sparad sökning. `DELETE
 * /api/v1/saved-searches/{id}`. 404 (okänt/annan användare, oskiljbart
 * per ADR 0031) mappas till en neutral felcopy.
 */
export async function deleteSavedSearchAction(
  id: string
): Promise<ActionResult> {
  const sessionId = await getSessionId();
  if (!sessionId) return { success: false, error: "Du är inte inloggad." };

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/saved-searches/${id}`,
      { method: "DELETE", headers: authHeaders(sessionId), cache: "no-store" }
    );
    if (!res.ok) {
      return {
        success: false,
        error: mapActionError(res, "Kunde inte radera sökningen."),
      };
    }
  } catch {
    return { success: false, error: "Kunde inte nå servern. Försök igen." };
  }

  revalidatePath("/sokningar");
  return { success: true };
}

"use server";

import { revalidatePath } from "next/cache";
import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import { deriveOccupations } from "@/lib/api/occupation-derive";
import type { OccupationCandidate } from "@/lib/dto/match-preferences";
import {
  setMatchPreferencesSchema,
  type SetMatchPreferencesInput,
} from "./match-preferences-schemas";
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
 * F4-12 PR-B (ADR 0076) — sparar användarens matchnings-önskemål
 * (yrkesgrupper + regioner + anställningsformer) via
 * `PUT /api/v1/me/match-preferences` (204 No Content vid lyckat).
 * Speglar `me.ts` `updateMyProfileAction`: getSessionId-vakt → safeParse →
 * fetch → `mapActionError` på !ok (body läses ALDRIG, TD-10) → network-
 * fallback → `revalidatePath`.
 *
 * Full-replace: `input` bär HELA den aktuella mängden per dimension. Alla
 * tre tomma är tillåtet (rensar önskemålen — ärlig not-assessed-state).
 * Revaliderar både `/installningar` (kortet) och `/oversikt` (setup-nudgen
 * styrs av `hasStatedDesiredOccupation` som ändras av detta skriv).
 */
export async function updateMatchPreferencesAction(
  input: SetMatchPreferencesInput
): Promise<ActionResult> {
  const sessionId = await getSessionId();
  if (!sessionId) return { success: false, error: "Du är inte inloggad." };

  const parsed = setMatchPreferencesSchema.safeParse(input);
  if (!parsed.success) {
    return {
      success: false,
      error: parsed.error.issues[0]?.message ?? "Ogiltiga uppgifter.",
    };
  }

  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/me/match-preferences`, {
      method: "PUT",
      headers: authHeaders(sessionId),
      body: JSON.stringify(parsed.data),
      cache: "no-store",
    });

    if (!res.ok) {
      return {
        success: false,
        error: mapActionError(res, "Kunde inte spara dina matchningsönskemål."),
      };
    }
  } catch {
    return {
      success: false,
      error: "Kunde inte nå servern. Kontrollera din nätverksanslutning.",
    };
  }

  revalidatePath("/installningar");
  revalidatePath("/oversikt");
  return { success: true };
}

export type DeriveResult =
  | { success: true; candidates: ReadonlyArray<OccupationCandidate> }
  | { success: false; error: string };

/**
 * Tunn server-action-wrapper kring `deriveOccupations`-BFF:en så
 * matchnings-kortets klient-ö kan be om yrkestitel-förslag UTAN att läsa
 * backend direkt (server-only-gränsen bevaras; Bearer-sessionen exponeras
 * aldrig mot klienten). Förslagen skrivs ALDRIG — användaren bekräftar genom
 * att toggla kryssrutorna och därefter spara (propose-and-approve, ADR 0040
 * Beslut 4).
 */
export async function deriveOccupationsAction(
  title: string
): Promise<DeriveResult> {
  if (typeof title !== "string") {
    return { success: false, error: "Ogiltig yrkestitel." };
  }

  const result = await deriveOccupations(title);
  switch (result.kind) {
    case "ok":
      return { success: true, candidates: result.data.candidates };
    case "unauthorized":
      return { success: false, error: "Du är inte inloggad." };
    case "rateLimited":
      return {
        success: false,
        error: "För många försök. Vänta en stund och försök igen.",
      };
    default:
      return {
        success: false,
        error: "Kunde inte hämta förslag just nu. Försök igen om en stund.",
      };
  }
}

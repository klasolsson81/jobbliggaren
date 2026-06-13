"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { env } from "@/lib/env";
import { deleteSessionCookie, getSessionId } from "@/lib/auth/session";
import {
  deleteMyAccountSchema,
  type DeleteMyAccountInput,
  updateMyProfileSchema,
  type UpdateMyProfileInput,
} from "./me-schemas";
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

export async function updateMyProfileAction(
  input: UpdateMyProfileInput
): Promise<ActionResult> {
  const sessionId = await getSessionId();
  if (!sessionId) return { success: false, error: "Du är inte inloggad." };

  const parsed = updateMyProfileSchema.safeParse(input);
  if (!parsed.success) {
    return {
      success: false,
      error: parsed.error.issues[0]?.message ?? "Ogiltiga uppgifter.",
    };
  }

  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/me/profile`, {
      method: "PATCH",
      headers: authHeaders(sessionId),
      body: JSON.stringify(parsed.data),
      cache: "no-store",
    });

    if (!res.ok) {
      return {
        success: false,
        error: mapActionError(res, "Kunde inte uppdatera profilen."),
      };
    }
  } catch {
    return {
      success: false,
      error: "Kunde inte nå servern. Kontrollera din nätverksanslutning.",
    };
  }

  revalidatePath("/installningar");
  return { success: true };
}

/**
 * TD-28 — radera konto. Tre-stegs flöde:
 *   1. Validera typed-confirmation (e-postmatch) + lösenords-form
 *   2. POST /api/v1/auth/verify med lösenord (re-auth, ingen session-mutation)
 *   3. DELETE /api/v1/me (soft-delete + cascade-invalidering av alla sessioner)
 * Vid success: ta bort lokal cookie + redirect till /logga-in.
 *
 * Action returnerar inte vid success — `redirect` throw:ar. Vid failure
 * returneras `ActionResult` med svensk felmeddelande. PII (lösenord, e-post)
 * loggas ALDRIG på error-path.
 */
export async function deleteAccountAction(
  input: DeleteMyAccountInput,
  currentEmail: string
): Promise<ActionResult> {
  const parsed = deleteMyAccountSchema.safeParse(input);
  if (!parsed.success) {
    return {
      success: false,
      error: parsed.error.issues[0]?.message ?? "Ogiltiga uppgifter.",
    };
  }

  // Email-match — case-insensitive, trim. Görs här (inte i Zod) så vi kan
  // jämföra mot currentEmail (server-trusted) snarare än klient-input ensam.
  const confirm = parsed.data.confirmEmail.trim().toLowerCase();
  const expected = currentEmail.trim().toLowerCase();
  if (confirm !== expected) {
    return {
      success: false,
      error: "E-postadressen matchar inte ditt konto.",
    };
  }

  const sessionId = await getSessionId();
  if (!sessionId) return { success: false, error: "Du är inte inloggad." };

  // Steg 1 — verifiera lösenord (re-auth)
  try {
    const verifyRes = await fetch(`${env.BACKEND_URL}/api/v1/auth/verify`, {
      method: "POST",
      headers: authHeaders(sessionId),
      body: JSON.stringify({ password: parsed.data.password }),
      cache: "no-store",
    });

    if (verifyRes.status === 401) {
      return { success: false, error: "Lösenordet är felaktigt." };
    }
    if (!verifyRes.ok) {
      return {
        success: false,
        error: mapActionError(verifyRes, "Kunde inte verifiera lösenordet."),
      };
    }
  } catch {
    return {
      success: false,
      error: "Kunde inte nå servern. Kontrollera din nätverksanslutning.",
    };
  }

  // Steg 2 — radera kontot
  try {
    const deleteRes = await fetch(`${env.BACKEND_URL}/api/v1/me/`, {
      method: "DELETE",
      headers: authHeaders(sessionId),
      cache: "no-store",
    });

    if (!deleteRes.ok) {
      return {
        success: false,
        error: mapActionError(deleteRes, "Kunde inte radera kontot."),
      };
    }
  } catch {
    return {
      success: false,
      error: "Kunde inte nå servern. Kontrollera din nätverksanslutning.",
    };
  }

  // Backend har invaliderat alla sessioner — ta bort lokal cookie + redirect.
  await deleteSessionCookie();
  redirect("/logga-in");
}

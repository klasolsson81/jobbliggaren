"use server";

import { revalidatePath } from "next/cache";
import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import {
  updateMyProfileSchema,
  type UpdateMyProfileInput,
} from "./me-schemas";

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
      const body = (await res.json().catch(() => null)) as {
        detail?: string;
        title?: string;
      } | null;
      return {
        success: false,
        error:
          body?.detail ?? body?.title ?? "Kunde inte uppdatera profilen.",
      };
    }
  } catch {
    return {
      success: false,
      error: "Kunde inte nå servern. Kontrollera din nätverksanslutning.",
    };
  }

  revalidatePath("/mig");
  return { success: true };
}

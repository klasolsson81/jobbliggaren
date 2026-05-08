"use server";

import { redirect } from "next/navigation";
import { revalidatePath } from "next/cache";
import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import {
  createApplicationSchema,
  transitionStatusSchema,
  addFollowUpSchema,
  addNoteSchema,
} from "./application-schemas";

function authHeaders(sessionId: string): HeadersInit {
  return {
    Authorization: `Bearer ${sessionId}`,
    "Content-Type": "application/json",
  };
}

export type ActionResult = { success: true } | { success: false; error: string };

export async function createApplicationAction(
  _prevState: ActionResult | null,
  formData: FormData
): Promise<ActionResult> {
  const sessionId = await getSessionId();
  if (!sessionId) return { success: false, error: "Du är inte inloggad." };

  const parsed = createApplicationSchema.safeParse({
    coverLetter: formData.get("coverLetter") || undefined,
  });
  if (!parsed.success) {
    return { success: false, error: parsed.error.issues[0]?.message ?? "Ogiltiga uppgifter." };
  }

  let applicationId: string;
  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/applications`, {
      method: "POST",
      headers: authHeaders(sessionId),
      body: JSON.stringify({ coverLetter: parsed.data.coverLetter ?? null }),
      cache: "no-store",
    });

    if (!res.ok) {
      const body = (await res.json().catch(() => null)) as { detail?: string } | null;
      return { success: false, error: body?.detail ?? "Kunde inte spara ansökan." };
    }

    const data = (await res.json()) as { id: string };
    applicationId = data.id;
  } catch {
    return { success: false, error: "Kunde inte nå servern. Försök igen." };
  }

  revalidatePath("/ansokningar");
  redirect(`/ansokningar/${applicationId}`);
}

export async function transitionStatusAction(
  applicationId: string,
  targetStatus: string
): Promise<ActionResult> {
  const sessionId = await getSessionId();
  if (!sessionId) return { success: false, error: "Du är inte inloggad." };

  const parsed = transitionStatusSchema.safeParse({ applicationId, targetStatus });
  if (!parsed.success) {
    return { success: false, error: parsed.error.issues[0]?.message ?? "Ogiltiga uppgifter." };
  }

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/applications/${applicationId}/transition`,
      {
        method: "POST",
        headers: authHeaders(sessionId),
        body: JSON.stringify({ targetStatus }),
        cache: "no-store",
      }
    );

    if (!res.ok) {
      const body = (await res.json().catch(() => null)) as { detail?: string } | null;
      return { success: false, error: body?.detail ?? "Statusbytet misslyckades." };
    }
  } catch {
    return { success: false, error: "Kunde inte nå servern. Försök igen." };
  }

  revalidatePath("/ansokningar");
  revalidatePath(`/ansokningar/${applicationId}`);
  return { success: true };
}

export async function addFollowUpAction(
  applicationId: string,
  formData: FormData
): Promise<ActionResult> {
  const sessionId = await getSessionId();
  if (!sessionId) return { success: false, error: "Du är inte inloggad." };

  const parsed = addFollowUpSchema.safeParse({
    applicationId,
    channel: formData.get("channel"),
    scheduledAt: formData.get("scheduledAt"),
    note: formData.get("note") || undefined,
  });
  if (!parsed.success) {
    return { success: false, error: parsed.error.issues[0]?.message ?? "Ogiltiga uppgifter." };
  }

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/applications/${applicationId}/follow-ups`,
      {
        method: "POST",
        headers: authHeaders(sessionId),
        body: JSON.stringify({
          channel: parsed.data.channel,
          scheduledAt: parsed.data.scheduledAt,
          note: parsed.data.note ?? null,
        }),
        cache: "no-store",
      }
    );

    if (!res.ok) {
      const body = (await res.json().catch(() => null)) as { detail?: string } | null;
      return { success: false, error: body?.detail ?? "Kunde inte spara uppföljningen." };
    }
  } catch {
    return { success: false, error: "Kunde inte nå servern. Försök igen." };
  }

  revalidatePath(`/ansokningar/${applicationId}`);
  return { success: true };
}

export async function addNoteAction(
  applicationId: string,
  formData: FormData
): Promise<ActionResult> {
  const sessionId = await getSessionId();
  if (!sessionId) return { success: false, error: "Du är inte inloggad." };

  const parsed = addNoteSchema.safeParse({
    applicationId,
    content: formData.get("content"),
  });
  if (!parsed.success) {
    return { success: false, error: parsed.error.issues[0]?.message ?? "Ogiltiga uppgifter." };
  }

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/applications/${applicationId}/notes`,
      {
        method: "POST",
        headers: authHeaders(sessionId),
        body: JSON.stringify({ content: parsed.data.content }),
        cache: "no-store",
      }
    );

    if (!res.ok) {
      const body = (await res.json().catch(() => null)) as { detail?: string } | null;
      return { success: false, error: body?.detail ?? "Kunde inte spara noteringen." };
    }
  } catch {
    return { success: false, error: "Kunde inte nå servern. Försök igen." };
  }

  revalidatePath(`/ansokningar/${applicationId}`);
  return { success: true };
}

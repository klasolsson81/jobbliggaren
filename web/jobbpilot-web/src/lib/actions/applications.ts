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
  recordFollowUpOutcomeSchema,
} from "./application-schemas";
import { createdResourceSchema } from "@/lib/dto/common";
import { parseResponse } from "@/lib/dto/_helpers";
import { mapActionError } from "./_action-error";
import { isValidId } from "@/lib/validation/guid";

function authHeaders(sessionId: string): HeadersInit {
  return {
    Authorization: `Bearer ${sessionId}`,
    "Content-Type": "application/json",
  };
}

export type ActionResult = { success: true } | { success: false; error: string };

export type CreateApplicationFromJobAdResult =
  | { success: true; applicationId: string }
  | { success: false; error: string };

/**
 * F6 P5 Punkt 2 Del B — "Har ansökt"-quick-create från ADR 0053-modal-footer.
 * Returnerar applicationId vid framgång så client-island kan visa toast med
 * länk till `/ansokningar/{id}`. Skiljs från `createApplicationAction` (som
 * redirectar — denna lever inom modal-flödet, ingen redirect).
 *
 * Backend: `POST /api/v1/applications/from-job-ad/{jobAdId}` (CTO Val 3
 * Variant A — separat endpoint per SRP, commit a187467).
 */
export async function createApplicationFromJobAdAction(
  jobAdId: string
): Promise<CreateApplicationFromJobAdResult> {
  const sessionId = await getSessionId();
  if (!sessionId) return { success: false, error: "Du är inte inloggad." };
  // Allowlist-guard: avvisa icke-GUID innan id:t når backend-URL:en (SSRF-
  // barrier + path-injektion-skydd).
  if (!isValidId(jobAdId)) return { success: false, error: "Ogiltigt annons-ID." };

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/applications/from-job-ad/${encodeURIComponent(jobAdId)}`,
      {
        method: "POST",
        headers: authHeaders(sessionId),
        cache: "no-store",
      }
    );

    if (!res.ok) {
      return {
        success: false,
        error: mapActionError(res, "Kunde inte registrera ansökan."),
      };
    }

    const data = await parseResponse(
      res,
      createdResourceSchema,
      "POST /api/v1/applications/from-job-ad"
    );
    revalidatePath("/ansokningar");
    revalidatePath("/jobb");
    return { success: true, applicationId: data.id };
  } catch {
    return { success: false, error: "Kunde inte nå servern. Försök igen." };
  }
}

export async function createApplicationAction(
  _prevState: ActionResult | null,
  formData: FormData
): Promise<ActionResult> {
  const sessionId = await getSessionId();
  if (!sessionId) return { success: false, error: "Du är inte inloggad." };

  const parsed = createApplicationSchema.safeParse({
    title: formData.get("title") ?? "",
    company: formData.get("company") ?? "",
    url: formData.get("url") ?? "",
    expiresAt: formData.get("expiresAt") ?? "",
    coverLetter: formData.get("coverLetter") || undefined,
  });
  if (!parsed.success) {
    return { success: false, error: parsed.error.issues[0]?.message ?? "Ogiltiga uppgifter." };
  }

  // /ansokningar/ny skapar alltid en manuell ansökan (jobAdId == null).
  // Backend tar `manual: { title, company, url?, expiresAt? }` (ingen
  // source — manuell ansökan är implicit Source=Manual).
  let applicationId: string;
  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/applications`, {
      method: "POST",
      headers: authHeaders(sessionId),
      body: JSON.stringify({
        coverLetter: parsed.data.coverLetter ?? null,
        manual: {
          title: parsed.data.title,
          company: parsed.data.company,
          url: parsed.data.url ?? null,
          expiresAt: parsed.data.expiresAt ?? null,
        },
      }),
      cache: "no-store",
    });

    if (!res.ok) {
      return { success: false, error: mapActionError(res, "Kunde inte spara ansökan.") };
    }

    const data = await parseResponse(
      res,
      createdResourceSchema,
      "POST /api/v1/applications"
    );
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
      `${env.BACKEND_URL}/api/v1/applications/${encodeURIComponent(parsed.data.applicationId)}/transition`,
      {
        method: "POST",
        headers: authHeaders(sessionId),
        body: JSON.stringify({ targetStatus }),
        cache: "no-store",
      }
    );

    if (!res.ok) {
      return { success: false, error: mapActionError(res, "Statusbytet misslyckades.") };
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
      `${env.BACKEND_URL}/api/v1/applications/${encodeURIComponent(parsed.data.applicationId)}/follow-ups`,
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
      return { success: false, error: mapActionError(res, "Kunde inte spara uppföljningen.") };
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
      `${env.BACKEND_URL}/api/v1/applications/${encodeURIComponent(parsed.data.applicationId)}/notes`,
      {
        method: "POST",
        headers: authHeaders(sessionId),
        body: JSON.stringify({ content: parsed.data.content }),
        cache: "no-store",
      }
    );

    if (!res.ok) {
      return { success: false, error: mapActionError(res, "Kunde inte spara noteringen.") };
    }
  } catch {
    return { success: false, error: "Kunde inte nå servern. Försök igen." };
  }

  revalidatePath(`/ansokningar/${applicationId}`);
  return { success: true };
}

export async function recordFollowUpOutcomeAction(
  applicationId: string,
  followUpId: string,
  formData: FormData
): Promise<ActionResult> {
  const sessionId = await getSessionId();
  if (!sessionId) return { success: false, error: "Du är inte inloggad." };

  const parsed = recordFollowUpOutcomeSchema.safeParse({
    applicationId,
    followUpId,
    outcome: formData.get("outcome"),
  });
  if (!parsed.success) {
    return { success: false, error: parsed.error.issues[0]?.message ?? "Ogiltiga uppgifter." };
  }

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/applications/${encodeURIComponent(parsed.data.applicationId)}/follow-ups/${encodeURIComponent(parsed.data.followUpId)}/outcome`,
      {
        method: "POST",
        headers: authHeaders(sessionId),
        body: JSON.stringify({ outcome: parsed.data.outcome }),
        cache: "no-store",
      }
    );

    if (!res.ok) {
      return { success: false, error: mapActionError(res, "Kunde inte registrera utfallet.") };
    }
  } catch {
    return { success: false, error: "Kunde inte nå servern. Försök igen." };
  }

  revalidatePath(`/ansokningar/${applicationId}`);
  return { success: true };
}

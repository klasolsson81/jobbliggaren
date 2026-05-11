"use server";

import { redirect } from "next/navigation";
import { revalidatePath } from "next/cache";
import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import {
  createResumeSchema,
  renameResumeSchema,
  updateMasterContentSchema,
} from "./resume-schemas";
import type { ResumeContentDto } from "@/lib/types/resumes";
import { createdResourceSchema } from "@/lib/dto/common";
import { parseResponse } from "@/lib/dto/_helpers";

function authHeaders(sessionId: string): HeadersInit {
  return {
    Authorization: `Bearer ${sessionId}`,
    "Content-Type": "application/json",
  };
}

export type ActionResult = { success: true } | { success: false; error: string };

const GUID_REGEX =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export async function createResumeAction(
  _prevState: ActionResult | null,
  formData: FormData
): Promise<ActionResult> {
  const sessionId = await getSessionId();
  if (!sessionId) return { success: false, error: "Du är inte inloggad." };

  const parsed = createResumeSchema.safeParse({
    name: formData.get("name"),
    fullName: formData.get("fullName"),
  });
  if (!parsed.success) {
    return {
      success: false,
      error: parsed.error.issues[0]?.message ?? "Ogiltiga uppgifter.",
    };
  }

  let resumeId: string;
  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/resumes`, {
      method: "POST",
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
        error: body?.detail ?? body?.title ?? "Kunde inte skapa CV:t.",
      };
    }

    const data = await parseResponse(
      res,
      createdResourceSchema,
      "POST /api/v1/resumes"
    );
    resumeId = data.id;
  } catch {
    return { success: false, error: "Kunde inte nå servern. Försök igen." };
  }

  revalidatePath("/cv");
  redirect(`/cv/${resumeId}`);
}

export async function renameResumeAction(
  resumeId: string,
  formData: FormData
): Promise<ActionResult> {
  const sessionId = await getSessionId();
  if (!sessionId) return { success: false, error: "Du är inte inloggad." };

  const parsed = renameResumeSchema.safeParse({
    resumeId,
    name: formData.get("name"),
  });
  if (!parsed.success) {
    return {
      success: false,
      error: parsed.error.issues[0]?.message ?? "Ogiltiga uppgifter.",
    };
  }

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/resumes/${parsed.data.resumeId}`,
      {
        method: "PATCH",
        headers: authHeaders(sessionId),
        body: JSON.stringify({ name: parsed.data.name }),
        cache: "no-store",
      }
    );

    if (!res.ok) {
      const body = (await res.json().catch(() => null)) as {
        detail?: string;
        title?: string;
      } | null;
      return {
        success: false,
        error: body?.detail ?? body?.title ?? "Kunde inte byta namn.",
      };
    }
  } catch {
    return { success: false, error: "Kunde inte nå servern. Försök igen." };
  }

  revalidatePath("/cv");
  revalidatePath(`/cv/${resumeId}`);
  return { success: true };
}

export async function updateMasterContentAction(
  resumeId: string,
  content: ResumeContentDto
): Promise<ActionResult> {
  const sessionId = await getSessionId();
  if (!sessionId) return { success: false, error: "Du är inte inloggad." };

  const parsed = updateMasterContentSchema.safeParse({ resumeId, content });
  if (!parsed.success) {
    return {
      success: false,
      error: parsed.error.issues[0]?.message ?? "Ogiltiga uppgifter.",
    };
  }

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/resumes/${parsed.data.resumeId}/master`,
      {
        method: "PUT",
        headers: authHeaders(sessionId),
        body: JSON.stringify(parsed.data.content),
        cache: "no-store",
      }
    );

    if (!res.ok) {
      const body = (await res.json().catch(() => null)) as {
        detail?: string;
        title?: string;
      } | null;
      return {
        success: false,
        error: body?.detail ?? body?.title ?? "Kunde inte spara CV:t.",
      };
    }
  } catch {
    return { success: false, error: "Kunde inte nå servern. Försök igen." };
  }

  revalidatePath("/cv");
  revalidatePath(`/cv/${resumeId}`);
  return { success: true };
}

export async function deleteResumeAction(
  resumeId: string
): Promise<ActionResult> {
  const sessionId = await getSessionId();
  if (!sessionId) return { success: false, error: "Du är inte inloggad." };

  if (!GUID_REGEX.test(resumeId)) {
    return { success: false, error: "Ogiltigt CV-ID." };
  }

  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/resumes/${resumeId}`, {
      method: "DELETE",
      headers: authHeaders(sessionId),
      cache: "no-store",
    });

    if (!res.ok) {
      const body = (await res.json().catch(() => null)) as {
        detail?: string;
        title?: string;
      } | null;
      return {
        success: false,
        error: body?.detail ?? body?.title ?? "Kunde inte radera CV:t.",
      };
    }
  } catch {
    return { success: false, error: "Kunde inte nå servern. Försök igen." };
  }

  revalidatePath("/cv");
  redirect("/cv");
}

export async function deleteResumeVersionAction(
  resumeId: string,
  versionId: string
): Promise<ActionResult> {
  const sessionId = await getSessionId();
  if (!sessionId) return { success: false, error: "Du är inte inloggad." };

  if (!GUID_REGEX.test(resumeId) || !GUID_REGEX.test(versionId)) {
    return { success: false, error: "Ogiltigt ID." };
  }

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/resumes/${resumeId}/versions/${versionId}`,
      {
        method: "DELETE",
        headers: authHeaders(sessionId),
        cache: "no-store",
      }
    );

    if (!res.ok) {
      const body = (await res.json().catch(() => null)) as {
        detail?: string;
        title?: string;
      } | null;
      return {
        success: false,
        error:
          body?.detail ?? body?.title ?? "Kunde inte radera versionen.",
      };
    }
  } catch {
    return { success: false, error: "Kunde inte nå servern. Försök igen." };
  }

  revalidatePath(`/cv/${resumeId}`);
  return { success: true };
}

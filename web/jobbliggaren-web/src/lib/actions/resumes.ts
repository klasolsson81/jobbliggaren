"use server";

import { redirect } from "next/navigation";
import { revalidatePath } from "next/cache";
import { getTranslations } from "next-intl/server";
import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import { getParsedResume } from "@/lib/api/resumes";
import {
  makeCreateResumeSchema,
  makeRenameResumeSchema,
  makeUpdateMasterContentSchema,
  makePromoteParsedResumeSchema,
} from "./resume-schemas";
import type { ResumeContentDto } from "@/lib/types/resumes";
import type { ParsedContentDto } from "@/lib/dto/parsed-resume";
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

export async function createResumeAction(
  _prevState: ActionResult | null,
  formData: FormData
): Promise<ActionResult> {
  const tr = await getTranslations("resumes.actions");
  const te = await getTranslations("errors");
  const sessionId = await getSessionId();
  if (!sessionId) return { success: false, error: tr("notLoggedIn") };

  const t = await getTranslations("validation");
  const parsed = makeCreateResumeSchema(t).safeParse({
    name: formData.get("name"),
    fullName: formData.get("fullName"),
  });
  if (!parsed.success) {
    return {
      success: false,
      error: parsed.error.issues[0]?.message ?? tr("invalidData"),
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
      return {
        success: false,
        error: mapActionError(res, tr("createFailed"), te),
      };
    }

    const data = await parseResponse(
      res,
      createdResourceSchema,
      "POST /api/v1/resumes"
    );
    resumeId = data.id;
  } catch {
    return { success: false, error: tr("serverUnreachable") };
  }

  revalidatePath("/cv");
  redirect(`/cv/${resumeId}`);
}

export async function renameResumeAction(
  resumeId: string,
  formData: FormData
): Promise<ActionResult> {
  const tr = await getTranslations("resumes.actions");
  const te = await getTranslations("errors");
  const sessionId = await getSessionId();
  if (!sessionId) return { success: false, error: tr("notLoggedIn") };

  const t = await getTranslations("validation");
  const parsed = makeRenameResumeSchema(t).safeParse({
    resumeId,
    name: formData.get("name"),
  });
  if (!parsed.success) {
    return {
      success: false,
      error: parsed.error.issues[0]?.message ?? tr("invalidData"),
    };
  }

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/resumes/${encodeURIComponent(parsed.data.resumeId)}`,
      {
        method: "PATCH",
        headers: authHeaders(sessionId),
        body: JSON.stringify({ name: parsed.data.name }),
        cache: "no-store",
      }
    );

    if (!res.ok) {
      return {
        success: false,
        error: mapActionError(res, tr("renameFailed"), te),
      };
    }
  } catch {
    return { success: false, error: tr("serverUnreachable") };
  }

  revalidatePath("/cv");
  revalidatePath(`/cv/${resumeId}`);
  return { success: true };
}

export async function updateMasterContentAction(
  resumeId: string,
  content: ResumeContentDto
): Promise<ActionResult> {
  const tr = await getTranslations("resumes.actions");
  const te = await getTranslations("errors");
  const sessionId = await getSessionId();
  if (!sessionId) return { success: false, error: tr("notLoggedIn") };

  const t = await getTranslations("validation");
  const parsed = makeUpdateMasterContentSchema(t).safeParse({ resumeId, content });
  if (!parsed.success) {
    return {
      success: false,
      error: parsed.error.issues[0]?.message ?? tr("invalidData"),
    };
  }

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/resumes/${encodeURIComponent(parsed.data.resumeId)}/master`,
      {
        method: "PUT",
        headers: authHeaders(sessionId),
        body: JSON.stringify(parsed.data.content),
        cache: "no-store",
      }
    );

    if (!res.ok) {
      return {
        success: false,
        error: mapActionError(res, tr("saveFailed"), te),
      };
    }
  } catch {
    return { success: false, error: tr("serverUnreachable") };
  }

  revalidatePath("/cv");
  revalidatePath(`/cv/${resumeId}`);
  return { success: true };
}

export async function deleteResumeAction(
  resumeId: string
): Promise<ActionResult> {
  const tr = await getTranslations("resumes.actions");
  const te = await getTranslations("errors");
  const sessionId = await getSessionId();
  if (!sessionId) return { success: false, error: tr("notLoggedIn") };

  if (!isValidId(resumeId)) {
    return { success: false, error: tr("invalidResumeId") };
  }

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/resumes/${encodeURIComponent(resumeId)}`,
      {
        method: "DELETE",
        headers: authHeaders(sessionId),
        cache: "no-store",
      }
    );

    if (!res.ok) {
      return {
        success: false,
        error: mapActionError(res, tr("deleteFailed"), te),
      };
    }
  } catch {
    return { success: false, error: tr("serverUnreachable") };
  }

  revalidatePath("/cv");
  redirect("/cv");
}

/**
 * Befordra en tolkad CV-stagingartefakt (F4-8 / STEG A) till en kanonisk Resume
 * (Fas 4 STEG B / F2). `content` är den användar-godkända, gap-fillade
 * `ResumeContentDto` (DQ1 Variant A — det godkända innehållet ÄR Resumen).
 * Backend re-skannar personnummer över all submittad fritext och syntetiserar
 * aldrig (§5). Klient-validering via samma schema — server-validering är
 * auktoritativ. Delad kärna för de två promote-ingångarna (sid-redirect vs
 * onboarding-modal som stannar kvar för wizarden).
 */
async function promoteParsedResumeCore(
  parsedResumeId: string,
  name: string,
  content: ResumeContentDto
): Promise<{ ok: true; resumeId: string } | { ok: false; error: string }> {
  const tr = await getTranslations("resumes.actions");
  const te = await getTranslations("errors");
  const sessionId = await getSessionId();
  if (!sessionId) return { ok: false, error: tr("notLoggedIn") };

  const t = await getTranslations("validation");
  const parsed = makePromoteParsedResumeSchema(t).safeParse({
    parsedResumeId,
    name,
    content,
  });
  if (!parsed.success) {
    return {
      ok: false,
      error: parsed.error.issues[0]?.message ?? tr("invalidData"),
    };
  }

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/resumes/parsed/${encodeURIComponent(parsed.data.parsedResumeId)}/promote`,
      {
        method: "POST",
        headers: authHeaders(sessionId),
        body: JSON.stringify({
          name: parsed.data.name,
          content: parsed.data.content,
        }),
        cache: "no-store",
      }
    );

    if (!res.ok) {
      return { ok: false, error: mapActionError(res, tr("saveFailed"), te) };
    }

    const data = await parseResponse(
      res,
      createdResourceSchema,
      "POST /api/v1/resumes/parsed/{id}/promote"
    );
    return { ok: true, resumeId: data.id };
  } catch {
    return { ok: false, error: tr("serverUnreachable") };
  }
}

/**
 * Sid-ingången (`/cv/granska/[id]/komplettera`): vid lyckad befordran navigerar
 * vi till det nya CV:t. NEXT_REDIRECT är en framgångssignal som får propagera.
 */
export async function promoteParsedResumeAction(
  parsedResumeId: string,
  name: string,
  content: ResumeContentDto
): Promise<ActionResult> {
  const result = await promoteParsedResumeCore(parsedResumeId, name, content);
  if (!result.ok) return { success: false, error: result.error };
  revalidatePath("/cv");
  redirect(`/cv/${result.resumeId}`);
}

export type PromoteInModalResult =
  | { success: true; resumeId: string }
  | { success: false; error: string };

/**
 * Onboarding-modal-ingången (welcome-setup, STEG 1 / ADR 0079): vid lyckad
 * befordran RETURNERAR vi det nya CV-id:t i stället för att navigera bort —
 * flödet stannar i modalen och fortsätter till matchnings-wizarden. Revaliderar
 * både CV-listan och Översikt (matchnings-ytan läser nu det promotade CV:t).
 */
export async function promoteParsedResumeInModalAction(
  parsedResumeId: string,
  name: string,
  content: ResumeContentDto
): Promise<PromoteInModalResult> {
  const result = await promoteParsedResumeCore(parsedResumeId, name, content);
  if (!result.ok) return { success: false, error: result.error };
  revalidatePath("/cv");
  revalidatePath("/oversikt");
  return { success: true, resumeId: result.resumeId };
}

export type LoadParsedForGapFillResult =
  | {
      kind: "ok";
      sourceFileName: string;
      content: ParsedContentDto;
      /** Multi-signal-yrkesförslag (utbildning+erfarenhet, #145) härledda vid
       * import. Bärs in i welcome-wizarden INNAN promote raderar staging-
       * artefakten, så de rika förslagen överlever befordran (ADR 0079 STEG 1). */
      proposedOccupationGroups: string[];
    }
  | { kind: "unauthorized" }
  | { kind: "notFound" }
  | { kind: "error" };

/**
 * Läser en tolkad CV-stagingartefakt server-side för welcome-modalens
 * in-modal-gap-fill (STEG 1 / ADR 0079). Speglar exakt vad `/cv/granska/[id]/
 * komplettera`-sidan redan exponerar mot klient-formen: ägar-scopat,
 * auth-gatat, CV-PII läses i server-actionen (ingen ny egress-yta). Backend
 * `getParsedResume` är ägar-scopat (IDOR fail-closed). Returnerar även de
 * redan-härledda yrkesförslagen så de kan bäras in i wizarden före promote.
 */
export async function loadParsedResumeForGapFillAction(
  parsedResumeId: string
): Promise<LoadParsedForGapFillResult> {
  if (!isValidId(parsedResumeId)) return { kind: "notFound" };

  const result = await getParsedResume(parsedResumeId);
  switch (result.kind) {
    case "ok":
      return {
        kind: "ok",
        sourceFileName: result.data.sourceFileName,
        content: result.data.content,
        proposedOccupationGroups: result.data.occupationProposals.map(
          (p) => p.conceptId
        ),
      };
    case "unauthorized":
      return { kind: "unauthorized" };
    case "notFound":
      return { kind: "notFound" };
    default:
      return { kind: "error" };
  }
}

export async function deleteResumeVersionAction(
  resumeId: string,
  versionId: string
): Promise<ActionResult> {
  const tr = await getTranslations("resumes.actions");
  const te = await getTranslations("errors");
  const sessionId = await getSessionId();
  if (!sessionId) return { success: false, error: tr("notLoggedIn") };

  if (!isValidId(resumeId) || !isValidId(versionId)) {
    return { success: false, error: tr("invalidId") };
  }

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/resumes/${encodeURIComponent(resumeId)}/versions/${encodeURIComponent(versionId)}`,
      {
        method: "DELETE",
        headers: authHeaders(sessionId),
        cache: "no-store",
      }
    );

    if (!res.ok) {
      return {
        success: false,
        error: mapActionError(res, tr("deleteVersionFailed"), te),
      };
    }
  } catch {
    return { success: false, error: tr("serverUnreachable") };
  }

  revalidatePath(`/cv/${resumeId}`);
  return { success: true };
}

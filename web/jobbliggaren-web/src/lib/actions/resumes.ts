"use server";

import { z } from "zod";
import { redirect } from "next/navigation";
import { revalidatePath } from "next/cache";
import { getTranslations } from "next-intl/server";
import { getSessionId } from "@/lib/auth/session";
import { authedFetch } from "@/lib/http/authed-fetch";
import {
  makeCreateResumeSchema,
  makeRenameResumeSchema,
  makeUpdateMasterContentSchema,
  makePromoteParsedResumeSchema,
} from "./resume-schemas";
import type { ResumeContentDto } from "@/lib/types/resumes";
import { createdResourceSchema } from "@/lib/dto/common";
import { parseResponse } from "@/lib/dto/_helpers";
import { mapActionError } from "./_action-error";
import { isValidId } from "@/lib/validation/guid";
import type { ActionResult } from "./_action-result";

export type { ActionResult };

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
    const res = await authedFetch(sessionId, `/api/v1/resumes`, {
      method: "POST",
      body: JSON.stringify(parsed.data),
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
    const res = await authedFetch(
      sessionId,
      `/api/v1/resumes/${encodeURIComponent(parsed.data.resumeId)}`,
      {
        method: "PATCH",
        body: JSON.stringify({ name: parsed.data.name }),
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
    const res = await authedFetch(
      sessionId,
      `/api/v1/resumes/${encodeURIComponent(parsed.data.resumeId)}/master`,
      {
        method: "PUT",
        body: JSON.stringify(parsed.data.content),
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

/**
 * De fyra visuella malloptionerna som mallbyggaren skriver (Fas 4b PR-8b 8b.3). Namnen
 * är SmartEnum Name-tokens ur `template-catalog` — klient-zod är en guard (kontrollerad
 * UI), backendens validator är auktoritativ (okänt namn → 400 fail-loud). `fontPair`
 * bärs oförändrad genom byggaren (TYPSNITT-kontrollen är deferrad, Klas 2026-07-12) så
 * skrivningen bevarar den persisterade fonten.
 */
const templateOptionsInputSchema = z.object({
  template: z.string().trim().min(1).max(64),
  accentColor: z.string().trim().min(1).max(64),
  fontPair: z.string().trim().min(1).max(64),
  density: z.string().trim().min(1).max(64),
});
export type TemplateOptionsInput = z.infer<typeof templateOptionsInputSchema>;

/**
 * Sparar ett CV:s visuella malloptioner (Fas 4b PR-8b 8b.3, CTO-bind) — mallbyggarens
 * "Spara mall". Speglar `updateMasterContentAction`: getSessionId-guard + `isValidId`
 * (SSRF/path-injektion-barriär) + zod-validering FÖRE fetch, server-till-server via
 * `authedFetch` (httpOnly `__Host-`-sessionscookien bär auth; endpointen exponeras
 * aldrig klient-sidan). Body-nycklarna matchar backendens `ChangeTemplateOptionsBody`
 * (`template`/`accentColor`/`fontPair`/`density`) — INTE query-parametrarnas korta
 * namn. Vid lyckad 204 revalideras hubben (kort-metadata), detaljvyn och mall-vyn.
 * `mapActionError` läser ALDRIG ProblemDetails-body:n (säkerhetsinvariant, TD-10) men
 * sväljer aldrig felet (returnerar alltid `{success:false}`, aldrig tyst lyckat).
 */
export async function updateTemplateOptionsAction(
  resumeId: string,
  options: TemplateOptionsInput
): Promise<ActionResult> {
  const tr = await getTranslations("resumes.actions");
  const te = await getTranslations("errors");
  const sessionId = await getSessionId();
  if (!sessionId) return { success: false, error: tr("notLoggedIn") };

  if (!isValidId(resumeId)) {
    return { success: false, error: tr("invalidResumeId") };
  }
  const parsed = templateOptionsInputSchema.safeParse(options);
  if (!parsed.success) {
    return { success: false, error: tr("invalidData") };
  }

  try {
    const res = await authedFetch(
      sessionId,
      `/api/v1/resumes/${encodeURIComponent(resumeId)}/template-options`,
      {
        method: "PUT",
        body: JSON.stringify({
          template: parsed.data.template,
          accentColor: parsed.data.accentColor,
          fontPair: parsed.data.fontPair,
          density: parsed.data.density,
        }),
      }
    );

    if (!res.ok) {
      return {
        success: false,
        error: mapActionError(res, tr("templateSaveFailed"), te),
      };
    }
  } catch {
    return { success: false, error: tr("serverUnreachable") };
  }

  revalidatePath("/cv");
  revalidatePath(`/cv/${resumeId}`);
  revalidatePath(`/cv/${resumeId}/mall`);
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
    const res = await authedFetch(
      sessionId,
      `/api/v1/resumes/${encodeURIComponent(resumeId)}`,
      { method: "DELETE" }
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
    const res = await authedFetch(
      sessionId,
      `/api/v1/resumes/parsed/${encodeURIComponent(parsed.data.parsedResumeId)}/promote`,
      {
        method: "POST",
        body: JSON.stringify({
          name: parsed.data.name,
          content: parsed.data.content,
        }),
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

/**
 * Guide-ingången (`/cv/slutfor/[parsedId]`, Fas 4b PR-8.3) — CALLER-LESS sedan
 * CV-pivot 5c (R4): Slutför-guiden är retirerad (rutten 404:ar; se
 * slutfor/page.tsx) och `CvCompleteGuide` är mothballad i trädet, revert-ready.
 * Actionen följer komponenten (samma deferred-posture) och behåller sin
 * ursprungliga semantik: samma one-shot promote som sid-ingången (delar
 * `promoteParsedResumeCore`, DRY) men landar på den kanoniska granska-vyn
 * `/cv/[id]/granska`. NEXT_REDIRECT är en framgångssignal som får propagera.
 */
export async function promoteParsedResumeFromGuideAction(
  parsedResumeId: string,
  name: string,
  content: ResumeContentDto
): Promise<ActionResult> {
  const result = await promoteParsedResumeCore(parsedResumeId, name, content);
  if (!result.ok) return { success: false, error: result.error };
  revalidatePath("/cv");
  redirect(`/cv/${result.resumeId}/granska`);
}

/**
 * Kasta en tolkad CV-stagingartefakt (Fas 4b PR-8, CTO-bind Q6) — hubbens
 * åtgärdskorts "Ta bort utkastet". POST, inte DELETE: en soft-delete
 * state-transition (artefakten behålls för audit tills retention-sveparen),
 * paritet med `/promote`. Ägar-scopad + IDOR-404 fail-closed + auditerad på
 * backend-sidan. Ingen redirect: knappen sitter på hubben, `revalidatePath`
 * tar bort åtgärdskortet. `isValidId` speglar `deleteResumeAction`.
 */
export async function discardParsedResumeAction(
  parsedResumeId: string
): Promise<ActionResult> {
  const tr = await getTranslations("resumes.actions");
  const te = await getTranslations("errors");
  const sessionId = await getSessionId();
  if (!sessionId) return { success: false, error: tr("notLoggedIn") };

  if (!isValidId(parsedResumeId)) {
    return { success: false, error: tr("invalidResumeId") };
  }

  try {
    const res = await authedFetch(
      sessionId,
      `/api/v1/resumes/parsed/${encodeURIComponent(parsedResumeId)}/discard`,
      { method: "POST" }
    );

    if (!res.ok) {
      return {
        success: false,
        error: mapActionError(res, tr("discardFailed"), te),
      };
    }
  } catch {
    return { success: false, error: tr("serverUnreachable") };
  }

  revalidatePath("/cv");
  return { success: true };
}

/** Den låsta mängden finding-statusar (paritet backend `ReviewFindingStatus`).
 * "Resolved" = "jag fixar det själv", "Ignored" = "ignorera regeln" (endast
 * stilkriterier), "Open" = återställ. */
const FINDING_STATUS_VALUES = ["Open", "Resolved", "Ignored"] as const;
export type FindingStatusValue = (typeof FINDING_STATUS_VALUES)[number];

/**
 * Registrerar användarens beslut om EN granskningsanmärkning (Fas 4b PR-8.4,
 * CTO-bind Q4/Q3) — den FÖRSTA FE-konsumenten av PR-4:s
 * `PUT /api/v1/resumes/{id}/review/findings/{criterionId}/status`. Server-till-
 * server via `authedFetch` (httpOnly `__Host-`-sessionscookien bär auth; endpointen
 * exponeras aldrig klient-sidan). Vid lyckad skrivning revalideras BÅDE granska-
 * vyn (statusen/stale-hinten re-beräknas server-side, ingen klient-optimism) OCH
 * `/cv` (badge-dekrementet på kortet). 400-fel (`FindingNotIgnorable` om ett
 * icke-stilkriterium ändå når hit, `FindingNotActionable` om anmärkningen försvann
 * i en race) ytas som en civil åtgärds-specifik fallback — `mapActionError` läser
 * ALDRIG ProblemDetails-body:n (säkerhetsinvariant, TD-10) men sväljer aldrig felet
 * (returnerar `{success:false}`, aldrig tyst lyckat). Ignorera-knappen är dessutom
 * ärligt gate:ad på `isIgnorable` i UI:t, så 400:orna ska normalt aldrig inträffa.
 */
export async function setFindingStatusAction(
  resumeId: string,
  criterionId: string,
  status: FindingStatusValue
): Promise<ActionResult> {
  const tr = await getTranslations("resumes.actions");
  const te = await getTranslations("errors");
  const sessionId = await getSessionId();
  if (!sessionId) return { success: false, error: tr("notLoggedIn") };

  if (!isValidId(resumeId)) {
    return { success: false, error: tr("invalidResumeId") };
  }
  if (!FINDING_STATUS_VALUES.includes(status)) {
    return { success: false, error: tr("invalidData") };
  }
  // criterionId är ett rubrik-id ("A1"/"E3") — allowlist:a till en kort alfanumerisk
  // token så ett malformat värde aldrig når backend-URL:ens path (path-injektion-
  // barrier, paritet `isValidId`); ett okänt id kan ändå inte matcha ett kriterium.
  if (!/^[A-Za-z0-9]{1,8}$/.test(criterionId)) {
    return { success: false, error: tr("invalidData") };
  }

  try {
    const res = await authedFetch(
      sessionId,
      `/api/v1/resumes/${encodeURIComponent(resumeId)}/review/findings/${encodeURIComponent(criterionId)}/status`,
      {
        method: "PUT",
        body: JSON.stringify({ status }),
      }
    );

    if (!res.ok) {
      return {
        success: false,
        error: mapActionError(res, tr("statusUpdateFailed"), te),
      };
    }
  } catch {
    return { success: false, error: tr("serverUnreachable") };
  }

  revalidatePath(`/cv/${resumeId}/granska`);
  revalidatePath("/cv");
  return { success: true };
}

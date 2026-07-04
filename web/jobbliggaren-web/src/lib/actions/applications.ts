"use server";

import { redirect } from "next/navigation";
import { revalidatePath } from "next/cache";
import { getTranslations } from "next-intl/server";
import { getSessionId } from "@/lib/auth/session";
import { authedFetch } from "@/lib/http/authed-fetch";
import {
  makeCreateApplicationSchema,
  makeTransitionStatusSchema,
  makeAddFollowUpSchema,
  makeAddNoteSchema,
  makeRecordFollowUpOutcomeSchema,
} from "./application-schemas";
import { createdResourceSchema } from "@/lib/dto/common";
import { parseResponse } from "@/lib/dto/_helpers";
import { mapActionError } from "./_action-error";
import { isValidId } from "@/lib/validation/guid";
import type { ActionResult } from "./_action-result";

export type { ActionResult };

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
  const t = await getTranslations("applications.ui");
  const te = await getTranslations("errors");
  const sessionId = await getSessionId();
  if (!sessionId) return { success: false, error: t("actions.notLoggedIn") };
  // Allowlist-guard: avvisa icke-GUID innan id:t når backend-URL:en (SSRF-
  // barrier + path-injektion-skydd).
  if (!isValidId(jobAdId)) return { success: false, error: t("actions.invalidJobAdId") };

  try {
    const res = await authedFetch(
      sessionId,
      `/api/v1/applications/from-job-ad/${encodeURIComponent(jobAdId)}`,
      { method: "POST" }
    );

    if (!res.ok) {
      return {
        success: false,
        error: mapActionError(res, t("actions.createFromJobAdFailed"), te),
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
    return { success: false, error: t("actions.serverUnreachable") };
  }
}

export async function createApplicationAction(
  _prevState: ActionResult | null,
  formData: FormData
): Promise<ActionResult> {
  const tUi = await getTranslations("applications.ui");
  const te = await getTranslations("errors");
  const sessionId = await getSessionId();
  if (!sessionId) return { success: false, error: tUi("actions.notLoggedIn") };

  const t = await getTranslations("validation");
  const parsed = makeCreateApplicationSchema(t).safeParse({
    title: formData.get("title") ?? "",
    company: formData.get("company") ?? "",
    url: formData.get("url") ?? "",
    expiresAt: formData.get("expiresAt") ?? "",
    coverLetter: formData.get("coverLetter") || undefined,
  });
  if (!parsed.success) {
    return { success: false, error: parsed.error.issues[0]?.message ?? tUi("actions.invalidInput") };
  }

  // /ny-ansokan skapar alltid en manuell ansökan (jobAdId == null).
  // Backend tar `manual: { title, company, url?, expiresAt? }` (ingen
  // source — manuell ansökan är implicit Source=Manual).
  let applicationId: string;
  try {
    const res = await authedFetch(sessionId, `/api/v1/applications`, {
      method: "POST",
      body: JSON.stringify({
        coverLetter: parsed.data.coverLetter ?? null,
        manual: {
          title: parsed.data.title,
          company: parsed.data.company,
          url: parsed.data.url ?? null,
          expiresAt: parsed.data.expiresAt ?? null,
        },
      }),
    });

    if (!res.ok) {
      return { success: false, error: mapActionError(res, tUi("actions.createApplicationFailed"), te) };
    }

    const data = await parseResponse(
      res,
      createdResourceSchema,
      "POST /api/v1/applications"
    );
    applicationId = data.id;
  } catch {
    return { success: false, error: tUi("actions.serverUnreachable") };
  }

  revalidatePath("/ansokningar");
  redirect(`/ansokningar/${applicationId}`);
}

export async function transitionStatusAction(
  applicationId: string,
  targetStatus: string
): Promise<ActionResult> {
  const tUi = await getTranslations("applications.ui");
  const te = await getTranslations("errors");
  const sessionId = await getSessionId();
  if (!sessionId) return { success: false, error: tUi("actions.notLoggedIn") };

  const t = await getTranslations("validation");
  const parsed = makeTransitionStatusSchema(t).safeParse({ applicationId, targetStatus });
  if (!parsed.success) {
    return { success: false, error: parsed.error.issues[0]?.message ?? tUi("actions.invalidInput") };
  }

  try {
    const res = await authedFetch(
      sessionId,
      `/api/v1/applications/${encodeURIComponent(parsed.data.applicationId)}/transition`,
      {
        method: "POST",
        body: JSON.stringify({ targetStatus }),
      }
    );

    if (!res.ok) {
      return { success: false, error: mapActionError(res, tUi("actions.transitionFailed"), te) };
    }
  } catch {
    return { success: false, error: tUi("actions.serverUnreachable") };
  }

  revalidatePath("/ansokningar");
  revalidatePath(`/ansokningar/${applicationId}`);
  return { success: true };
}

export async function addFollowUpAction(
  applicationId: string,
  formData: FormData
): Promise<ActionResult> {
  const tUi = await getTranslations("applications.ui");
  const te = await getTranslations("errors");
  const sessionId = await getSessionId();
  if (!sessionId) return { success: false, error: tUi("actions.notLoggedIn") };

  const t = await getTranslations("validation");
  const parsed = makeAddFollowUpSchema(t).safeParse({
    applicationId,
    channel: formData.get("channel"),
    scheduledAt: formData.get("scheduledAt"),
    note: formData.get("note") || undefined,
  });
  if (!parsed.success) {
    return { success: false, error: parsed.error.issues[0]?.message ?? tUi("actions.invalidInput") };
  }

  try {
    const res = await authedFetch(
      sessionId,
      `/api/v1/applications/${encodeURIComponent(parsed.data.applicationId)}/follow-ups`,
      {
        method: "POST",
        body: JSON.stringify({
          channel: parsed.data.channel,
          scheduledAt: parsed.data.scheduledAt,
          note: parsed.data.note ?? null,
        }),
      }
    );

    if (!res.ok) {
      return { success: false, error: mapActionError(res, tUi("actions.addFollowUpFailed"), te) };
    }
  } catch {
    return { success: false, error: tUi("actions.serverUnreachable") };
  }

  revalidatePath(`/ansokningar/${applicationId}`);
  return { success: true };
}

export async function addNoteAction(
  applicationId: string,
  formData: FormData
): Promise<ActionResult> {
  const tUi = await getTranslations("applications.ui");
  const te = await getTranslations("errors");
  const sessionId = await getSessionId();
  if (!sessionId) return { success: false, error: tUi("actions.notLoggedIn") };

  const t = await getTranslations("validation");
  const parsed = makeAddNoteSchema(t).safeParse({
    applicationId,
    content: formData.get("content"),
  });
  if (!parsed.success) {
    return { success: false, error: parsed.error.issues[0]?.message ?? tUi("actions.invalidInput") };
  }

  try {
    const res = await authedFetch(
      sessionId,
      `/api/v1/applications/${encodeURIComponent(parsed.data.applicationId)}/notes`,
      {
        method: "POST",
        body: JSON.stringify({ content: parsed.data.content }),
      }
    );

    if (!res.ok) {
      return { success: false, error: mapActionError(res, tUi("actions.addNoteFailed"), te) };
    }
  } catch {
    return { success: false, error: tUi("actions.serverUnreachable") };
  }

  revalidatePath(`/ansokningar/${applicationId}`);
  return { success: true };
}

export async function recordFollowUpOutcomeAction(
  applicationId: string,
  followUpId: string,
  formData: FormData
): Promise<ActionResult> {
  const tUi = await getTranslations("applications.ui");
  const te = await getTranslations("errors");
  const sessionId = await getSessionId();
  if (!sessionId) return { success: false, error: tUi("actions.notLoggedIn") };

  const t = await getTranslations("validation");
  const parsed = makeRecordFollowUpOutcomeSchema(t).safeParse({
    applicationId,
    followUpId,
    outcome: formData.get("outcome"),
  });
  if (!parsed.success) {
    return { success: false, error: parsed.error.issues[0]?.message ?? tUi("actions.invalidInput") };
  }

  try {
    const res = await authedFetch(
      sessionId,
      `/api/v1/applications/${encodeURIComponent(parsed.data.applicationId)}/follow-ups/${encodeURIComponent(parsed.data.followUpId)}/outcome`,
      {
        method: "POST",
        body: JSON.stringify({ outcome: parsed.data.outcome }),
      }
    );

    if (!res.ok) {
      return { success: false, error: mapActionError(res, tUi("actions.recordOutcomeFailed"), te) };
    }
  } catch {
    return { success: false, error: tUi("actions.serverUnreachable") };
  }

  revalidatePath(`/ansokningar/${applicationId}`);
  return { success: true };
}

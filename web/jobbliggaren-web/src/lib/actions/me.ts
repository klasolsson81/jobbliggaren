"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { env } from "@/lib/env";
import { deleteSessionCookie, getSessionId } from "@/lib/auth/session";
import { updateNotificationConsent } from "@/lib/api/me";
import {
  makeDeleteMyAccountSchema,
  type DeleteMyAccountInput,
  makeUpdateMyProfileSchema,
  type UpdateMyProfileInput,
  makeUpdateNotificationConsentSchema,
  type UpdateNotificationConsentInput,
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
  const ts = await getTranslations("settings");
  const te = await getTranslations("errors");
  const sessionId = await getSessionId();
  if (!sessionId)
    return { success: false, error: ts("account.errors.notLoggedIn") };

  const t = await getTranslations("validation");
  const parsed = makeUpdateMyProfileSchema(t).safeParse(input);
  if (!parsed.success) {
    return {
      success: false,
      error: parsed.error.issues[0]?.message ?? ts("account.errors.invalidInput"),
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
        error: mapActionError(res, ts("account.errors.updateFailed"), te),
      };
    }
  } catch {
    return {
      success: false,
      error: ts("account.errors.network"),
    };
  }

  revalidatePath("/installningar");
  return { success: true };
}

/**
 * ADR 0080 Vag 4 PR-6 — sparar användarens bakgrundsmatchnings-notis-consent
 * (opt-in-toggle + digest-kadens) via `PUT /api/v1/me/notification-consent`
 * (204 vid lyckat). Tunn transport runt `updateNotificationConsent`-BFF:en så
 * klient-ön aldrig läser backend direkt (server-only-gränsen bevaras; Bearer-
 * sessionen exponeras aldrig mot klienten) — samma mönster som
 * `match-preferences`-actionerna. safeParse → BFF-anrop → `ApiResult`→
 * `ActionResult`-mappning → `revalidatePath`.
 *
 * GDPR: ett opt-in är samtycke (Art. 6(1)(a)/7), ett opt-out drar tillbaka det
 * (Art. 7(3)) — Domänen äger consent-stämplingen; denna action är ren transport.
 * Idempotent full-replace; kadensen skickas alltid med (meningsfull endast när
 * `enabled`, men wire bär den oavsett). Revaliderar `/installningar` så kortet
 * speglar det sparade läget.
 */
export async function updateNotificationConsentAction(
  input: UpdateNotificationConsentInput
): Promise<ActionResult> {
  const ts = await getTranslations("settings");
  const t = await getTranslations("validation");
  const parsed = makeUpdateNotificationConsentSchema(t).safeParse(input);
  if (!parsed.success) {
    return {
      success: false,
      error:
        parsed.error.issues[0]?.message ??
        ts("backgroundMatch.errors.invalidInput"),
    };
  }

  const result = await updateNotificationConsent(parsed.data);
  switch (result.kind) {
    case "ok":
      revalidatePath("/installningar");
      return { success: true };
    case "unauthorized":
      return {
        success: false,
        error: ts("backgroundMatch.errors.notLoggedIn"),
      };
    case "rateLimited":
      return {
        success: false,
        error: ts("backgroundMatch.errors.tooManyAttempts"),
      };
    default:
      return {
        success: false,
        error: ts("backgroundMatch.errors.saveFailed"),
      };
  }
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
  const ts = await getTranslations("settings");
  const te = await getTranslations("errors");
  const t = await getTranslations("validation");
  const parsed = makeDeleteMyAccountSchema(t).safeParse(input);
  if (!parsed.success) {
    return {
      success: false,
      error: parsed.error.issues[0]?.message ?? ts("account.errors.invalidInput"),
    };
  }

  // Email-match — case-insensitive, trim. Görs här (inte i Zod) så vi kan
  // jämföra mot currentEmail (server-trusted) snarare än klient-input ensam.
  const confirm = parsed.data.confirmEmail.trim().toLowerCase();
  const expected = currentEmail.trim().toLowerCase();
  if (confirm !== expected) {
    return {
      success: false,
      error: ts("account.errors.emailMismatch"),
    };
  }

  const sessionId = await getSessionId();
  if (!sessionId)
    return { success: false, error: ts("account.errors.notLoggedIn") };

  // Steg 1 — verifiera lösenord (re-auth)
  try {
    const verifyRes = await fetch(`${env.BACKEND_URL}/api/v1/auth/verify`, {
      method: "POST",
      headers: authHeaders(sessionId),
      body: JSON.stringify({ password: parsed.data.password }),
      cache: "no-store",
    });

    if (verifyRes.status === 401) {
      return { success: false, error: ts("account.errors.wrongPassword") };
    }
    if (!verifyRes.ok) {
      return {
        success: false,
        error: mapActionError(verifyRes, ts("account.errors.verifyFailed"), te),
      };
    }
  } catch {
    return {
      success: false,
      error: ts("account.errors.network"),
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
        error: mapActionError(deleteRes, ts("account.errors.deleteFailed"), te),
      };
    }
  } catch {
    return {
      success: false,
      error: ts("account.errors.network"),
    };
  }

  // Backend har invaliderat alla sessioner — ta bort lokal cookie + redirect.
  await deleteSessionCookie();
  redirect("/logga-in");
}

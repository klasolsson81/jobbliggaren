"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { deleteSessionCookie, getSessionId } from "@/lib/auth/session";
import { authedFetch } from "@/lib/http/authed-fetch";
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
import type { ActionResult } from "./_action-result";

export type { ActionResult };

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
    const res = await authedFetch(sessionId, `/api/v1/me/profile`, {
      method: "PATCH",
      body: JSON.stringify(parsed.data),
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
 * TD-28 / PR2c-1 — delete account with server-enforced re-auth. Two steps:
 *   1. Validate the typed confirmation (email-match) + password form (client
 *      friction only).
 *   2. POST /api/v1/me/delete with `{ password }`. The server re-authenticates
 *      the password (wrong -> 401, empty -> 400, correct -> 204) AND performs
 *      the soft-delete + cascade session-invalidation in the same operation.
 *      The password travels with the delete; there is no separate
 *      /api/v1/auth/verify pre-call any more.
 * On 204: drop the local cookie + redirect to /logga-in.
 *
 * The action does not return on success — `redirect` throws. On failure it
 * returns an `ActionResult` with a Swedish message. PII (password, email) is
 * NEVER logged on the error path.
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

  // Email-match — case-insensitive, trimmed. Done here (not in Zod) so we can
  // compare against currentEmail (server-trusted) rather than client input alone.
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

  // Delete the account — the password travels with the operation and the server
  // re-authenticates it. `authedFetch` returns the raw Response and never reads
  // the body, so we branch on the status code only.
  try {
    const res = await authedFetch(sessionId, `/api/v1/me/delete`, {
      method: "POST",
      body: JSON.stringify({ password: parsed.data.password }),
    });

    if (res.status === 401) {
      return { success: false, error: ts("account.errors.wrongPassword") };
    }
    if (res.status === 400) {
      return { success: false, error: ts("account.errors.invalidInput") };
    }
    if (res.status !== 204) {
      return {
        success: false,
        error: mapActionError(res, ts("account.errors.deleteFailed"), te),
      };
    }
    // 204 — falls through to the cookie removal + redirect below.
  } catch {
    return {
      success: false,
      error: ts("account.errors.network"),
    };
  }

  // The backend has invalidated all sessions — drop the local cookie + redirect.
  // `redirect` throws NEXT_REDIRECT, so it must stay outside the try/catch.
  await deleteSessionCookie();
  redirect("/logga-in");
}

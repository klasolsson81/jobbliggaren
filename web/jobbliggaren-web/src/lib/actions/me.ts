"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import {
  deleteSessionCookie,
  getSessionId,
  setSessionCookie,
} from "@/lib/auth/session";
import { authedFetch } from "@/lib/http/authed-fetch";
import { readProblemTitle } from "@/lib/http/problem";
import {
  updateFollowedCompanyNotificationConsent,
  updateNotificationConsent,
} from "@/lib/api/me";
import {
  makeChangePasswordSchema,
  makeChangeEmailSchema,
  makeDeleteMyAccountSchema,
  type DeleteMyAccountInput,
  makeUpdateMyProfileSchema,
  type UpdateMyProfileInput,
  makeUpdateNotificationConsentSchema,
  type UpdateNotificationConsentInput,
  makeUpdateFollowedCompanyNotificationConsentSchema,
  type UpdateFollowedCompanyNotificationConsentInput,
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
 * Bevakning F4 (#803, CTO RF-12=12C) — sets consent for the followed-company
 * email digest. The canonical GDPR Art. 7(3) withdrawal surface for that
 * channel: opting in is consent (Art. 6(1)(a)/7), switching it off withdraws it.
 * The Domain owns the consent stamping; this action is pure transport.
 *
 * Carries ONLY `{ enabled }` — the digest cadence is SHARED with the
 * background-match notifications (ADR 0087 D2) and is written by
 * `updateNotificationConsentAction`. After 7C the in-app follow-rail is
 * unaffected by this flag (Art. 6(1)(b) service); this gates the EMAIL channel
 * only. Revalidates `/installningar` so the card mirrors the saved state.
 */
export async function updateFollowedCompanyNotificationConsentAction(
  input: UpdateFollowedCompanyNotificationConsentInput
): Promise<ActionResult> {
  const ts = await getTranslations("settings");
  const t = await getTranslations("validation");
  const parsed =
    makeUpdateFollowedCompanyNotificationConsentSchema(t).safeParse(input);
  if (!parsed.success) {
    return {
      success: false,
      error:
        parsed.error.issues[0]?.message ??
        ts("followedCompanyNotifications.errors.invalidInput"),
    };
  }

  const result = await updateFollowedCompanyNotificationConsent(parsed.data);
  switch (result.kind) {
    case "ok":
      revalidatePath("/installningar");
      return { success: true };
    case "unauthorized":
      return {
        success: false,
        error: ts("followedCompanyNotifications.errors.notLoggedIn"),
      };
    case "rateLimited":
      return {
        success: false,
        error: ts("followedCompanyNotifications.errors.tooManyAttempts"),
      };
    default:
      return {
        success: false,
        error: ts("followedCompanyNotifications.errors.saveFailed"),
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

/**
 * #678 — self-service change-password + C6. The current password travels with the
 * operation and is re-authenticated server-side (wrong -> 401, empty/weak -> 400).
 * On success the backend logs the user out on every OTHER device and re-issues THIS
 * session, returning `{ sessionId, persistent }`; we re-set the `__Host-` cookie to
 * the new id (ADR 0018 — the backend sets no cookies), preserving persistence, so
 * the current device stays logged in. STAY-ON-PAGE: returns `{ success: true }`
 * (no redirect) so the card can show a confirmation. PII (either password) is NEVER
 * logged on any path.
 */
export async function changePasswordAction(
  currentPassword: string,
  newPassword: string
): Promise<ActionResult> {
  const ts = await getTranslations("settings");
  const te = await getTranslations("errors");
  const t = await getTranslations("validation");

  const parsed = makeChangePasswordSchema(t).safeParse({ currentPassword, newPassword });
  if (!parsed.success) {
    return {
      success: false,
      error: parsed.error.issues[0]?.message ?? ts("account.errors.invalidInput"),
    };
  }

  const sessionId = await getSessionId();
  if (!sessionId)
    return { success: false, error: ts("account.errors.notLoggedIn") };

  let reissued: { sessionId?: unknown; persistent?: unknown };
  try {
    const res = await authedFetch(sessionId, `/api/v1/auth/change-password`, {
      method: "POST",
      body: JSON.stringify({
        currentPassword: parsed.data.currentPassword,
        newPassword: parsed.data.newPassword,
      }),
    });

    if (res.status === 401) {
      return { success: false, error: ts("account.errors.wrongPassword") };
    }
    if (res.status === 400) {
      // #616 — a breached new password is the one 400 the client-side Zod schema can never
      // catch (it only knows length), so NIST SP 800-63B "provide the reason" requires
      // recognizing the machine code and rendering its localized copy. Only the whitelisted
      // code changes the message; backend text is never rendered.
      const title = await readProblemTitle(res);
      return {
        success: false,
        error:
          title === "Auth.PwnedPassword"
            ? ts("account.errors.passwordBreached")
            : ts("account.errors.invalidInput"),
      };
    }
    if (!res.ok) {
      return {
        success: false,
        error: mapActionError(res, ts("account.errors.changePasswordFailed"), te),
      };
    }
    // res.json() is `any`; narrow to unknown-typed fields and guard each below (§4).
    reissued = (await res.json()) as { sessionId?: unknown; persistent?: unknown };
  } catch {
    return { success: false, error: ts("account.errors.network") };
  }

  // C6 re-issue: re-set the cookie to the new session id so this device stays logged
  // in. Missing/invalid id is a can't-happen on 200; if it ever occurs the stale
  // cookie fail-safes to a logout on the next request (the password already changed).
  if (typeof reissued.sessionId === "string" && reissued.sessionId.length > 0) {
    await setSessionCookie(reissued.sessionId, reissued.persistent === true);
  }

  // No revalidatePath: a password change alters nothing server-rendered on
  // /installningar (unlike updateMyProfileAction). Stay-on-page — the card shows its
  // own confirmation and the cookie is already re-set for the next navigation.
  return { success: true };
}

/**
 * #679 — self-service change-email (request step). The current password travels
 * with the operation and is re-authenticated server-side (wrong -> 401, empty
 * current / malformed email -> 400, address already taken -> 409). On success the
 * backend returns 202 and emails a confirmation link to the NEW address; the email
 * is NOT changed at this point and NO session is touched — so, unlike
 * changePasswordAction, there is no cookie to re-issue and no session-body to read.
 * STAY-ON-PAGE: returns `{ success: true }` (no redirect) so the card can confirm
 * that a link was sent. PII (the password, the new email) is NEVER logged on any
 * path.
 */
export async function changeEmailAction(
  currentPassword: string,
  newEmail: string
): Promise<ActionResult> {
  const ts = await getTranslations("settings");
  const te = await getTranslations("errors");
  const t = await getTranslations("validation");

  const parsed = makeChangeEmailSchema(t).safeParse({ currentPassword, newEmail });
  if (!parsed.success) {
    return {
      success: false,
      error: parsed.error.issues[0]?.message ?? ts("account.errors.invalidInput"),
    };
  }

  const sessionId = await getSessionId();
  if (!sessionId)
    return { success: false, error: ts("account.errors.notLoggedIn") };

  try {
    const res = await authedFetch(sessionId, `/api/v1/auth/change-email`, {
      method: "POST",
      body: JSON.stringify({
        currentPassword: parsed.data.currentPassword,
        newEmail: parsed.data.newEmail,
      }),
    });

    if (res.status === 401) {
      return { success: false, error: ts("account.errors.wrongPassword") };
    }
    if (res.status === 400) {
      return { success: false, error: ts("account.errors.invalidInput") };
    }
    if (res.status === 409) {
      // Two distinct 409 codes share this endpoint: `Auth.ChangeEmailCooldown` (the per-user
      // rate-limit, #703) and `Auth.EmailTaken`. Read the ProblemDetails title to pick the right
      // localized copy (exact-whitelist only; the backend `detail` is never rendered). A 409 with
      // no recognized title falls back to the taken-address message (the deliberate #679 choice),
      // not the generic `stateConflict` message `mapActionError` returns for a 409.
      const title = await readProblemTitle(res);
      return {
        success: false,
        error:
          title === "Auth.ChangeEmailCooldown"
            ? ts("account.errors.changeEmailCooldown")
            : ts("account.errors.emailTaken"),
      };
    }
    if (!res.ok) {
      return {
        success: false,
        error: mapActionError(res, ts("account.errors.changeEmailFailed"), te),
      };
    }
    // 202 (any 2xx): a confirmation link was emailed to the NEW address. The email
    // is not changed and no session was touched, so there is nothing to read from
    // the body and no cookie to re-issue. Fall through to the stay-on-page success.
  } catch {
    return { success: false, error: ts("account.errors.network") };
  }

  // No revalidatePath: nothing server-rendered on /installningar changes now — the
  // address swaps only after the emailed link is confirmed (confirmEmailChangeAction).
  return { success: true };
}

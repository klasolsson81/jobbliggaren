"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import {
  deleteSessionCookie,
  getServerSession,
  getSessionId,
  setSessionCookie,
} from "@/lib/auth/session";
import { authedFetch } from "@/lib/http/authed-fetch";
import { readProblemTitle } from "@/lib/http/problem";
import { AUTH_ERROR_CODES } from "@/lib/auth/auth-error-codes";
import { emailInputSchema } from "@/lib/auth/challenge-schemas";
import { comparableAddress } from "@/lib/auth/comparable-address";
import { writeLoginFlow } from "@/lib/auth/login-flow-cookie";
import { checkNewAddress, NEW_ADDRESS_REFUSAL_COPY } from "@/lib/auth/new-address";
import type { CodeProof, ReauthOutcome } from "@/lib/auth/reauth-action-state";
import { type BoundCodeRefusal, verifyBoundCode } from "@/lib/auth/reauth-code";
import { parseResponse } from "@/lib/dto/_helpers";
import { boundChallengeSchema, reissuedSessionSchema } from "@/lib/dto/reauth";
import {
  updateFollowedCompanyNotificationConsent,
  updateNotificationConsent,
} from "@/lib/api/me";
import {
  codeProofSchema,
  deleteConfirmationSchema,
  makeUpdateMyProfileSchema,
  type UpdateMyProfileInput,
  makeUpdateNotificationConsentSchema,
  type UpdateNotificationConsentInput,
  makeUpdateFollowedCompanyNotificationConsentSchema,
  type UpdateFollowedCompanyNotificationConsentInput,
} from "./me-schemas";
import { mapActionError } from "./_action-error";
import type { ActionResult } from "./_action-result";

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

  revalidatePath("/mina-sidor");
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
 * `enabled`, men wire bär den oavsett). Revaliderar `/mina-sidor` så kortet
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
      revalidatePath("/mina-sidor");
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
 * only. Revalidates `/mina-sidor` so the card mirrors the saved state.
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
      revalidatePath("/mina-sidor");
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
 * A bound code the backend did not accept, in the consumer's copy. The code was not spent by it: a
 * wrong code keeps its remaining attempts, and a dead one needs a new code whatever happens next.
 */
function codeRefusal(
  refusal: BoundCodeRefusal,
  copy: { wrongCode: string; lastAttempt: string; tooManyAttempts: string; unavailable: string }
): ReauthOutcome<never> {
  switch (refusal.kind) {
    case "wrongCode":
      // One slot, one announcement: the last-attempt warning rides in the same alert as the miss.
      return {
        ok: false,
        kind: "wrongCode",
        error: refusal.lastAttempt ? `${copy.wrongCode} ${copy.lastAttempt}` : copy.wrongCode,
      };
    case "deadCode":
      return { ok: false, kind: "deadCode", reason: refusal.reason };
    case "status":
      if (refusal.cause === "notLoggedIn") return { ok: false, kind: "notLoggedIn" };
      return {
        ok: false,
        kind: "status",
        error: refusal.cause === "tooManyAttempts" ? copy.tooManyAttempts : copy.unavailable,
      };
  }
}

/**
 * #1740 — deletes the account on a re-authentication code (ADR 0142 D5). The code is verified and the
 * account deleted in this one action, so the grant between them never leaves the server.
 *
 * The typed address is compared with the SESSION's address before anything is spent (#822: an address
 * the caller hands in is friction it can hand itself), in the one comparison form this side of the wire
 * uses (`comparableAddress`). Once the code is accepted it is spent, and the backend's answer falls in
 * one of three classes (security-auditor, #1740 Minor 3): a documented refusal says the account was not
 * deleted; a 5xx, a transport failure or any other 2xx may sit over a committed deletion and claims
 * nothing; a 204 is done.
 *
 * On 204 the login page's notice and the session's end travel in this one response, before the
 * redirect. The notice carries nothing but its name: the cookie outlives the account for up to two
 * minutes on a device that may be shared. `redirect` throws, so it stays outside every try.
 */
export async function deleteAccountAction(
  confirmEmail: string,
  proof: CodeProof
): Promise<ReauthOutcome<never>> {
  const ts = await getTranslations("settings");
  const tp = await getTranslations("pages");

  const code = codeProofSchema.safeParse(proof);
  if (!code.success) {
    return { ok: false, kind: "wrongCode", error: tp("auth.passwordless.code.malformedCode") };
  }
  const confirmation = deleteConfirmationSchema.safeParse(confirmEmail);
  const session = await getServerSession();
  if (!session) return { ok: false, kind: "notLoggedIn" };
  const expected = comparableAddress(session.email);
  // Fail closed: an absent expected address must never let "" === "" arm an irreversible action.
  if (
    !confirmation.success ||
    expected.length === 0 ||
    comparableAddress(confirmation.data) !== expected
  ) {
    return { ok: false, kind: "inputRefused", error: ts("account.delete.confirmMismatch") };
  }

  const sessionId = await getSessionId();
  if (!sessionId) return { ok: false, kind: "notLoggedIn" };

  const verified = await verifyBoundCode("reauth", sessionId, code.data);
  if (!verified.ok) {
    return codeRefusal(verified, {
      wrongCode: tp("auth.passwordless.code.wrongCode"),
      lastAttempt: tp("auth.passwordless.code.lastAttempt"),
      tooManyAttempts: tp("auth.passwordless.errors.tooManyAttempts"),
      unavailable: ts("account.reauth.verifyUnavailable"),
    });
  }

  let res: Response;
  try {
    res = await authedFetch(sessionId, "/api/v1/me/delete", {
      method: "POST",
      body: JSON.stringify({ reauthGrant: verified.grant }),
    });
  } catch {
    return { ok: false, kind: "outcomeUnknown", error: ts("account.delete.outcomeUnknown") };
  }
  if (res.status !== 204) {
    return res.status >= 400 && res.status < 500
      ? {
          ok: false,
          kind: "operationRefused",
          error: `${ts("account.delete.failed")} ${ts("account.reauth.codeSpent")}`,
          channel: "status",
        }
      : { ok: false, kind: "outcomeUnknown", error: ts("account.delete.outcomeUnknown") };
  }

  await writeLoginFlow({ phase: "notice", notice: "accountDeleted" });
  await deleteSessionCookie();
  redirect("/logga-in");
}

/**
 * #1740 — the REQUEST step of change-email by two codes (ADR 0142 D5). The re-authentication code is
 * verified and the change requested in this one action, so the grant between them never leaves the
 * server. The new address is checked the way the card checks it before anything is spent; what an
 * address is stays the backend's to say.
 *
 * Once the code is accepted it is spent, and every refusal after it says so. The address cannot change
 * at this step, so no answer here is an unknown outcome (design-reviewer, #1740): a 202 without a
 * readable id, a 5xx or a lost response all leave the address as it was. Among the 409s only the two
 * with copy of their own are compared; every other one, the shared cooldown among them, takes the
 * neutral copy, so no producer of a 409 can be told apart (security-auditor, #1740 S3).
 */
export async function requestEmailChangeAction(
  newEmail: string,
  proof: CodeProof
): Promise<ReauthOutcome<{ challengeId: string }>> {
  const ts = await getTranslations("settings");
  const tp = await getTranslations("pages");

  const code = codeProofSchema.safeParse(proof);
  if (!code.success) {
    return { ok: false, kind: "wrongCode", error: tp("auth.passwordless.code.malformedCode") };
  }
  const session = await getServerSession();
  if (!session) return { ok: false, kind: "notLoggedIn" };
  const address = checkNewAddress(typeof newEmail === "string" ? newEmail : "", session.email);
  if (!address.ok) {
    return { ok: false, kind: "inputRefused", error: ts(NEW_ADDRESS_REFUSAL_COPY[address.reason]) };
  }

  const sessionId = await getSessionId();
  if (!sessionId) return { ok: false, kind: "notLoggedIn" };

  const verified = await verifyBoundCode("reauth", sessionId, code.data);
  if (!verified.ok) {
    return codeRefusal(verified, {
      wrongCode: tp("auth.passwordless.code.wrongCode"),
      lastAttempt: tp("auth.passwordless.code.lastAttempt"),
      tooManyAttempts: tp("auth.passwordless.errors.tooManyAttempts"),
      unavailable: ts("account.reauth.verifyUnavailable"),
    });
  }

  const spent = (message: string) => `${message} ${ts("account.reauth.codeSpent")}`;
  const notDone = {
    ok: false,
    kind: "operationRefused",
    error: spent(ts("account.changeEmail.notDone")),
    channel: "status",
  } as const;

  let res: Response;
  try {
    res = await authedFetch(sessionId, "/api/v1/auth/change-email", {
      method: "POST",
      body: JSON.stringify({ reauthGrant: verified.grant, newEmail: address.address }),
    });
  } catch {
    return notDone;
  }

  if (res.status === 202) {
    try {
      const { challengeId } = await parseResponse(
        res,
        boundChallengeSchema,
        "POST /api/v1/auth/change-email"
      );
      return { ok: true, value: { challengeId } };
    } catch {
      return notDone;
    }
  }
  if (res.status === 409) {
    const title = await readProblemTitle(res);
    if (title === AUTH_ERROR_CODES.EmailTaken) {
      return {
        ok: false,
        kind: "operationRefused",
        error: spent(ts("account.errors.emailTaken")),
        channel: "field",
      };
    }
    if (title === AUTH_ERROR_CODES.ChangeEmailTargetBudgetExhausted) {
      return {
        ok: false,
        kind: "operationRefused",
        error: spent(ts("account.errors.changeEmailTargetBudget")),
        channel: "status",
        terminal: true,
      };
    }
    return {
      ok: false,
      kind: "operationRefused",
      error: spent(ts("account.errors.changeEmailCooldown")),
      channel: "status",
    };
  }
  if (res.status === 400) {
    return {
      ok: false,
      kind: "operationRefused",
      error: spent(ts("account.changeEmail.unusable")),
      channel: "field",
    };
  }
  if (res.status === 503 && (await readProblemTitle(res)) === AUTH_ERROR_CODES.EmailDeliveryUnavailable) {
    return {
      ok: false,
      kind: "operationRefused",
      error: spent(ts("account.errors.emailDeliveryUnavailable")),
      channel: "status",
      terminal: true,
    };
  }
  return notDone;
}

/**
 * #1740 — the CONFIRM step of change-email: the code mailed to the NEW address is verified and the
 * change confirmed in this one action, so neither grant leaves the server. The address is the one the
 * request sent, verbatim: the grant binds it as spelled.
 *
 * Once the code is accepted it is spent, and the answer falls in one of three classes
 * (security-auditor, #1740 Minor 3): a documented refusal leaves the address unchanged; a 5xx, a
 * transport failure or a 200 that does not parse may sit over a committed change and claims nothing;
 * a parsed 200 is done. Only then is the device's session re-issued (ADR 0018: the backend sets no
 * cookie), and nothing reads the session after the confirm, whose old id is dead from there on (S2).
 */
export async function confirmEmailChangeAction(
  newEmail: string,
  proof: CodeProof
): Promise<ReauthOutcome<null>> {
  const ts = await getTranslations("settings");
  const tp = await getTranslations("pages");

  const code = codeProofSchema.safeParse(proof);
  if (!code.success) {
    return { ok: false, kind: "wrongCode", error: tp("auth.passwordless.code.malformedCode") };
  }
  const address = emailInputSchema.safeParse(newEmail);
  if (!address.success) {
    return { ok: false, kind: "inputRefused", error: ts("account.changeEmail.invalidEmail") };
  }

  const sessionId = await getSessionId();
  if (!sessionId) return { ok: false, kind: "notLoggedIn" };

  const verified = await verifyBoundCode("changeEmail", sessionId, code.data);
  if (!verified.ok) {
    return codeRefusal(verified, {
      wrongCode: ts("account.changeEmail.wrongCode"),
      lastAttempt: ts("account.changeEmail.lastAttempt"),
      tooManyAttempts: tp("auth.passwordless.errors.tooManyAttempts"),
      unavailable: ts("account.reauth.verifyUnavailable"),
    });
  }

  const spent = (message: string) => `${message} ${ts("account.reauth.codeSpent")}`;
  const unknown = {
    ok: false,
    kind: "outcomeUnknown",
    error: ts("account.changeEmail.outcomeUnknown", { newEmail: address.data }),
  } as const;

  let res: Response;
  try {
    res = await authedFetch(sessionId, "/api/v1/auth/change-email/confirm", {
      method: "POST",
      body: JSON.stringify({ changeEmailGrant: verified.grant, newEmail: address.data }),
    });
  } catch {
    return unknown;
  }

  if (res.status === 200) {
    let reissued: { sessionId: string; persistent: boolean };
    try {
      reissued = await parseResponse(
        res,
        reissuedSessionSchema,
        "POST /api/v1/auth/change-email/confirm"
      );
    } catch {
      return unknown;
    }
    await setSessionCookie(reissued.sessionId, reissued.persistent);
    return { ok: true, value: null };
  }
  if (res.status >= 400 && res.status < 500) {
    const title = res.status === 409 ? await readProblemTitle(res) : null;
    if (title === AUTH_ERROR_CODES.EmailTaken) {
      return {
        ok: false,
        kind: "operationRefused",
        error: spent(ts("account.errors.emailTaken")),
        channel: "field",
      };
    }
    return {
      ok: false,
      kind: "operationRefused",
      error: spent(
        ts(
          title === AUTH_ERROR_CODES.EmailChangeIncomplete
            ? "account.changeEmail.incomplete"
            : "account.changeEmail.notDone"
        )
      ),
      channel: "status",
      terminal: true,
    };
  }
  return unknown;
}

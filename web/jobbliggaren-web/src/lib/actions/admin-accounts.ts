"use server";

import { getTranslations } from "next-intl/server";
import type {
  AdminEmailChangeCancelOutcome,
  AdminEmailChangeRequestOutcome,
} from "@/lib/admin/account-email-change";
import { accountEmailChangePath, isAccountId } from "@/lib/api/admin-accounts";
import { AUTH_ERROR_CODES } from "@/lib/auth/auth-error-codes";
import type { MessageChannel } from "@/lib/auth/challenge-action-state";
import { checkNewAddress } from "@/lib/auth/new-address";
import type { CodeProof } from "@/lib/auth/reauth-action-state";
import { codeRefusalOutcome, verifyBoundCode } from "@/lib/auth/reauth-code";
import { getServerSession, getSessionId, ROLES } from "@/lib/auth/session";
import { parseResponse, parseRetryAfter } from "@/lib/dto/_helpers";
import { emailChangeRequestedSchema } from "@/lib/dto/admin-accounts";
import { authedFetch } from "@/lib/http/authed-fetch";
import { readProblemTitle } from "@/lib/http/problem";
import { codeProofSchema } from "./me-schemas";

const REQUEST_CONTEXT = "POST /api/v1/admin/accounts/{id}/email-change";

/**
 * #1975 (ADR 0153) — an administrator starts a change of an account's address. The administrator's own step-up code
 * is verified and the change requested in this one action, so the grant between them never reaches the browser
 * (security-auditor, #1740 S1). The new address is checked as any address must be before a code is spent; that it
 * differs from the account's own address is the panel's check, and the backend's.
 *
 * Once the code is accepted it is spent. The answers fall in three classes
 * (design-reviewer, #1975 item 5): a 202 that reads is a pending change; a refusal the backend documents changed
 * nothing; a 5xx, a lost response or a 202 that does not read may sit over a pending change whose two mails have
 * gone, so it claims nothing. Among the 409s only those with copy of their own are compared; every other one, the
 * shared per-address cooldown among them, takes the neutral copy (#1740 S3).
 */
export async function requestAccountEmailChangeAction(
  accountId: string,
  newEmail: string,
  proof: CodeProof,
): Promise<AdminEmailChangeRequestOutcome> {
  const t = await getTranslations("admin.users");
  const ts = await getTranslations("settings");
  const tp = await getTranslations("pages");

  const code = codeProofSchema.safeParse(proof);
  if (!code.success) {
    return { ok: false, kind: "wrongCode", error: tp("auth.passwordless.code.malformedCode") };
  }
  if (!isAccountId(accountId)) return { ok: false, kind: "inputRefused", error: t("errors.gone") };
  // No account address to compare with here, and an empty one is never the same as the address typed.
  const address = checkNewAddress(typeof newEmail === "string" ? newEmail : "", "");
  if (!address.ok) {
    return { ok: false, kind: "inputRefused", error: t(`edit.refusal.${address.reason}`) };
  }

  // The role before the code, in the backend's own order: an account without it never spends one here.
  const session = await getServerSession();
  if (!session) return { ok: false, kind: "notLoggedIn" };
  if (!session.roles.includes(ROLES.Admin)) {
    return { ok: false, kind: "status", error: t("errors.forbidden") };
  }
  const sessionId = await getSessionId();
  if (!sessionId) return { ok: false, kind: "notLoggedIn" };

  const verified = await verifyBoundCode("reauth", sessionId, code.data);
  if (!verified.ok) {
    return codeRefusalOutcome(verified, {
      wrongCode: tp("auth.passwordless.code.wrongCode"),
      lastAttempt: tp("auth.passwordless.code.lastAttempt"),
      tooManyAttempts: tp("auth.passwordless.errors.tooManyAttempts"),
      unavailable: ts("account.reauth.verifyUnavailable"),
    });
  }

  const spent = (message: string) => `${message} ${ts("account.reauth.codeSpent")}`;
  const refused = (
    message: string,
    channel: MessageChannel = "status",
    after?: "changed" | "gone" | "refetch",
  ): AdminEmailChangeRequestOutcome => ({
    ok: false,
    kind: "operationRefused",
    error: spent(message),
    channel,
    ...(after === undefined ? {} : { after }),
  });
  const unknown: AdminEmailChangeRequestOutcome = {
    ok: false,
    kind: "outcomeUnknown",
    error: spent(t("emailChange.unknown")),
  };

  let res: Response;
  try {
    res = await authedFetch(sessionId, accountEmailChangePath(accountId), {
      method: "POST",
      body: JSON.stringify({ newEmail: address.address, reauthGrant: verified.grant }),
    });
  } catch {
    return unknown;
  }

  switch (res.status) {
    case 202:
      try {
        const { completableFrom, expiresAt } = await parseResponse(
          res,
          emailChangeRequestedSchema,
          REQUEST_CONTEXT,
        );
        return { ok: true, value: { state: "pending", completableFrom, expiresAt } };
      } catch {
        return unknown;
      }
    case 400:
      return refused(ts("account.changeEmail.unusable"), "field");
    case 401:
      // A refused grant is the backend's own answer; any other 401 is a session that ended on the way.
      return (await readProblemTitle(res)) === AUTH_ERROR_CODES.InvalidCredentials
        ? refused(t("emailChange.notConfirmed"))
        : { ok: false, kind: "notLoggedIn" };
    case 403:
      return refused(t("errors.forbidden"));
    case 404:
      return (await readProblemTitle(res)) === AUTH_ERROR_CODES.UserNotFound
        ? refused(t("errors.gone"), "status", "gone")
        : unknown;
    case 409: {
      const title = await readProblemTitle(res);
      if (title === AUTH_ERROR_CODES.EmailTaken) return refused(ts("account.errors.emailTaken"), "field");
      if (title === AUTH_ERROR_CODES.AccountEmailChangeAdministratorTarget) {
        return refused(t("emailChange.administratorTarget"), "status", "changed");
      }
      if (title === AUTH_ERROR_CODES.AccountEmailChangeInactiveTarget) {
        return refused(t("emailChange.changed"), "status", "changed");
      }
      if (title === AUTH_ERROR_CODES.AccountEmailChangePendingForAnotherAccount) {
        return refused(t("emailChange.pendingElsewhere"), "status", "refetch");
      }
      return refused(ts("account.errors.changeEmailCooldown"));
    }
    case 429:
      return refused(t("errors.rateLimited", { seconds: parseRetryAfter(res.headers.get("Retry-After")) }));
    case 503:
      // Discriminated on the title: the volatile store's own 503 carries none, and it may follow a change the store
      // wrote, so it claims nothing.
      return (await readProblemTitle(res)) === AUTH_ERROR_CODES.EmailDeliveryUnavailable
        ? { ok: false, kind: "refused", error: spent(t("emailChange.deliveryUnavailable")) }
        : unknown;
    default:
      return unknown;
  }
}

/**
 * #1975 (ADR 0153) — cancels the account's pending address change. No re-authentication: a cancel only removes
 * exposure (security-auditor C-4). A cancel that found nothing is the backend's 410 and never a success, because a
 * write that changes nothing claims no change; a 5xx or a lost response may sit over a cancel that happened, so it
 * claims nothing.
 */
export async function cancelAccountEmailChangeAction(accountId: string): Promise<AdminEmailChangeCancelOutcome> {
  // An id that names no account has nothing pending.
  if (!isAccountId(accountId)) return { kind: "nothingPending" };
  const sessionId = await getSessionId();
  if (!sessionId) return { kind: "refused", reason: "unauthorized" };

  let res: Response;
  try {
    res = await authedFetch(sessionId, accountEmailChangePath(accountId), { method: "DELETE" });
  } catch {
    return { kind: "unknown" };
  }

  switch (res.status) {
    case 204:
      return { kind: "cancelled" };
    case 410:
      return (await readProblemTitle(res)) === AUTH_ERROR_CODES.AccountEmailChangeNothingPending
        ? { kind: "nothingPending" }
        : { kind: "unknown" };
    case 401:
      return { kind: "refused", reason: "unauthorized" };
    case 403:
      return { kind: "refused", reason: "forbidden" };
    case 429:
      return {
        kind: "refused",
        reason: "rateLimited",
        retryAfterSeconds: parseRetryAfter(res.headers.get("Retry-After")),
      };
    default:
      return { kind: "unknown" };
  }
}

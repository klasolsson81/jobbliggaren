"use server";

import { getTranslations } from "next-intl/server";
import { AUTH_ERROR_CODES } from "@/lib/auth/auth-error-codes";
import type { ReauthRequestResult } from "@/lib/auth/reauth-action-state";
import { getSessionId } from "@/lib/auth/session";
import { parseResponse } from "@/lib/dto/_helpers";
import { boundChallengeSchema } from "@/lib/dto/reauth";
import { authedFetch } from "@/lib/http/authed-fetch";
import { readProblemTitle } from "@/lib/http/problem";

/**
 * Asks for a re-authentication code to the account's own address (#1740, ADR 0142 D5). The address is
 * the session user's and never the client's, so the request carries no body. The challenge id goes back
 * to the dialog that asked for it and nowhere else.
 *
 * The 202 follows a synchronous send, and every branch that sends nothing answers a visible refusal,
 * which is what lets the dialog say "Vi har skickat" where the login page may not (ADR 0142 "Page form").
 */
export async function requestReauthCode(): Promise<ReauthRequestResult> {
  const t = await getTranslations("settings");
  const tp = await getTranslations("pages");
  const sessionId = await getSessionId();
  if (!sessionId) return { ok: false, kind: "notLoggedIn" };

  const unavailable = { ok: false, kind: "status", error: t("account.reauth.unavailable") } as const;

  let res: Response;
  try {
    res = await authedFetch(sessionId, "/api/v1/auth/reauth", { method: "POST" });
  } catch {
    return unavailable;
  }

  if (res.status === 202) {
    try {
      const { challengeId } = await parseResponse(
        res,
        boundChallengeSchema,
        "POST /api/v1/auth/reauth"
      );
      return { ok: true, challengeId };
    } catch {
      return unavailable;
    }
  }
  if (res.status === 409) {
    // Both budgets are the account's, and a hijacked session can spend them without the inbox, so
    // neither message names who asked (security-auditor, #1740 Minor 5).
    const title = await readProblemTitle(res);
    if (title === AUTH_ERROR_CODES.ReauthCodeBudgetExhausted) {
      return { ok: false, kind: "terminal", error: t("account.reauth.budgetExhausted") };
    }
    if (title === AUTH_ERROR_CODES.ReauthCooldown) {
      return { ok: false, kind: "status", error: t("account.reauth.cooldown"), cooldown: true };
    }
    return unavailable;
  }
  if (res.status === 503) {
    // Discriminated on the title: the session store's own 503 carries none, and saying "mail is off"
    // during a Redis outage would mask it (the #734 B-ii arm this replaces did the same).
    const title = await readProblemTitle(res);
    return title === AUTH_ERROR_CODES.EmailDeliveryUnavailable
      ? { ok: false, kind: "refused" }
      : unavailable;
  }
  if (res.status === 429) {
    return { ok: false, kind: "status", error: tp("auth.passwordless.errors.tooManyAttempts") };
  }
  if (res.status === 401) return { ok: false, kind: "notLoggedIn" };
  return unavailable;
}

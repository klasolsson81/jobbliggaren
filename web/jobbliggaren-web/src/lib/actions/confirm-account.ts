"use server";

import { getTranslations } from "next-intl/server";
import { env } from "@/lib/env";
import type { ActionResult } from "./_action-result";

/**
 * #714 — PUBLIC registration email-confirmation (the CONFIRM step of email-confirmation-first signup).
 * The activation link is opened from the account's own inbox and the visitor may be logged out, so
 * this action does NOT read the session (no `getSessionId`, no `authedFetch`). It POSTs the
 * `{ uid, token }` pair with a BARE `fetch` to `env.BACKEND_URL` and no Authorization header (mirrors
 * the #679 confirm-email-change action; there is no email here — the address is not changing).
 *
 * 204 -> EmailConfirmed is set server-side -> `{ success: true }`. Transient server failures (429 /
 * 5xx) and a transport throw map to the retryable network message; every other 4xx token-validity
 * failure (400/410) maps to the same uniform Swedish "invalid link" message so a bad/expired/unknown
 * token is indistinguishable (anti-enumeration). The backend body is NEVER read.
 *
 * SECURITY (§5): `uid` / `token` are single-use confirmation material and a leaked token could activate
 * the account. They are NEVER logged on any path (no `console.*`, no error-body read). The caller (the
 * /bekrafta-konto page + its client island) fires this ONLY on an explicit button click, never on load.
 */
export async function confirmAccountAction(
  uid: string,
  token: string
): Promise<ActionResult> {
  const t = await getTranslations("pages");

  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/auth/verify-email`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      cache: "no-store",
      body: JSON.stringify({ uid, token }),
    });

    if (res.status === 204) {
      return { success: true };
    }

    // Transient server-side failures (rate-limit / 5xx) are retryable → the network message.
    if (res.status === 429 || res.status >= 500) {
      return { success: false, error: t("auth.confirmAccount.networkError") };
    }

    // Any other non-204 is a 4xx token-validity failure (400/410): a uniform "invalid link" message
    // so a bad/expired/unknown token is indistinguishable (anti-enumeration). Body never surfaced.
    return { success: false, error: t("auth.confirmAccount.invalidBody") };
  } catch {
    return { success: false, error: t("auth.confirmAccount.networkError") };
  }
}

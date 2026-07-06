"use server";

import { getTranslations } from "next-intl/server";
import { env } from "@/lib/env";
import type { ActionResult } from "./_action-result";

/**
 * #679 — PUBLIC confirm-email-change (the second step of a self-service email
 * change). The confirmation link is opened from the NEW inbox and the visitor may
 * be logged out, so this action does NOT read the session (no `getSessionId`, no
 * `authedFetch`). It POSTs the `{ uid, email, token }` triple with a BARE `fetch`
 * to `env.BACKEND_URL` — mirroring how `getServerSession` reads the backend URL —
 * and no Authorization header.
 *
 * 204 -> the address is swapped and ALL sessions are logged out server-side ->
 * `{ success: true }`. 400 is uniform for any bad/expired/replayed token; every
 * non-204 maps to the same Swedish "invalid link" message (the backend body is
 * NEVER read — TD-10 / OWASP ASVS V8.2). A transport throw maps to a network message.
 *
 * SECURITY (§5): `uid` / `email` / `token` are single-use confirmation material and a
 * leaked token is an account-takeover primitive. They are NEVER logged on any path
 * (no `console.*`, no error-body read). The caller (the /bekrafta-epost page + its
 * client island) fires this ONLY on an explicit button click, never on page load.
 */
export async function confirmEmailChangeAction(
  uid: string,
  email: string,
  token: string
): Promise<ActionResult> {
  const t = await getTranslations("pages");

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/auth/confirm-email-change`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        cache: "no-store",
        body: JSON.stringify({ uid, email, token }),
      }
    );

    if (res.status === 204) {
      return { success: true };
    }

    // 400 uniform for any bad/expired/replayed token; treat every other non-204
    // status the same way. The backend body is never surfaced.
    return {
      success: false,
      error: t("auth.confirmEmailChange.invalidBody"),
    };
  } catch {
    return {
      success: false,
      error: t("auth.confirmEmailChange.networkError"),
    };
  }
}

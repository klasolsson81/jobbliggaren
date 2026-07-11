"use server";

import { getTranslations } from "next-intl/server";
import { env } from "@/lib/env";
import type { ActionResult } from "./_action-result";

/**
 * #733 — PUBLIC resend of the registration-confirmation link (the RESEND step of the
 * email-confirmation-first signup, #714). Fired from the login "email not confirmed" state and the
 * register "check your inbox" panel. Like confirm-account it does NOT read the session (the visitor is
 * logged out): a BARE `fetch` to `env.BACKEND_URL`, no Authorization header, POSTing only `{ email }`.
 *
 * The backend ALWAYS answers 202 (empty body) for any well-formed email — it never reveals whether an
 * unconfirmed account exists (anti-enumeration); a fresh link is (re)sent out-of-band only when it does.
 * 202 -> `{ success: true }`. Transient failures (429 rate-limit / 5xx) AND every other 4xx (e.g. a 400
 * malformed email) map to the SAME single uniform network-error message, and a transport throw maps
 * there too. One civic message on every non-202 path: the FE surfaces no signal about whether the
 * address was well-formed or whether an account exists. The backend body is NEVER read.
 *
 * SECURITY (§5): the email is user PII. It is NEVER logged on any path (no `console.*`, no error-body
 * read). The caller fires this ONLY on an explicit button click, never on load.
 */
export async function resendConfirmationAction(
  email: string
): Promise<ActionResult> {
  const t = await getTranslations("pages");

  try {
    const res = await fetch(
      `${env.BACKEND_URL}/api/v1/auth/resend-confirmation`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        cache: "no-store",
        body: JSON.stringify({ email }),
      }
    );

    if (res.status === 202) {
      return { success: true };
    }

    // Any non-202 collapses to one message: retryable transients (429 / 5xx) and a 400 malformed
    // email are indistinguishable to the user, so a bad address never becomes an enumeration oracle.
    return { success: false, error: t("auth.resendConfirmation.error") };
  } catch {
    return { success: false, error: t("auth.resendConfirmation.error") };
  }
}

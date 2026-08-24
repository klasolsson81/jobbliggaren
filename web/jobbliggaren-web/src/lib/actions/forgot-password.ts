"use server";

import { getTranslations } from "next-intl/server";
import { env } from "@/lib/env";
import type { RefusableActionResult } from "./_action-result";
import { forwardedHeaders } from "@/lib/http/forwarded-headers";
import { readProblemTitle } from "@/lib/http/problem";

/**
 * `RefusableActionResult` plus the address the caller just submitted, echoed on the FAILURE arm so
 * `ForgotPasswordForm` can re-seed its input: React 19 resets an uncontrolled `<form action={…}>`
 * after every action, so a failure otherwise empties the field the user must retype to retry.
 *
 * DERIVED from the shared union rather than restated — two near-identical hand-written unions drift
 * the moment either is edited, which is why `RefusableActionResult` itself is derived where it lives.
 * The success and refused panels replace the form, so neither carries an echo; the refused arm keeps
 * the property optional only because it shares the failure shape.
 *
 * No secret is a member. This form posts one field and it is not one.
 */
export type ForgotPasswordActionState =
  | Extract<RefusableActionResult, { success: true }>
  | (Extract<RefusableActionResult, { success: false }> & {
      values?: { email: string };
      // #1117's discriminator, for the one failure this action can attribute to the input: a
      // missing address. Absent means "not a field error" — a transport fault or a rate limit
      // must not mark an address the user typed correctly as invalid.
      field?: "email";
    });

/**
 * #1171 — PUBLIC forgot-password request. The requester has lost access by definition, so this action
 * reads no session (no `getSessionId`, no `authedFetch`) and sends no Authorization header.
 *
 * The backend answers a uniform 202 for a known address, an unknown one and a cooled repeat alike, so
 * SUCCESS HERE MEANS "the request was accepted", never "an account exists". The copy the caller renders
 * must stay conditional ("Om adressen hör till ett konto…") — saying "vi har skickat en länk till din
 * adress" would confirm existence in the UI after the API deliberately refused to.
 *
 * The 503 arm is CONJUNCTIVE: `res.status === 503` AND the exact ProblemDetails title. A status-only arm
 * would print "e-post är inte aktiverat" during an unrelated incident and mask it — this route has other
 * 503 producers whose bodies look nothing alike (`SessionStoreUnavailableException` writes `{ error }`
 * with no `title` key; a reverse proxy writes no JSON at all). Both counterfactuals are pinned in
 * `forgot-password.test.ts`; do not relax this to a bare status check.
 *
 * SECURITY (§5): the address is never logged on any path, and the backend body is read at most ONCE
 * (`readProblemTitle` consumes it) and never rendered — `detail` can carry server text.
 */
export async function requestPasswordResetAction(
  _prev: ForgotPasswordActionState | null,
  formData: FormData
): Promise<ForgotPasswordActionState> {
  const t = await getTranslations("pages");

  const email = (formData.get("email") as string | null)?.trim() ?? "";
  // The trimmed address, which is the one that was actually sent — re-seeding the raw input would
  // hand back whitespace the request did not carry.
  const values = { email };
  if (!email) {
    return {
      success: false,
      error: t("auth.actions.passwordResetEmailRequired"),
      values,
      field: "email",
    };
  }

  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/auth/forgot-password`, {
      method: "POST",
      headers: { ...(await forwardedHeaders()), "Content-Type": "application/json" },
      cache: "no-store",
      body: JSON.stringify({ email }),
    });

    if (res.status === 202) {
      return { success: true };
    }

    if (res.status === 503) {
      const title = await readProblemTitle(res);
      if (title === "Auth.EmailDeliveryUnavailable") {
        // `refused`: blocked by deployment configuration, so no retry with different input can
        // succeed until an operator sets a real Email:Provider. The form removes its own submit
        // affordance rather than leaving a live button on a request that cannot work.
        return {
          success: false,
          refused: true,
          error: t("auth.actions.emailDeliveryUnavailable"),
        };
      }
      // Deliberately no `else`: an unrecognised 503 is not ours and falls through to the generic
      // message below, which is literally true for it.
    }

    // 429 and every other non-202 collapse to one message. A malformed address is the backend's only
    // 400 and is existence-independent, so nothing here needs to distinguish it — and collapsing keeps
    // the frontend from inventing a signal the API refused to give.
    return { success: false, error: t("auth.actions.passwordResetFailed"), values };
  } catch {
    return { success: false, error: t("auth.actions.serverUnreachable"), values };
  }
}

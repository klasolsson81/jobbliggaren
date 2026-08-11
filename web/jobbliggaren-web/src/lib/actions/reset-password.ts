"use server";

import { getTranslations } from "next-intl/server";
import { env } from "@/lib/env";
import { forwardedHeaders } from "@/lib/http/forwarded-headers";
import { readProblemBody } from "@/lib/http/problem";

/**
 * The result of {@link resetPasswordAction}. A flag bag rather than a discriminated union, matching
 * `AuthActionState`, because `useActionState` consumes it. `done` and `error` are mutually exclusive in
 * practice; the form renders `done` first.
 */
export type ResetPasswordActionState =
  | { error?: string; done?: true; linkDead?: true }
  | null;

/**
 * #1171 — PUBLIC password reset (the APPLY step). The link is opened from the account's own inbox with
 * no session, so the token IS the authorization: no `getSessionId`, no Authorization header.
 *
 * 204 -> the password is changed and every session is torn down server-side -> `{ done: true }`. NO
 * session is issued, so the user logs in afterwards; that is the `/confirm-email-change` precedent and
 * is deliberate — the client that opened the link is not necessarily the user's device.
 *
 * The 400 arm discriminates, which is safe here and would not be on the request half: the backend
 * reaches a PASSWORD error only after verifying the token, so naming the broken rule tells the holder
 * of a valid token nothing they do not already have, while every TOKEN rejection collapses to one
 * uniform message. A real user needs to know which rule they broke; an attacker without a token never
 * sees either. It reads TWO shapes rather than an exact title whitelist alone, because the backend
 * emits two on 400 — see the arm itself.
 *
 * `linkDead` splits the failure channel by RETRYABILITY, which is what the caller renders on. A token
 * rejection cannot be fixed by anything the user types here, so the page replaces the form and offers
 * a way to request a new link; a password rejection can, and leaves the form intact — the link itself
 * survives it. Same distinction, and the same reason, as `refused` on the request half.
 *
 * SECURITY (§5): `uid`, `token` and the password are NEVER logged on any path, the body is read at most
 * ONCE (`readProblemBody` consumes it) and backend `detail` is never rendered. The caller page carries
 * `robots: noindex` because the URL holds a single-use credential.
 */
export async function resetPasswordAction(
  _prev: ResetPasswordActionState,
  formData: FormData
): Promise<ResetPasswordActionState> {
  const t = await getTranslations("pages");

  const uid = (formData.get("uid") as string | null) ?? "";
  const token = (formData.get("token") as string | null) ?? "";
  const newPassword = (formData.get("newPassword") as string | null) ?? "";

  if (!uid || !token) {
    return { error: t("auth.resetPassword.invalidBody"), linkDead: true };
  }
  if (newPassword.length < 12) {
    // Client friction only; the server is authoritative and enforces the same floor.
    return { error: t("auth.resetPassword.passwordTooShort") };
  }

  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/auth/reset-password`, {
      method: "POST",
      headers: { ...(await forwardedHeaders()), "Content-Type": "application/json" },
      cache: "no-store",
      body: JSON.stringify({ uid, token, newPassword }),
    });

    if (res.status === 204) {
      return { done: true };
    }

    if (res.status === 400) {
      // The body is read ONCE and inspected for two different shapes, because the backend produces
      // two: ProblemDetails with a `title` (a DomainError from the handler) and the
      // ValidationException shape `{ errors: { Field: [...] } }` (FluentValidation, which runs
      // BEFORE the handler). Measured, not assumed: a too-short password never reaches the handler,
      // because ResetPasswordCommandValidator's shared Password() rule carries the same 12-character
      // floor as Identity, so ValidationBehavior fells it first and no `Auth.PasswordTooShort` title
      // is ever emitted on this route.
      const body = await readProblemBody(res);

      if (body?.title === "Auth.PwnedPassword") {
        return { error: t("auth.actions.passwordBreached") };
      }

      // A validation failure naming the password field means the PASSWORD was rejected, not the link.
      // Getting this wrong is the harmful direction: telling someone holding a valid token that their
      // link is broken sends them to request a new one they do not need.
      if (body?.errors && "NewPassword" in body.errors) {
        return { error: t("auth.resetPassword.passwordTooShort") };
      }

      // Everything else is a token rejection, uniform by construction on the backend. `linkDead`
      // marks it NON-RETRYABLE: no password the user types on this page can make a spent or expired
      // token work, so the caller replaces the form rather than leaving a live button on a dead link.
      // The password arms above deliberately do NOT set it — those ARE retryable, because the token
      // survives a rejected password.
      return { error: t("auth.resetPassword.invalidBody"), linkDead: true };
    }

    // Transient server-side failures (rate-limit / 5xx) are retryable, so they get the network message
    // rather than the "invalid link" one — telling a user their link is broken when the server merely
    // hiccupped sends them to request a second link they do not need.
    if (res.status === 429 || res.status >= 500) {
      return { error: t("auth.resetPassword.networkError") };
    }

    return { error: t("auth.resetPassword.invalidBody"), linkDead: true };
  } catch {
    return { error: t("auth.resetPassword.networkError") };
  }
}

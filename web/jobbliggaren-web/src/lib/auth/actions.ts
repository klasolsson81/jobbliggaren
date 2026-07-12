"use server";

import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { deleteSessionCookie, setSessionCookie } from "@/lib/auth/session";
import { SESSION_COOKIE_NAME } from "@/lib/auth/cookie-names";
import { env } from "@/lib/env";
import {
  registrationValidationErrorSchema,
  sessionResponseSchema,
} from "@/lib/dto/auth";
import { parseResponse } from "@/lib/dto/_helpers";

// F6 P5 Punkt 4 svans-PR3 (2026-05-24, Klas-feedback "kom direkt till jobb"):
// /jobb och rot / hoppar över next-param och defaultar till /oversikt.
// Skäl: proxy-flödet redirektar unauth user från /jobb → /logga-in?next=/jobb,
// vilket bevarade /jobb som login-target trots Klas-intent "/oversikt är start-
// sidan". Andra deep links (/ansokningar/abc-123, /cv/xyz) respekteras fortfarande
// — användare som faktiskt klickat en deep link ska komma dit, men "passiv"
// landning på jobb-listan ska gå till /oversikt.
const HOME_REDIRECT_PATHS = new Set<string>(["/", "/jobb"]);

function safeRedirectPath(raw: string | null): string {
  if (
    raw &&
    raw.startsWith("/") &&
    !raw.startsWith("//") &&
    !raw.startsWith("/\\") &&
    !HOME_REDIRECT_PATHS.has(raw)
  ) {
    return raw;
  }
  return "/oversikt";
}

export type AuthActionState = {
  error?: string;
  // #714: set by registerAction on the email-confirmation-first path (HTTP 202). The form then shows
  // a "check your inbox" panel instead of an error. Identical for a fresh or a taken address — the
  // out-of-band email is the only differentiator, so the FE never distinguishes them.
  pendingConfirmation?: boolean;
  // #733: set alongside the login 403 gate (Auth.EmailNotConfirmed) so LoginForm can offer a
  // "resend confirmation link" action. Only reachable with a correct password, so not an oracle.
  emailNotConfirmed?: boolean;
  // #733: the submitted email echoed back so the resend action can read it from the action state.
  // Both consumers rely on this rather than the input: the register check-inbox panel unmounts the
  // form, and the login-403 gate keeps the form mounted but React 19 resets its live input (#791).
  // Lives only in the returned action state; never logged.
  email?: string;
} | null;

export async function loginAction(
  _prevState: AuthActionState,
  formData: FormData
): Promise<AuthActionState> {
  const t = await getTranslations("pages");
  const email = formData.get("email") as string | null;
  const password = formData.get("password") as string | null;
  // A native checkbox posts "on" when checked, nothing when unchecked — a pure
  // boolean opt-in ("Håll mig inloggad"), no client-supplied duration.
  const rememberMe = formData.get("rememberMe") === "on";
  const next = safeRedirectPath(formData.get("next") as string | null);

  if (!email || !password) {
    return { error: t("auth.actions.credentialsRequired") };
  }

  let sessionId: string;

  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/auth/login`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email, password, rememberMe }),
      cache: "no-store",
    });

    if (res.status === 401) {
      return { error: t("auth.actions.loginFailed") };
    }
    // #714: an unconfirmed account whose password is correct is gated with a distinct 403
    // (Auth.EmailNotConfirmed) — actionable copy tells the user to confirm their email. Only reachable
    // with a valid password, so it is not an enumeration oracle (a wrong password stays a 401 above).
    // #733: flag the state so LoginForm can render the resend-confirmation-link action.
    if (res.status === 403) {
      // #733/#791: echo the submitted email so LoginForm's resend button reads it from the action
      // state. React 19 resets the (uncontrolled) form after the action completes, clearing the live
      // email input — reading it at click time would yield "" and the resend would silently no-op.
      // Uniform-safe: the 403 is only reachable with a correct password (a wrong one stays 401 above),
      // so echoing the address the caller just proved they own introduces no enumeration oracle.
      return {
        error: t("auth.actions.emailNotConfirmed"),
        emailNotConfirmed: true,
        email,
      };
    }
    if (!res.ok) {
      return { error: t("auth.actions.unexpectedError") };
    }

    const data = await parseResponse(
      res,
      sessionResponseSchema,
      "POST /api/v1/auth/login"
    );
    sessionId = data.sessionId;
  } catch {
    return { error: t("auth.actions.serverUnreachable") };
  }

  await setSessionCookie(sessionId, rememberMe);
  redirect(next);
}

export async function registerAction(
  _prevState: AuthActionState,
  formData: FormData
): Promise<AuthActionState> {
  const t = await getTranslations("pages");
  const displayName = formData.get("displayName") as string | null;
  const email = formData.get("email") as string | null;
  const password = formData.get("password") as string | null;
  // Same opt-in as login: checked native checkbox posts "on", unchecked posts nothing.
  const rememberMe = formData.get("rememberMe") === "on";
  const next = safeRedirectPath(formData.get("next") as string | null);

  if (!displayName || !email || !password) {
    return { error: t("auth.actions.registrationFieldsRequired") };
  }

  let sessionId: string;

  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/auth/register`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ displayName, email, password, rememberMe }),
      cache: "no-store",
    });

    if (res.status === 400) {
      try {
        // ONE body read covers both 400 shapes (see registrationValidationErrorSchema).
        const errorBody = await parseResponse(
          res,
          registrationValidationErrorSchema,
          "POST /api/v1/auth/register (400)"
        );
        // #616 — a breached password can never be caught client-side, so the machine code
        // must map to localized copy here (NIST "provide the reason"). Exact-whitelist
        // comparison only; ProblemDetails text is never rendered.
        if (errorBody.title === "Auth.PwnedPassword") {
          return { error: t("auth.actions.passwordBreached") };
        }
        const firstError = errorBody.errors
          ? Object.values(errorBody.errors).flat()[0]
          : null;
        return { error: firstError ?? t("auth.actions.registrationFailed") };
      } catch {
        return { error: t("auth.actions.registrationFailed") };
      }
    }
    // #714: email-confirmation-first — a 202 means "we sent a confirmation link" and NO session was
    // issued, byte-identical for a fresh and a taken address (the account-enumeration status oracle is
    // closed; the only signal is the out-of-band email). Show the pending-confirmation panel instead of
    // logging in. Intercepted BEFORE the sessionResponseSchema parse (a 202 has no sessionId, which
    // would otherwise throw and surface a misleading "server unreachable"). On the legacy instant-login
    // path (flag OFF) the backend returns 200 + sessionId and the flow below runs unchanged.
    if (res.status === 202) {
      // #733: echo the submitted email so the check-inbox panel can resend the link (the form, and
      // thus its email input, unmounts on 202). Uniform across fresh/taken addresses — no oracle.
      return { pendingConfirmation: true, email };
    }
    if (!res.ok) {
      return { error: t("auth.actions.unexpectedError") };
    }

    const data = await parseResponse(
      res,
      sessionResponseSchema,
      "POST /api/v1/auth/register"
    );
    sessionId = data.sessionId;
  } catch {
    return { error: t("auth.actions.serverUnreachable") };
  }

  await setSessionCookie(sessionId, rememberMe);
  redirect(next);
}

export async function logoutAction(): Promise<void> {
  const cookieStore = await cookies();
  const sessionId = cookieStore.get(SESSION_COOKIE_NAME)?.value;

  if (sessionId) {
    try {
      const res = await fetch(`${env.BACKEND_URL}/api/v1/auth/logout`, {
        method: "POST",
        headers: { Authorization: `Bearer ${sessionId}` },
        cache: "no-store",
      });
      // Best-effort logout: backend-session försvinner via Redis-TTL (14d) om
      // anropet failar. Strukturerad warning så vi kan upptäcka systematiska
      // fel (TD-6) — ingen PII loggad (session-id är pseudonym).
      if (!res.ok) {
        console.error("logout.backend_call_failed", {
          event: "logout",
          status: res.status,
        });
      }
    } catch (cause) {
      console.error("logout.backend_call_failed", {
        event: "logout",
        cause: cause instanceof Error ? cause.message : String(cause),
      });
    }
  }

  await deleteSessionCookie();
  redirect("/logga-in");
}

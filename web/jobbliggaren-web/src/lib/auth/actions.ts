"use server";

import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { deleteSessionCookie, setSessionCookie } from "@/lib/auth/session";
import { SESSION_COOKIE_NAME } from "@/lib/auth/cookie-names";
import { env } from "@/lib/env";
import {
  problemTitleSchema,
  registrationValidationErrorSchema,
  sessionResponseSchema,
} from "@/lib/dto/auth";
import { parseResponse } from "@/lib/dto/_helpers";
import { forwardedHeaders } from "@/lib/http/forwarded-headers";

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
  // ADR 0083 Amendment 2026-08-03: set by registerAction when the backend refuses because the
  // public-registration kill-switch is closed. Its own state rather than `error` — the form renders
  // it as a role="status" panel in place of itself, like pendingConfirmation, because a deliberate
  // pre-launch state is not a validation failure and must not invite a retry that cannot succeed.
  registrationsClosed?: boolean;
  // #733: set alongside the login 403 gate (Auth.EmailNotConfirmed) so LoginForm can offer a
  // "resend confirmation link" action. Only reachable with a correct password, so not an oracle.
  emailNotConfirmed?: boolean;
  // #733: the submitted email echoed back so the resend action can read it from the action state.
  // Both consumers rely on this rather than the input: the register check-inbox panel unmounts the
  // form, and the login-403 gate keeps the form mounted but React 19 resets its live input (#791).
  // Lives only in the returned action state; never logged.
  email?: string;
  // #1117: names the ONE input an error belongs to, so the form can wire aria-invalid and
  // aria-describedby to that field and move focus there. Opt-in and absent by default — absent
  // means "not a field error" (network, server unreachable, the kill-switch), exactly the
  // semantics ForgotPasswordForm already reads off `!state.refused`.
  field?: "displayName" | "acceptTerms" | "password";
  // The non-secret fields just submitted, echoed back so the form can re-seed its own inputs.
  // React 19 resets an uncontrolled `<form action={…}>` after EVERY action, so without this a
  // failed submit destroys the name, the address and the ticked terms box, and the user retypes
  // the whole form to retry one wrong character.
  //
  // `password` is NOT a member and must never become one: it is deliberately never re-seeded, so
  // echoing it would carry a plaintext secret through a payload nothing reads. Like `email` above
  // this lives only in the returned action state and is never logged.
  //
  // Set on FAILURE states only. The 202 check-inbox panel and the registrations-closed panel each
  // replace the form, so an echo there would feed inputs that are no longer mounted.
  values?: {
    displayName?: string;
    email?: string;
    rememberMe?: boolean;
    acceptTerms?: boolean;
  };
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

  // Built once and attached to every failure return below — see `values` on AuthActionState.
  // `password` is absent by construction, not by omission at each site.
  const values = { email: email ?? "", rememberMe };

  if (!email || !password) {
    return { error: t("auth.actions.credentialsRequired"), values };
  }

  let sessionId: string;

  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/auth/login`, {
      method: "POST",
      headers: { ...(await forwardedHeaders()), "Content-Type": "application/json" },
      body: JSON.stringify({ email, password, rememberMe }),
      cache: "no-store",
    });

    if (res.status === 401) {
      return { error: t("auth.actions.loginFailed"), values };
    }
    // #714: an unconfirmed account whose password is correct is gated with a distinct 403
    // (Auth.EmailNotConfirmed) — actionable copy tells the user to confirm their email. Only reachable
    // with a valid password, so it is not an enumeration oracle (a wrong password stays a 401 above).
    // #733: flag the state so LoginForm can render the resend-confirmation-link action.
    if (res.status === 403) {
      // #733/#791: echo the submitted email so LoginForm's resend button reads the address the
      // server actually received, not whatever the live input holds at click time.
      // Uniform-safe: the 403 is only reachable with a correct password (a wrong one stays 401 above),
      // so echoing the address the caller just proved they own introduces no enumeration oracle.
      return {
        error: t("auth.actions.emailNotConfirmed"),
        emailNotConfirmed: true,
        email,
        values,
      };
    }
    if (!res.ok) {
      return { error: t("auth.actions.unexpectedError"), values };
    }

    const data = await parseResponse(
      res,
      sessionResponseSchema,
      "POST /api/v1/auth/login"
    );
    sessionId = data.sessionId;
  } catch {
    return { error: t("auth.actions.serverUnreachable"), values };
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
  // #1479: read the same way, but this one is a REQUIREMENT rather than an opt-in.
  const acceptTerms = formData.get("acceptTerms") === "on";
  const next = safeRedirectPath(formData.get("next") as string | null);

  // Same construction as loginAction: one non-secret echo, attached to every failure return.
  // The form posts no rememberMe (#1478), so nothing sets it here.
  const values = {
    displayName: displayName ?? "",
    email: email ?? "",
    acceptTerms,
  };

  if (!displayName || !email || !password) {
    return { error: t("auth.actions.registrationFieldsRequired"), values };
  }

  // #1479: the checkbox carries `required`, so a browser blocks this submit before it is sent.
  // The gate is repeated here because the Server Action is the boundary an ordinary POST reaches
  // without passing through the form at all, and refused BEFORE the fetch: an account created
  // without the acceptance is exactly what this must not produce.
  if (!acceptTerms) {
    return { error: t("auth.actions.termsRequired"), field: "acceptTerms", values };
  }

  let sessionId: string;

  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/auth/register`, {
      method: "POST",
      headers: { ...(await forwardedHeaders()), "Content-Type": "application/json" },
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
          // Names the password input: this refusal is about that one field and is fixed by
          // changing it, the same wiring `reset-password` gives the identical refusal.
          return {
            error: t("auth.actions.passwordBreached"),
            field: "password",
            values,
          };
        }
        // #1117 — the display-name personnummer refusal is an aggregate invariant, so it
        // arrives as a ProblemDetails `title` and NOT in the FluentValidation `errors` dict the
        // fallthrough below reads. Without this arm the user gets the generic "registration
        // failed" for a refusal that names exactly what to change. Same exact-whitelist rule as
        // the arm above: compared, never rendered.
        if (errorBody.title === "JobSeeker.DisplayNamePersonnummerMustBeRemoved") {
          return {
            error: t("auth.actions.displayNamePersonnummer"),
            field: "displayName",
            values,
          };
        }
        const firstError = errorBody.errors
          ? Object.values(errorBody.errors).flat()[0]
          : null;
        return { error: firstError ?? t("auth.actions.registrationFailed"), values };
      } catch {
        return { error: t("auth.actions.registrationFailed"), values };
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
    // ADR 0083 Amendment 2026-08-03 — public registration is deliberately held closed. Caught BEFORE
    // the !res.ok fallthrough, which would render "ett oväntat fel uppstod" for a state that is
    // neither unexpected nor an error (§10: informative, non-blaming). Uniform for every address:
    // the backend gate never reads the submitted email, so this branch carries no enumeration signal.
    //
    // Discriminated on the ProblemDetails TITLE, not on the status: this endpoint has a SECOND 503
    // producer. `Program.cs` maps SessionStoreUnavailableException to 503 across the whole pipeline,
    // and the open instant-login path calls sessionStore.CreateAsync — so a Redis outage during an
    // OPEN registration would otherwise render "registration is not open yet", which is false and
    // masks the incident. A reverse proxy in front of the API can produce a 503 of its own too. Both
    // fall through to serverUnreachable, which is literally true for them. Same exact-whitelist
    // discipline the 400 path applies to "Auth.PwnedPassword" twelve lines up.
    if (res.status === 503) {
      let title: string | undefined;
      try {
        const problem = await parseResponse(
          res,
          problemTitleSchema,
          "POST /api/v1/auth/register (503)"
        );
        title = problem.title;
      } catch {
        // A 503 with an unparseable body is not ours — fall through.
      }
      if (title === "Auth.RegistrationsClosed") {
        // Returned as its own state, not as `error`: RegisterForm renders it in a role="status"
        // panel in place of the form, mirroring the 202 check-inbox branch. The error channel is
        // red, assertive and keeps the submit button live, which would invite a retry that cannot
        // succeed until a config flag is flipped.
        return { registrationsClosed: true };
      }
      // Any other 503 on this route is transport, not policy — an API container restarting, an
      // upstream timeout, a deploy window. "Kunde inte nå servern" is literally true for those and
      // invites the retry that will actually work; unexpectedError would be vaguer for no gain.
      return { error: t("auth.actions.serverUnreachable"), values };
    }
    if (!res.ok) {
      return { error: t("auth.actions.unexpectedError"), values };
    }

    const data = await parseResponse(
      res,
      sessionResponseSchema,
      "POST /api/v1/auth/register"
    );
    sessionId = data.sessionId;
  } catch {
    return { error: t("auth.actions.serverUnreachable"), values };
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
        headers: { ...(await forwardedHeaders()), Authorization: `Bearer ${sessionId}` },
        cache: "no-store",
      });
      // Best-effort logout: backend-session försvinner via Redis-TTL (14d) om
      // anropet failar. Strukturerad warning så vi kan upptäcka systematiska
      // fel (TD-6) — ingen PII loggad (session-id är pseudonym).
      if (!res.ok) {
        // An event name and a status code; the session id stays out of it.
        // eslint-disable-next-line no-console
        console.error("logout.backend_call_failed", {
          event: "logout",
          status: res.status,
        });
      }
    } catch (cause) {
      // The message, never the Error itself: a thrown Error prints its stack.
      // eslint-disable-next-line no-console
      console.error("logout.backend_call_failed", {
        event: "logout",
        cause: cause instanceof Error ? cause.message : String(cause),
      });
    }
  }

  await deleteSessionCookie();
  redirect("/logga-in");
}

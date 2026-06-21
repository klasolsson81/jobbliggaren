"use server";

import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { deleteSessionCookie, setSessionCookie } from "@/lib/auth/session";
import { env } from "@/lib/env";
import {
  registrationValidationErrorSchema,
  sessionResponseSchema,
} from "@/lib/dto/auth";
import { parseResponse } from "@/lib/dto/_helpers";

// F6 P5 Punkt 4 svans-PR3 (2026-05-24, Klas-feedback "kom direkt till jobb"):
// /jobb och rot / hoppar över next-param och defaultar till /oversikt.
// Skäl: middleware-flödet redirektar unauth user från /jobb → /logga-in?next=/jobb,
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
} | null;

export async function loginAction(
  _prevState: AuthActionState,
  formData: FormData
): Promise<AuthActionState> {
  const t = await getTranslations("pages");
  const email = formData.get("email") as string | null;
  const password = formData.get("password") as string | null;
  const next = safeRedirectPath(formData.get("next") as string | null);

  if (!email || !password) {
    return { error: t("auth.actions.credentialsRequired") };
  }

  let sessionId: string;

  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/auth/login`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email, password }),
      cache: "no-store",
    });

    if (res.status === 401) {
      return { error: t("auth.actions.loginFailed") };
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

  await setSessionCookie(sessionId);
  redirect(next);
}

export async function registerAction(
  _prevState: AuthActionState,
  formData: FormData
): Promise<AuthActionState> {
  const t = await getTranslations("pages");
  const email = formData.get("email") as string | null;
  const password = formData.get("password") as string | null;
  const next = safeRedirectPath(formData.get("next") as string | null);

  if (!email || !password) {
    return { error: t("auth.actions.credentialsRequired") };
  }

  let sessionId: string;

  try {
    const res = await fetch(`${env.BACKEND_URL}/api/v1/auth/register`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email, password }),
      cache: "no-store",
    });

    if (res.status === 400) {
      try {
        const errorBody = await parseResponse(
          res,
          registrationValidationErrorSchema,
          "POST /api/v1/auth/register (400)"
        );
        const firstError = errorBody.errors
          ? Object.values(errorBody.errors).flat()[0]
          : null;
        return { error: firstError ?? t("auth.actions.registrationFailed") };
      } catch {
        return { error: t("auth.actions.registrationFailed") };
      }
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

  await setSessionCookie(sessionId);
  redirect(next);
}

export async function logoutAction(): Promise<void> {
  const cookieStore = await cookies();
  const sessionId = cookieStore.get("__Host-jobbliggaren_session")?.value;

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

"use server";

import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { deleteSessionCookie, setSessionCookie } from "@/lib/auth/session";
import { env } from "@/lib/env";
import {
  registrationValidationErrorSchema,
  sessionResponseSchema,
} from "@/lib/dto/auth";
import { parseResponse } from "@/lib/dto/_helpers";

function safeRedirectPath(raw: string | null): string {
  if (
    raw &&
    raw.startsWith("/") &&
    !raw.startsWith("//") &&
    !raw.startsWith("/\\")
  ) {
    return raw;
  }
  return "/mig";
}

export type AuthActionState = {
  error?: string;
} | null;

export async function loginAction(
  _prevState: AuthActionState,
  formData: FormData
): Promise<AuthActionState> {
  const email = formData.get("email") as string | null;
  const password = formData.get("password") as string | null;
  const next = safeRedirectPath(formData.get("next") as string | null);

  if (!email || !password) {
    return { error: "E-post och lösenord krävs." };
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
      return { error: "Inloggningen misslyckades. Kontrollera e-post och lösenord." };
    }
    if (!res.ok) {
      return { error: "Ett oväntat fel uppstod. Försök igen." };
    }

    const data = await parseResponse(
      res,
      sessionResponseSchema,
      "POST /api/v1/auth/login"
    );
    sessionId = data.sessionId;
  } catch {
    return { error: "Kunde inte nå servern. Försök igen." };
  }

  await setSessionCookie(sessionId);
  redirect(next);
}

export async function registerAction(
  _prevState: AuthActionState,
  formData: FormData
): Promise<AuthActionState> {
  const email = formData.get("email") as string | null;
  const password = formData.get("password") as string | null;
  const next = safeRedirectPath(formData.get("next") as string | null);

  if (!email || !password) {
    return { error: "E-post och lösenord krävs." };
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
        return { error: firstError ?? "Registreringen misslyckades." };
      } catch {
        return { error: "Registreringen misslyckades." };
      }
    }
    if (!res.ok) {
      return { error: "Ett oväntat fel uppstod. Försök igen." };
    }

    const data = await parseResponse(
      res,
      sessionResponseSchema,
      "POST /api/v1/auth/register"
    );
    sessionId = data.sessionId;
  } catch {
    return { error: "Kunde inte nå servern. Försök igen." };
  }

  await setSessionCookie(sessionId);
  redirect(next);
}

export async function logoutAction(): Promise<void> {
  const cookieStore = await cookies();
  const sessionId = cookieStore.get("__Host-jobbpilot_session")?.value;

  if (sessionId) {
    try {
      await fetch(`${env.BACKEND_URL}/api/v1/auth/logout`, {
        method: "POST",
        headers: { Authorization: `Bearer ${sessionId}` },
        cache: "no-store",
      });
    } catch {
      // Always delete the local cookie even if backend is unreachable
    }
  }

  await deleteSessionCookie();
  redirect("/logga-in");
}

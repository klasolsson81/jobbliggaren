import "server-only";
import { cache } from "react";
import { cookies } from "next/headers";
import { env } from "@/lib/env";
import { currentUserSchema, type CurrentUserDto } from "@/lib/dto/me";
import { parseResponse } from "@/lib/dto/_helpers";
import {
  PERSISTENT_MAX_AGE_SECONDS,
  SESSION_COOKIE_NAME,
} from "@/lib/auth/cookie-names";

// Re-exported so existing importers of `@/lib/auth/session` keep working; the
// literal now lives once in cookie-names.ts (importable by the non-server-only
// proxy) to avoid duplicating the security-critical cookie name.
export { SESSION_COOKIE_NAME };

// Roll-konstanter speglar backend `Roles`-class (Jobbliggaren.Application.Common.Authorization).
// Magic-string-anti-pattern undvikt på säkerhetskritisk åtkomstkontroll.
export const ROLES = {
  Admin: "Admin",
} as const;

export type Role = (typeof ROLES)[keyof typeof ROLES];

export async function getSessionId(): Promise<string | null> {
  const cookieStore = await cookies();
  return cookieStore.get(SESSION_COOKIE_NAME)?.value ?? null;
}

export type CurrentUser = CurrentUserDto;

/**
 * Hämtar den inloggade användaren för aktuell request.
 *
 * Wrappad i `React.cache()` — flera anrop inom samma request (t.ex.
 * `(app)/layout.tsx` + en page-fil) träffar samma cache och utför endast
 * **ett** backend-anrop. Detta är intentional pattern, inte duplicering:
 * varje (app)-sida anropar `getServerSession()` direkt för att verifiera
 * session + härleda user-data, oberoende av layout. Layout-prop-passing
 * via Server Component-context-trick avvisades (TD-5 CTO-triage 2026-05-11)
 * för att bevara SoC mellan layout (skal) och page (innehåll), och för
 * konsistens med övriga 7 (app)-sidor som använder samma pattern.
 *
 * Returnerar `null` vid avsaknad av session-cookie, backend-fel eller
 * DTO-parsningsfel — alla mappas till "ingen session" så middleware/page
 * kan redirecta till `/logga-in`.
 */
export const getServerSession = cache(
  async (): Promise<CurrentUser | null> => {
    const sessionId = await getSessionId();
    if (!sessionId) return null;

    try {
      const res = await fetch(`${env.BACKEND_URL}/api/v1/me`, {
        headers: { Authorization: `Bearer ${sessionId}` },
        cache: "no-store",
      });
      if (!res.ok) return null;
      return await parseResponse(res, currentUserSchema, "GET /api/v1/me");
    } catch {
      // Network errors and DtoParseError both map to "no session"
      return null;
    }
  }
);

/**
 * Sets the session cookie for a freshly issued session id.
 *
 * `persistent` mirrors the user's "Håll mig inloggad" choice:
 *  - `true`  → a persistent cookie with a finite Max-Age (the 180d absolute cap,
 *    PERSISTENT_MAX_AGE_SECONDS) so the login survives a browser restart. The
 *    backend stays the SSOT for expiry (30d sliding / 180d cap); the Max-Age is
 *    only the finite ceiling, never an infinite cookie.
 *  - `false` → Max-Age is omitted → a session cookie the browser drops on close.
 *    This is the privacy-by-default (Art. 25(2)): an unticked box must not leave
 *    a durable credential on the device.
 *
 * All other attributes are the non-negotiable `__Host-` requirements (HttpOnly,
 * Secure, SameSite=Strict, Path=/, no Domain) in both branches.
 */
export async function setSessionCookie(
  sessionId: string,
  persistent: boolean
): Promise<void> {
  const cookieStore = await cookies();
  cookieStore.set(SESSION_COOKIE_NAME, sessionId, {
    httpOnly: true,
    secure: true,
    sameSite: "strict",
    path: "/",
    ...(persistent ? { maxAge: PERSISTENT_MAX_AGE_SECONDS } : {}),
  });
}

export async function deleteSessionCookie(): Promise<void> {
  const cookieStore = await cookies();
  cookieStore.set(SESSION_COOKIE_NAME, "", {
    httpOnly: true,
    secure: true,
    sameSite: "strict",
    path: "/",
    maxAge: 0,
  });
}

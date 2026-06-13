import "server-only";
import { cache } from "react";
import { cookies } from "next/headers";
import { env } from "@/lib/env";
import { currentUserSchema, type CurrentUserDto } from "@/lib/dto/me";
import { parseResponse } from "@/lib/dto/_helpers";

export const SESSION_COOKIE_NAME = "__Host-jobbliggaren_session";
const MAX_AGE = 14 * 24 * 60 * 60; // 14 days in seconds

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

export async function setSessionCookie(sessionId: string): Promise<void> {
  const cookieStore = await cookies();
  cookieStore.set(SESSION_COOKIE_NAME, sessionId, {
    httpOnly: true,
    secure: true,
    sameSite: "strict",
    path: "/",
    maxAge: MAX_AGE,
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

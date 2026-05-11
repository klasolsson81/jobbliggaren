import "server-only";
import { cache } from "react";
import { cookies } from "next/headers";
import { env } from "@/lib/env";
import { currentUserSchema, type CurrentUserDto } from "@/lib/dto/me";
import { parseResponse } from "@/lib/dto/_helpers";

export const SESSION_COOKIE_NAME = "__Host-jobbpilot_session";
const MAX_AGE = 14 * 24 * 60 * 60; // 14 days in seconds

// Roll-konstanter speglar backend `Roles`-class (JobbPilot.Application.Common.Authorization).
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

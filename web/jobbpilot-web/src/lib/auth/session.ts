import "server-only";
import { cache } from "react";
import { cookies } from "next/headers";
import { env } from "@/lib/env";

export const SESSION_COOKIE_NAME = "__Host-jobbpilot_session";
const MAX_AGE = 14 * 24 * 60 * 60; // 14 days in seconds

export async function getSessionId(): Promise<string | null> {
  const cookieStore = await cookies();
  return cookieStore.get(SESSION_COOKIE_NAME)?.value ?? null;
}

export type CurrentUser = {
  userId: string;
  email: string;
  roles: readonly string[];
};

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
      const data: CurrentUser = await res.json();
      return data;
    } catch {
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

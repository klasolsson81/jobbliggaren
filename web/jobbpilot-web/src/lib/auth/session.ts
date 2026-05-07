import "server-only";
import { cache } from "react";
import { cookies } from "next/headers";
import { env } from "@/lib/env";

const COOKIE_NAME = "__Host-jobbpilot_session";
const MAX_AGE = 14 * 24 * 60 * 60; // 14 days in seconds

export type CurrentUser = {
  userId: string;
  email: string;
  roles: readonly string[];
};

export const getServerSession = cache(
  async (): Promise<CurrentUser | null> => {
    const cookieStore = await cookies();
    const sessionId = cookieStore.get(COOKIE_NAME)?.value;
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
  cookieStore.set(COOKIE_NAME, sessionId, {
    httpOnly: true,
    secure: true,
    sameSite: "strict",
    path: "/",
    maxAge: MAX_AGE,
  });
}

export async function deleteSessionCookie(): Promise<void> {
  const cookieStore = await cookies();
  cookieStore.set(COOKIE_NAME, "", {
    httpOnly: true,
    secure: true,
    sameSite: "strict",
    path: "/",
    maxAge: 0,
  });
}

"use server";

import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { deleteSessionCookie } from "@/lib/auth/session";
import { SESSION_COOKIE_NAME } from "@/lib/auth/cookie-names";
import { env } from "@/lib/env";
import { forwardedHeaders } from "@/lib/http/forwarded-headers";

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
      // Best-effort logout: backend-session försvinner via sin Redis-TTL om
      // anropet failar. Strukturerad warning så vi kan upptäcka systematiska
      // fel (TD-6).
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

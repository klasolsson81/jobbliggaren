"use server";

// DEV-ONLY throwaway tool — REMOVE BEFORE LAUNCH (Klas). Lets Klas re-test the
// onboarding flow from scratch: clears his own CVs / saved + recent searches and
// resets match preferences server-side (so the welcome modal re-triggers), clears
// the FE welcome cookie, then redirects to /oversikt. Never a product surface — the
// backend endpoint is mapped only in Development.

import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { env } from "@/lib/env";
import { getSessionId } from "@/lib/auth/session";
import { SETUP_WELCOMED_COOKIE } from "@/lib/onboarding/setup-welcome";

/**
 * DEV-ONLY (remove before launch). POSTs `/api/v1/dev/reset-my-data` with the
 * session Bearer (mirrors `deleteAccountAction`'s authed fetch), then clears the
 * `__Host-jobbliggaren_setup_welcomed` cookie so the welcome modal re-shows, then
 * redirects to `/oversikt`. The backend response body is never read (TD-10) — on a
 * non-2xx we redirect anyway; this is a local convenience, not a user-facing flow.
 */
export async function resetMyDataAction(): Promise<void> {
  const sessionId = await getSessionId();
  if (sessionId) {
    try {
      await fetch(`${env.BACKEND_URL}/api/v1/dev/reset-my-data`, {
        method: "POST",
        headers: { Authorization: `Bearer ${sessionId}` },
        cache: "no-store",
      });
    } catch {
      // Dev tool — swallow network errors; the redirect below still re-runs the
      // onboarding flow with whatever state was cleared.
    }
  }

  // Clear the FE welcome cookie (delete by setting maxAge 0). Attributes must match
  // the original set so the `__Host-` prefix accepts the overwrite.
  const cookieStore = await cookies();
  cookieStore.set(SETUP_WELCOMED_COOKIE, "", {
    httpOnly: true,
    secure: true,
    sameSite: "lax",
    path: "/",
    maxAge: 0,
  });

  redirect("/oversikt");
}

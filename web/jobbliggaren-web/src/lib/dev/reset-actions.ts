"use server";

// DEV-ONLY throwaway tool — REMOVE BEFORE LAUNCH (Klas), with the flag and the endpoint
// (docs/runbooks/release-checklist.md). Lets the onboarding flow be re-tested from scratch:
// clears the caller's own CV data, saved/recent searches, graded matches and match
// preferences server-side (so the welcome modal re-triggers), then clears the FE welcome
// cookie. Never a product surface.

import { cookies } from "next/headers";
import { getTranslations } from "next-intl/server";
import { revalidatePath } from "next/cache";
import { getSessionId } from "@/lib/auth/session";
import { authedFetch } from "@/lib/http/authed-fetch";
import { SETUP_WELCOMED_COOKIE } from "@/lib/onboarding/setup-welcome";
import type { ActionResult } from "@/lib/actions/_action-result";

/**
 * DEV-ONLY (remove before launch). POSTs `/api/v1/dev/reset-my-data` with the session
 * bearer, clears the `__Host-jobbliggaren_setup_welcomed` cookie so the welcome modal
 * re-shows, and revalidates `/oversikt`.
 *
 * <b>Errors are reported, not swallowed.</b> The previous version hand-rolled `fetch`,
 * caught everything, ignored the response and redirected regardless — so a failed reset
 * was indistinguishable from a successful one, and on a deployed box the most likely
 * failure is precisely the interesting one (the backend flag off while this one is on).
 * It now returns {@link ActionResult} like every other mutation and routes through
 * `authedFetch`, which owns the bearer/forwarded-header injection and never reads the
 * response body (TD-10).
 *
 * The cookie is cleared ONLY on success. Clearing it after a failed reset would re-open
 * the welcome modal over data that is still there, which is a worse lie than an error.
 */
export async function resetMyDataAction(): Promise<ActionResult> {
  const t = await getTranslations("common");

  const sessionId = await getSessionId();
  if (!sessionId) {
    return { success: false, error: t("dev.errors.notLoggedIn") };
  }

  let response: Response;
  try {
    response = await authedFetch(sessionId, "/api/v1/dev/reset-my-data", {
      method: "POST",
    });
  } catch {
    return { success: false, error: t("dev.errors.serverUnreachable") };
  }

  if (!response.ok) {
    // 404 is the informative one on a deployed box: the route is not mapped, which means
    // DevTools:EnableResetMyData is off on the backend. Naming it saves the next person
    // the round trip of wondering whether the button is broken.
    const error =
      response.status === 404
        ? t("dev.errors.disabled")
        : t("dev.errors.failed");
    return { success: false, error };
  }

  // Attributes must match the original set so the `__Host-` prefix accepts the overwrite.
  const cookieStore = await cookies();
  cookieStore.set(SETUP_WELCOMED_COOKIE, "", {
    httpOnly: true,
    secure: true,
    sameSite: "lax",
    path: "/",
    maxAge: 0,
  });

  revalidatePath("/oversikt");
  return { success: true };
}

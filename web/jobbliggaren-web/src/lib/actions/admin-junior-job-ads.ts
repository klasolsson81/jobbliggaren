"use server";

import { revalidatePath } from "next/cache";
import { getTranslations } from "next-intl/server";
import type { ActionResult } from "@/lib/actions/_action-result";
import {
  isJuniorChoice,
  isJuniorJobAdId,
  JUNIOR_ERRORS,
  JUNIOR_JOBS_ROUTE,
  type AdminJuniorChoice,
} from "@/lib/admin/junior-jobs";
import { juniorDecisionPath } from "@/lib/api/admin-junior-job-ads";
import { getServerSession, getSessionId, ROLES } from "@/lib/auth/session";
import { parseRetryAfter } from "@/lib/dto/_helpers";
import { wireJuniorDecision } from "@/lib/dto/admin-junior-job-ads";
import { authedFetch } from "@/lib/http/authed-fetch";
import { readProblemTitle } from "@/lib/http/problem";

async function juniorCopy() {
  return getTranslations("admin.juniorJobs");
}

type Translate = Awaited<ReturnType<typeof juniorCopy>>;

function refused(error: string): ActionResult {
  return { success: false, error };
}

/**
 * The session the decision runs as, or the refusal when it has none or lacks the Admin role. The role is read
 * again here: the layout's check covered the page the decision came from, not this request.
 */
async function adminSession(t: Translate): Promise<{ readonly sessionId: string } | { readonly refusal: string }> {
  const session = await getServerSession();
  if (!session) return { refusal: t("errors.unauthorized") };
  if (!session.roles.includes(ROLES.Admin)) return { refusal: t("errors.forbidden") };
  const sessionId = await getSessionId();
  return sessionId ? { sessionId } : { refusal: t("errors.unauthorized") };
}

/**
 * #2264 — decides whether one listed ad is a junior job, or hands it back to the rules (null). A decision that went
 * through revalidates the page, so the row leaves its tab or shows the new decision. A refusal the backend documents
 * changed nothing and revalidates nothing; an ad that is no longer listed keeps its row and the line that says so,
 * until the page is read again. A 5xx or a lost response may sit over a saved decision, so it claims nothing.
 */
export async function decideJuniorJobAdAction(id: string, decision: AdminJuniorChoice): Promise<ActionResult> {
  const t = await juniorCopy();
  if (!isJuniorJobAdId(id)) return refused(t("errors.gone"));
  // Declared unreachable for a typed caller: a direct Server Action POST can send anything.
  if (!isJuniorChoice(decision)) return refused(t("errors.refused"));

  const admin = await adminSession(t);
  if ("refusal" in admin) return refused(admin.refusal);

  let res: Response;
  try {
    res = await authedFetch(admin.sessionId, juniorDecisionPath(id), {
      method: "POST",
      body: JSON.stringify({ decision: wireJuniorDecision(decision) }),
    });
  } catch {
    return refused(t("errors.unknown"));
  }

  switch (res.status) {
    case 204:
      revalidatePath(JUNIOR_JOBS_ROUTE);
      return { success: true };
    case 400:
      return refused(t("errors.refused"));
    case 401:
      return refused(t("errors.unauthorized"));
    case 403:
      return refused(t("errors.forbidden"));
    case 404:
      return (await readProblemTitle(res)) === JUNIOR_ERRORS.notFound
        ? refused(t("errors.gone"))
        : refused(t("errors.unknown"));
    case 429:
      return refused(t("errors.rateLimited", { seconds: parseRetryAfter(res.headers.get("Retry-After")) }));
    default:
      return refused(t("errors.unknown"));
  }
}

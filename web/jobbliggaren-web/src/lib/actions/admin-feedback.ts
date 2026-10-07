"use server";

import { revalidatePath } from "next/cache";
import { getTranslations } from "next-intl/server";
import {
  FEEDBACK_ERRORS,
  FEEDBACK_ROUTE,
  isFeedbackId,
  isFeedbackStatus,
  type AdminFeedbackRefusal,
} from "@/lib/admin/feedback";
import type { AdminFeedbackStatus } from "@/lib/admin/view-models";
import { feedbackPath } from "@/lib/api/admin-feedback";
import { getServerSession, getSessionId, ROLES } from "@/lib/auth/session";
import { parseRetryAfter } from "@/lib/dto/_helpers";
import { wireFeedbackStatus } from "@/lib/dto/admin-feedback";
import { authedFetch } from "@/lib/http/authed-fetch";
import { readProblemTitle } from "@/lib/http/problem";

async function feedbackCopy() {
  return getTranslations("admin.feedback");
}

type Translate = Awaited<ReturnType<typeof feedbackCopy>>;

/**
 * The session the command runs as, or the refusal when it has none or lacks the Admin role. The role is
 * read again here: the layout's check covered the page the command came from, not this request.
 */
async function adminSession(t: Translate): Promise<{ readonly sessionId: string } | { readonly refusal: string }> {
  const session = await getServerSession();
  if (!session) return { refusal: t("errors.unauthorized") };
  if (!session.roles.includes(ROLES.Admin)) return { refusal: t("errors.forbidden") };
  const sessionId = await getSessionId();
  return sessionId ? { sessionId } : { refusal: t("errors.unauthorized") };
}

/** The answers both commands share: a session gone on the way, a missing role and a rate limit. */
function sharedRefusal(t: Translate, res: Response): string | null {
  switch (res.status) {
    case 401:
      return t("errors.unauthorized");
    case 403:
      return t("errors.forbidden");
    case 429:
      return t("errors.rateLimited", { seconds: parseRetryAfter(res.headers.get("Retry-After")) });
    default:
      return null;
  }
}

/**
 * #1979 — moves a submission to another status. A change that went through, and a submission that is no
 * longer there, revalidate the page, so it shows what the backend holds. A refusal the backend documents
 * changed nothing; a 5xx or a lost response may sit over a change that was saved, so it claims nothing.
 */
export async function changeFeedbackStatusAction(
  id: string,
  status: AdminFeedbackStatus,
): Promise<AdminFeedbackRefusal> {
  const t = await feedbackCopy();
  if (!isFeedbackId(id)) return t("errors.gone");
  // Declared unreachable for a typed caller: a direct Server Action POST can send anything.
  if (!isFeedbackStatus(status)) return t("errors.statusRefused");

  const admin = await adminSession(t);
  if ("refusal" in admin) return admin.refusal;

  let res: Response;
  try {
    res = await authedFetch(admin.sessionId, `${feedbackPath(id)}/status`, {
      method: "POST",
      body: JSON.stringify({ status: wireFeedbackStatus(status) }),
    });
  } catch {
    return t("errors.statusUnknown");
  }

  if (res.status === 204) {
    revalidatePath(FEEDBACK_ROUTE);
    return null;
  }
  const shared = sharedRefusal(t, res);
  if (shared !== null) return shared;
  switch (res.status) {
    case 400:
      return (await readProblemTitle(res)) === FEEDBACK_ERRORS.statusUnchanged
        ? t("detail.status.unchanged")
        : t("errors.statusRefused");
    case 404:
      if ((await readProblemTitle(res)) !== FEEDBACK_ERRORS.notFound) return t("errors.statusUnknown");
      revalidatePath(FEEDBACK_ROUTE);
      return t("errors.gone");
    default:
      return t("errors.statusUnknown");
  }
}

/**
 * #1979 — sends a submission's notice again. From Failed nothing was sent, so no acknowledgement is asked;
 * from an unknown outcome the earlier mail may have arrived, and the administrator acknowledges the risk of
 * a duplicate before this runs. A conflict means the notice moved on since the page was read, so the page is
 * revalidated to show where it is now; a 5xx or a lost response claims nothing.
 */
export async function requeueFeedbackNotificationAction(
  id: string,
  acknowledgeDuplicateRisk: boolean,
): Promise<AdminFeedbackRefusal> {
  const t = await feedbackCopy();
  if (!isFeedbackId(id)) return t("errors.gone");
  // Declared unreachable for a typed caller: a direct Server Action POST can send anything.
  if (typeof acknowledgeDuplicateRisk !== "boolean") return t("errors.noticeRefused");

  const admin = await adminSession(t);
  if ("refusal" in admin) return admin.refusal;

  let res: Response;
  try {
    res = await authedFetch(admin.sessionId, `${feedbackPath(id)}/notification/requeue`, {
      method: "POST",
      body: JSON.stringify({ acknowledgeDuplicateRisk }),
    });
  } catch {
    return t("errors.noticeUnknown");
  }

  if (res.status === 204) {
    revalidatePath(FEEDBACK_ROUTE);
    return null;
  }
  const shared = sharedRefusal(t, res);
  if (shared !== null) return shared;
  switch (res.status) {
    case 400:
      return t("errors.noticeRefused");
    case 404:
      if ((await readProblemTitle(res)) !== FEEDBACK_ERRORS.notFound) return t("errors.noticeUnknown");
      revalidatePath(FEEDBACK_ROUTE);
      return t("errors.gone");
    case 409: {
      const title = await readProblemTitle(res);
      revalidatePath(FEEDBACK_ROUTE);
      return title === FEEDBACK_ERRORS.notificationNotRequeueable
        ? t("errors.noticeAlreadyQueued")
        : t("errors.noticeChanged");
    }
    default:
      return t("errors.noticeUnknown");
  }
}

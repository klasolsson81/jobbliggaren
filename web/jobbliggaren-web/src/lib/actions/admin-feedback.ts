"use server";

import { revalidatePath } from "next/cache";
import { getTranslations } from "next-intl/server";
import {
  commandRefusal,
  FEEDBACK_ERRORS,
  FEEDBACK_REPLY_MAX,
  FEEDBACK_ROUTE,
  isFeedbackId,
  isFeedbackStatus,
  type AdminFeedbackRefusal,
  type AdminFeedbackReplyOutcome,
} from "@/lib/admin/feedback";
import type { AdminFeedbackStatus } from "@/lib/admin/view-models";
import { feedbackPath } from "@/lib/api/admin-feedback";
import { getServerSession, getSessionId, ROLES } from "@/lib/auth/session";
import { parseResponse, parseRetryAfter } from "@/lib/dto/_helpers";
import { feedbackReplyOutcomeSchema, toFeedbackReplyState, wireFeedbackStatus } from "@/lib/dto/admin-feedback";
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
 * Only the status the submission already has refuses the value chosen; every other refusal is the command's.
 */
export async function changeFeedbackStatusAction(
  id: string,
  status: AdminFeedbackStatus,
): Promise<AdminFeedbackRefusal> {
  const t = await feedbackCopy();
  if (!isFeedbackId(id)) return commandRefusal(t("errors.gone"));
  // Declared unreachable for a typed caller: a direct Server Action POST can send anything.
  if (!isFeedbackStatus(status)) return commandRefusal(t("errors.statusRefused"));

  const admin = await adminSession(t);
  if ("refusal" in admin) return commandRefusal(admin.refusal);

  let res: Response;
  try {
    res = await authedFetch(admin.sessionId, `${feedbackPath(id)}/status`, {
      method: "POST",
      body: JSON.stringify({ status: wireFeedbackStatus(status) }),
    });
  } catch {
    return commandRefusal(t("errors.statusUnknown"));
  }

  if (res.status === 204) {
    revalidatePath(FEEDBACK_ROUTE);
    return null;
  }
  const shared = sharedRefusal(t, res);
  if (shared !== null) return commandRefusal(shared);
  switch (res.status) {
    case 400:
      return (await readProblemTitle(res)) === FEEDBACK_ERRORS.statusUnchanged
        ? { text: t("detail.status.unchanged"), about: "value" }
        : commandRefusal(t("errors.statusRefused"));
    case 404:
      if ((await readProblemTitle(res)) !== FEEDBACK_ERRORS.notFound) return commandRefusal(t("errors.statusUnknown"));
      revalidatePath(FEEDBACK_ROUTE);
      return commandRefusal(t("errors.gone"));
    default:
      return commandRefusal(t("errors.statusUnknown"));
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
  if (!isFeedbackId(id)) return commandRefusal(t("errors.gone"));
  // Declared unreachable for a typed caller: a direct Server Action POST can send anything.
  if (typeof acknowledgeDuplicateRisk !== "boolean") return commandRefusal(t("errors.noticeRefused"));

  const admin = await adminSession(t);
  if ("refusal" in admin) return commandRefusal(admin.refusal);

  let res: Response;
  try {
    res = await authedFetch(admin.sessionId, `${feedbackPath(id)}/notification/requeue`, {
      method: "POST",
      body: JSON.stringify({ acknowledgeDuplicateRisk }),
    });
  } catch {
    return commandRefusal(t("errors.noticeUnknown"));
  }

  if (res.status === 204) {
    revalidatePath(FEEDBACK_ROUTE);
    return null;
  }
  const shared = sharedRefusal(t, res);
  if (shared !== null) return commandRefusal(shared);
  switch (res.status) {
    case 400:
      return commandRefusal(t("errors.noticeRefused"));
    case 404:
      if ((await readProblemTitle(res)) !== FEEDBACK_ERRORS.notFound) return commandRefusal(t("errors.noticeUnknown"));
      revalidatePath(FEEDBACK_ROUTE);
      return commandRefusal(t("errors.gone"));
    case 409: {
      const title = await readProblemTitle(res);
      revalidatePath(FEEDBACK_ROUTE);
      return commandRefusal(
        title === FEEDBACK_ERRORS.notificationNotRequeueable ? t("errors.noticeAlreadyQueued") : t("errors.noticeChanged"),
      );
    }
    case 410:
      if ((await readProblemTitle(res)) !== FEEDBACK_ERRORS.reporterUnavailable) return commandRefusal(t("errors.noticeUnknown"));
      revalidatePath(FEEDBACK_ROUTE);
      return commandRefusal(t("errors.noticeRefused"));
    default:
      return commandRefusal(t("errors.noticeUnknown"));
  }
}

/**
 * #2069 — mails a reply to the person behind a submission. A 200 is the reply's recorded outcome, whatever it was:
 * the reply was saved, so the page is revalidated to list it and the next reply takes a new key. A refusal the
 * backend documents saved nothing. Anything else, a lost response included, may sit over a saved reply; the
 * caller keeps the key, so sending again replays it instead of mailing twice.
 */
export async function sendFeedbackReplyAction(
  id: string,
  replyKey: string,
  text: string,
): Promise<AdminFeedbackReplyOutcome> {
  const t = await feedbackCopy();
  const refused = (message: string, about: "value" | "command" = "command"): AdminFeedbackReplyOutcome => ({
    kind: "refused",
    refusal: { text: message, about },
  });
  const unanswered: AdminFeedbackReplyOutcome = { kind: "unanswered", text: t("errors.replyUnknown") };
  if (!isFeedbackId(id)) return refused(t("errors.gone"));
  // Declared unreachable for a typed caller: a direct Server Action POST can send anything.
  if (!isFeedbackId(replyKey) || typeof text !== "string") return refused(t("errors.replyRefused"));
  if (text.trim() === "") return refused(t("detail.reply.empty"), "value");
  if (text.length > FEEDBACK_REPLY_MAX) return refused(t("detail.reply.tooLong", { max: FEEDBACK_REPLY_MAX }), "value");

  const admin = await adminSession(t);
  if ("refusal" in admin) return refused(admin.refusal);

  let res: Response;
  try {
    res = await authedFetch(admin.sessionId, `${feedbackPath(id)}/replies`, {
      method: "POST",
      body: JSON.stringify({ replyKey, text }),
    });
  } catch {
    return unanswered;
  }

  if (res.status === 200) {
    revalidatePath(FEEDBACK_ROUTE);
    try {
      const outcome = await parseResponse(res, feedbackReplyOutcomeSchema, "POST /api/v1/admin/feedback/{id}/replies");
      return { kind: "recorded", state: toFeedbackReplyState(outcome.state) };
    } catch {
      return unanswered;
    }
  }
  const shared = sharedRefusal(t, res);
  if (shared !== null) return refused(shared);
  const title = await readProblemTitle(res);
  switch (res.status) {
    case 400:
      switch (title) {
        case FEEDBACK_ERRORS.replyEmpty:
          return refused(t("detail.reply.empty"), "value");
        case FEEDBACK_ERRORS.replyTooLong:
          return refused(t("detail.reply.tooLong", { max: FEEDBACK_REPLY_MAX }), "value");
        case FEEDBACK_ERRORS.replyUnavailable:
          return refused(t("errors.replyUnavailable"));
        default:
          return refused(t("errors.replyRefused"));
      }
    case 404:
      if (title !== FEEDBACK_ERRORS.notFound) return unanswered;
      revalidatePath(FEEDBACK_ROUTE);
      return refused(t("errors.gone"));
    case 409:
      if (title !== FEEDBACK_ERRORS.replyLimitReached) return unanswered;
      revalidatePath(FEEDBACK_ROUTE);
      return refused(t("errors.replyLimit"));
    case 410:
      if (title === FEEDBACK_ERRORS.reporterUnavailable) return refused(t("errors.replyReporterGone"));
      if (title !== FEEDBACK_ERRORS.replyRemoved) return unanswered;
      revalidatePath(FEEDBACK_ROUTE);
      return refused(t("errors.gone"));
    default:
      return unanswered;
  }
}

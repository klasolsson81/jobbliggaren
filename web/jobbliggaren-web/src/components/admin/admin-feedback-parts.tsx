import { useTranslations } from "next-intl";
import { StatusDot, type StatusTone } from "@/components/ui/status-dot";
import { isFeedbackPageKey } from "@/lib/admin/feedback";
import type { AdminFeedbackNoticeState, AdminFeedbackStatus } from "@/lib/admin/view-models";

/**
 * The pieces the feedback list (a Server Component on the live page) and the open submission (a Client
 * Component) share. No directive: each renders on whichever side imports it.
 */

/** Ny needs the administrator's attention; Avstår is closed without action, so it is neutral. */
const STATUS_TONE: Readonly<Record<AdminFeedbackStatus, string>> = {
  new: "jp-pill--warning",
  inProgress: "jp-pill--info",
  resolved: "jp-pill--success",
  declined: "jp-pill--neutral",
};

const NOTICE_TONE: Readonly<Record<AdminFeedbackNoticeState, StatusTone>> = {
  queued: "neutral",
  sending: "info",
  accepted: "success",
  failed: "danger",
  unknown: "warning",
};

export function FeedbackStatusPill({ status }: { readonly status: AdminFeedbackStatus }) {
  const t = useTranslations("admin.feedback.status");
  return <span className={`jp-pill ${STATUS_TONE[status]}`}>{t(status)}</span>;
}

/** "4 av 5", or "Inget betyg": a rating is a count of five, never a star or a percentage. */
export function FeedbackRating({ rating }: { readonly rating: number | null }) {
  const t = useTranslations("admin.feedback.rating");
  return <>{rating === null ? t("none") : t("value", { rating })}</>;
}

/**
 * The notice's state as a dot and its words; the words carry it, the colour repeats it. In the list the
 * words say what they are about, "Avisering: Köad"; in the open submission a label beside them does.
 */
export function FeedbackNoticeState({
  state,
  named = false,
}: {
  readonly state: AdminFeedbackNoticeState;
  readonly named?: boolean;
}) {
  const t = useTranslations("admin.feedback");
  const words = t(`detail.notice.states.${state}`);
  return <StatusDot tone={NOTICE_TONE[state]}>{named ? t("list.notice", { state: words }) : words}</StatusDot>;
}

/** A page's name as the app titles it; a key this web does not know yet is shown as it came. */
export function useFeedbackPageLabel(): (page: string) => string {
  const t = useTranslations("admin.feedback.pages");
  return (page) => (isFeedbackPageKey(page) ? t(page) : page);
}

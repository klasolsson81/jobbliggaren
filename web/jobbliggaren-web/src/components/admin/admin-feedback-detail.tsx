"use client";

// "use client": the open submission holds the status being chosen, the command that runs, its refusal
// and the requeue's confirmation, and moves focus to itself when it is opened from the list.
import { useEffect, useId, useLayoutEffect, useRef, useState, useTransition, type FormEvent } from "react";
import { useFormatter, useTranslations } from "next-intl";
import { ArrowLeft, Send } from "lucide-react";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import {
  FEEDBACK_STATUSES,
  isFeedbackStatus,
  type AdminFeedbackRefusal,
  type AdminFeedbackRefused,
} from "@/lib/admin/feedback";
import { showAdminToast } from "@/lib/admin/toast-store";
import type {
  AdminFeedbackClient,
  AdminFeedbackItem,
  AdminFeedbackStatus,
  AdminRegion,
} from "@/lib/admin/view-models";
import { formatDateTime } from "@/lib/i18n/format";
import { AdminBusyLabel } from "./admin-busy-label";
import { AdminConfirmDialog } from "./admin-confirm-dialog";
import { FeedbackFocusLink } from "./admin-feedback-focus";
import { FeedbackNoticeState, FeedbackRating, FeedbackStatusPill, useFeedbackPageLabel } from "./admin-feedback-parts";
import { AdminRegionLine } from "./admin-region-line";
import { AdminUnknown } from "./admin-unknown";
import { ComingSoon } from "./coming-soon";

/** The list the submissions are opened from (`admin-feedback-view.tsx`). */
const LIST_SELECTOR = ".jp-adminfeedback__list";

export interface AdminFeedbackDetailProps {
  readonly region: AdminRegion<AdminFeedbackItem>;
  /** The submission the URL opens: "Alla inskick" returns focus to its row in the list. */
  readonly openId: string;
  /** The list it was opened from: the same URL without the open submission. */
  readonly backHref: string;
  /** A refused read's own line; without one a failed read shows the shared line. */
  readonly failed?: string;
  readonly onStatus: (id: string, status: AdminFeedbackStatus) => Promise<AdminFeedbackRefusal>;
  readonly onRequeue: (id: string, acknowledgeDuplicateRisk: boolean) => Promise<AdminFeedbackRefusal>;
}

/**
 * The open submission (#1979, ADR 0150 D1: one framed region, not a card grid). The caller keys it by the
 * submission's id, so each submission starts with no choice, no refusal and no confirmation of its own.
 * Below 1100 px it is a step of its own, and "Alla inskick" leads back to the list it was opened from.
 */
export function AdminFeedbackDetail({ region, openId, backHref, failed, onStatus, onRequeue }: AdminFeedbackDetailProps) {
  const t = useTranslations("admin.feedback.detail");
  const sectionRef = useRef<HTMLElement>(null);

  // A submission opened from the list takes focus, so the keyboard and a screen reader land on it. A
  // layout effect: below 1100 px the list steps aside, and focus must move before the row holding it is
  // gone from the screen. One the page opened by its own URL leaves focus where the page put it.
  useLayoutEffect(() => {
    const active = document.activeElement;
    if (!(active instanceof Element) || active.closest(LIST_SELECTOR) === null) return;
    sectionRef.current?.focus({ preventScroll: true });
    sectionRef.current?.scrollIntoView?.({ block: "start" });
  }, []);

  return (
    <section
      ref={sectionRef}
      tabIndex={-1}
      aria-labelledby="admin-feedback-detail"
      className="jp-adminfeedback__detail"
    >
      <FeedbackFocusLink
        href={backHref}
        focusTo={`item:${openId.toLowerCase()}`}
        className="jp-adminfeedback__back jp-adminfeedback__textlink"
      >
        <ArrowLeft size={16} aria-hidden="true" />
        {t("back")}
      </FeedbackFocusLink>
      <h2 id="admin-feedback-detail" className="sr-only">
        {t("label")}
      </h2>
      {region.kind === "loaded" ? (
        <FeedbackItemBody item={region.data} onStatus={onStatus} onRequeue={onRequeue} />
      ) : (
        <AdminRegionLine kind={region.kind} empty={t("notFound")} failed={failed} />
      )}
    </section>
  );
}

/** A refusal, shown with the control that asked for it. */
interface Refusal extends AdminFeedbackRefused {
  readonly where: "status" | "notice";
}

/** The status field for a refusal of its value; the refusal's own line for every other. */
type FocusTarget = "field" | "refusal" | "notice";

function FeedbackItemBody({
  item,
  onStatus,
  onRequeue,
}: {
  readonly item: AdminFeedbackItem;
  readonly onStatus: AdminFeedbackDetailProps["onStatus"];
  readonly onRequeue: AdminFeedbackDetailProps["onRequeue"];
}) {
  const t = useTranslations("admin.feedback");
  const format = useFormatter();
  const pageLabel = useFeedbackPageLabel();
  const statusId = useId();
  const statusRefusalId = useId();
  const replyId = useId();
  const soonId = useId();

  // The status being chosen; a new status from the server (a save, another administrator) starts over.
  const [choice, setChoice] = useState<AdminFeedbackStatus | null>(null);
  const [savedStatus, setSavedStatus] = useState(item.status);
  if (savedStatus !== item.status) {
    setSavedStatus(item.status);
    setChoice(null);
  }
  const chosen = choice ?? item.status;

  const [pending, startTransition] = useTransition();
  const [running, setRunning] = useState<"status" | "notice" | null>(null);
  const [refusal, setRefusal] = useState<Refusal | null>(null);
  const [confirming, setConfirming] = useState(false);
  const selectRef = useRef<HTMLSelectElement>(null);
  const refusalRef = useRef<HTMLParagraphElement>(null);
  const noticeHeadingRef = useRef<HTMLHeadingElement>(null);
  const resendRef = useRef<HTMLButtonElement>(null);
  const pendingFocus = useRef<FocusTarget | null>(null);
  const requeuedFromDialog = useRef(false);

  // Focus follows what a command left on screen, once it is there and the field is enabled again.
  useEffect(() => {
    const target = pendingFocus.current;
    if (target === null) return;
    const element =
      target === "field" ? selectRef.current : target === "refusal" ? refusalRef.current : noticeHeadingRef.current;
    if (element === null || (element instanceof HTMLSelectElement && element.disabled)) return;
    pendingFocus.current = null;
    element.focus();
  });

  function refuse(where: Refusal["where"], refused: AdminFeedbackRefused) {
    setRefusal({ where, ...refused });
    pendingFocus.current = where === "status" && refused.about === "value" ? "field" : "refusal";
  }

  // The button stays focusable while a command runs (DESIGN.md §6): a disabled button holding focus drops
  // it to the page. A command that throws ends at the nearest error boundary.
  function saveStatus(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (pending) return;
    if (chosen === item.status) {
      refuse("status", { text: t("detail.status.unchanged"), about: "value" });
      return;
    }
    const status = chosen;
    setRefusal(null);
    setRunning("status");
    startTransition(async () => {
      const outcome = await onStatus(item.id, status);
      startTransition(() => {
        setRunning(null);
        if (outcome === null) showAdminToast(t("detail.status.saved", { status: t(`status.${status}`) }));
        else refuse("status", outcome);
      });
    });
  }

  // From Failed nothing was sent, so the notice is sent again at a press, without a question.
  function retryNotice() {
    if (pending) return;
    setRefusal(null);
    setRunning("notice");
    startTransition(async () => {
      const outcome = await onRequeue(item.id, false);
      startTransition(() => {
        setRunning(null);
        if (outcome === null) {
          showAdminToast(t("detail.notice.queued"));
          pendingFocus.current = "notice";
        } else {
          refuse("notice", outcome);
        }
      });
    });
  }

  const notice = item.notice;
  const statusRefusal = refusal?.where === "status" ? refusal : null;

  return (
    <>
      <div className="jp-adminfeedback__detailhead">
        <FeedbackStatusPill status={item.status} />
        <form className="jp-adminfeedback__status" onSubmit={saveStatus} noValidate>
          <div className="jp-adminfeedback__statusrow">
            <label htmlFor={statusId}>{t("detail.status.label")}</label>
            <select
              ref={selectRef}
              id={statusId}
              className="jp-adminfeedback__select"
              value={chosen}
              disabled={pending}
              aria-invalid={statusRefusal?.about === "value" ? true : undefined}
              aria-describedby={statusRefusal === null ? undefined : statusRefusalId}
              onChange={(event) => {
                const value = event.target.value;
                if (isFeedbackStatus(value)) setChoice(value);
              }}
            >
              {FEEDBACK_STATUSES.map((option) => (
                <option key={option} value={option}>
                  {t(`status.${option}`)}
                </option>
              ))}
            </select>
            <button type="submit" className="jp-btn jp-btn--secondary jp-btn--sm" aria-disabled={pending || undefined}>
              <AdminBusyLabel
                busy={running === "status"}
                label={t("detail.status.save")}
                busyLabel={t("detail.status.saving")}
              />
            </button>
          </div>
          {statusRefusal === null ? null : (
            <p
              ref={refusalRef}
              id={statusRefusalId}
              tabIndex={-1}
              role="alert"
              className="jp-adminfeedback__refusal"
            >
              {statusRefusal.text}
            </p>
          )}
        </form>
      </div>

      {item.comment === null ? (
        <p className="jp-adminfeedback__none">{t("detail.noComment")}</p>
      ) : (
        <p className="jp-adminfeedback__text">{item.comment}</p>
      )}

      <dl className="jp-admindl">
        <dt>{t("detail.facts.reporter")}</dt>
        <dd>{item.reporterEmail ?? <AdminUnknown />}</dd>
        <dt>{t("detail.facts.page")}</dt>
        <dd>{pageLabel(item.page)}</dd>
        <dt>{t("detail.facts.rating")}</dt>
        <dd>
          <FeedbackRating rating={item.rating} />
        </dd>
        <dt>{t("detail.facts.submitted")}</dt>
        <dd>{formatDateTime(format, item.submittedAt) ?? <AdminUnknown />}</dd>
        {item.statusChangedAt === null ? null : (
          <>
            <dt>{t("detail.facts.statusChanged")}</dt>
            <dd>{formatDateTime(format, item.statusChangedAt) ?? <AdminUnknown />}</dd>
          </>
        )}
      </dl>

      <div className="jp-adminfeedback__block">
        <h3 ref={noticeHeadingRef} tabIndex={-1} className="jp-adminfeedback__subhead">
          {t("detail.notice.heading")}
        </h3>
        <dl className="jp-admindl">
          <dt>{t("detail.notice.state")}</dt>
          <dd>{notice === null ? <AdminUnknown /> : <FeedbackNoticeState state={notice.state} />}</dd>
          {notice === null ? null : (
            <>
              <dt>{t("detail.notice.attempts")}</dt>
              <dd>{format.number(notice.attempts)}</dd>
            </>
          )}
          {notice?.state === "queued" ? (
            <>
              <dt>{t("detail.notice.nextAttempt")}</dt>
              <dd>{formatDateTime(format, notice.nextAttemptAt) ?? <AdminUnknown />}</dd>
            </>
          ) : null}
        </dl>
        {notice?.state === "failed" ? (
          <button
            type="button"
            className="jp-btn jp-btn--secondary jp-btn--sm"
            aria-disabled={pending || undefined}
            onClick={retryNotice}
          >
            <AdminBusyLabel
              busy={running === "notice"}
              label={t("detail.notice.retry")}
              busyLabel={t("detail.notice.queueing")}
            />
          </button>
        ) : null}
        {notice?.state === "unknown" ? (
          <button
            ref={resendRef}
            type="button"
            className="jp-btn jp-btn--secondary jp-btn--sm"
            aria-disabled={pending || undefined}
            aria-haspopup="dialog"
            onClick={() => {
              if (pending) return;
              setRefusal(null);
              setConfirming(true);
            }}
          >
            {t("detail.notice.resend")}
          </button>
        ) : null}
        {refusal?.where === "notice" ? (
          <p ref={refusalRef} tabIndex={-1} role="alert" className="jp-adminfeedback__refusal">
            {refusal.text}
          </p>
        ) : null}
      </div>

      <div className="jp-adminfeedback__block">
        <h3 className="jp-adminfeedback__subhead">{t("detail.client.heading")}</h3>
        <FeedbackClientFacts client={item.client} appVersion={item.appVersion} />
      </div>

      {/* Replies to the reporter are a later issue: the form keeps its place, disabled (ADR 0150 D2). */}
      <div className="jp-adminfeedback__reply">
        <Label htmlFor={replyId}>{t("detail.reply")}</Label>
        <Textarea id={replyId} disabled aria-describedby={soonId} />
        <div>
          <button type="button" className="jp-btn jp-btn--secondary" disabled aria-describedby={soonId}>
            <Send size={16} aria-hidden="true" />
            {t("detail.send")}
          </button>
        </div>
        <ComingSoon id={soonId} />
      </div>

      {/* A notice sent again destroys nothing, and the body says what it risks: the neutral tone. */}
      <AdminConfirmDialog
        key={confirming ? "open" : "closed"}
        open={confirming}
        tone="neutral"
        title={t("detail.notice.confirm.title")}
        body={t("detail.notice.confirm.body")}
        confirmLabel={t("detail.notice.confirm.confirm")}
        busyLabel={t("detail.notice.queueing")}
        onConfirm={async () => {
          const outcome = await onRequeue(item.id, true);
          if (outcome === null) {
            showAdminToast(t("detail.notice.queued"));
            requeuedFromDialog.current = true;
            setConfirming(false);
          }
          return outcome?.text ?? null;
        }}
        onCancel={() => setConfirming(false)}
        onCloseAutoFocus={(event) => {
          // A requeue lands on the notice, whose state has moved on; a cancel returns to its button.
          event.preventDefault();
          const requeued = requeuedFromDialog.current;
          requeuedFromDialog.current = false;
          (requeued ? noticeHeadingRef.current : (resendRef.current ?? noticeHeadingRef.current))?.focus();
        }}
      />
    </>
  );
}

/** What the browser reported, as reported. An unknown value is an en-dash, never 0 (ADR 0150 D2). */
function FeedbackClientFacts({
  client,
  appVersion,
}: {
  readonly client: AdminFeedbackClient;
  readonly appVersion: string | null;
}) {
  const t = useTranslations("admin.feedback.detail.client");
  const format = useFormatter();
  const size = (width: number | null, height: number | null) =>
    width === null || height === null ? <AdminUnknown /> : t("size", { width: String(width), height: String(height) });

  return (
    <dl className="jp-admindl jp-adminfeedback__meta">
      <dt>{t("viewport")}</dt>
      <dd>{size(client.viewportWidth, client.viewportHeight)}</dd>
      <dt>{t("screen")}</dt>
      <dd>{size(client.screenWidth, client.screenHeight)}</dd>
      <dt>{t("pixelRatio")}</dt>
      <dd>
        {client.pixelRatio === null ? (
          <AdminUnknown />
        ) : (
          format.number(client.pixelRatio, { maximumFractionDigits: 2 })
        )}
      </dd>
      <dt>{t("theme")}</dt>
      <dd>{client.theme === null ? <AdminUnknown /> : t(`themes.${client.theme}`)}</dd>
      <dt>{t("device")}</dt>
      <dd>{client.deviceClass === null ? <AdminUnknown /> : t(`devices.${client.deviceClass}`)}</dd>
      <dt>{t("os")}</dt>
      <dd>{client.os === null ? <AdminUnknown /> : t(`systems.${client.os}`)}</dd>
      <dt>{t("browser")}</dt>
      <dd>{client.browser === null ? <AdminUnknown /> : t(`browsers.${client.browser}`)}</dd>
      <dt>{t("version")}</dt>
      <dd>{appVersion === null ? <AdminUnknown /> : <code>{appVersion}</code>}</dd>
    </dl>
  );
}

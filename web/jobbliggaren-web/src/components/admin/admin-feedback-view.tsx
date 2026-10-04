"use client";

// "use client": the view holds the filter, the open report and the reply being written.
import { useEffect, useId, useRef, useState, useTransition } from "react";
import { flushSync } from "react-dom";
import { useFormatter, useTranslations } from "next-intl";
import { Send } from "lucide-react";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { formatDateTime } from "@/lib/i18n/format";
import { showAdminToast } from "@/lib/admin/toast-store";
import type {
  AdminFeedbackItem,
  AdminFeedbackStatus,
  AdminRegion,
} from "@/lib/admin/view-models";
import { AdminBusyLabel } from "./admin-busy-label";
import { AdminRegionLine } from "./admin-region-line";
import { AdminSegment } from "./admin-segment";
import { ComingSoon } from "./coming-soon";

type Filter = "all" | AdminFeedbackStatus;

const FILTERS: ReadonlyArray<Filter> = ["all", "new", "inProgress", "resolved", "skipped"];
const STATUSES: ReadonlyArray<AdminFeedbackStatus> = ["new", "inProgress", "resolved", "skipped"];
const STATUS_TONE: Readonly<Record<AdminFeedbackStatus, string>> = {
  new: "jp-pill--info",
  inProgress: "jp-pill--warning",
  resolved: "jp-pill--success",
  skipped: "jp-pill--neutral",
};
const LIST_SOON_ID = "admin-feedback-list-soon";
const DETAIL_SOON_ID = "admin-feedback-detail-soon";
const EXCERPT_LENGTH = 90;

function excerpt(text: string): string {
  return text.length <= EXCERPT_LENGTH ? text : `${text.slice(0, EXCERPT_LENGTH).trimEnd()}…`;
}

type FeedbackRegion = AdminRegion<ReadonlyArray<AdminFeedbackItem>>;

/** An unavailable region takes no handlers; every other state needs both. */
export type AdminFeedbackViewProps =
  | { readonly region: Extract<FeedbackRegion, { readonly kind: "unavailable" }> }
  | {
      readonly region: Exclude<FeedbackRegion, { readonly kind: "unavailable" }>;
      readonly onReply: (id: string, text: string) => Promise<void>;
      readonly onStatus: (id: string, status: AdminFeedbackStatus) => void;
    };

/**
 * Reports from the app's feedback button as a master/detail layout (ADR 0150). Until #1979 the
 * region is unavailable: the filter, the list and the reply form keep their structure, disabled
 * and described by their "Kommer snart" lines, and no count, report or reply is shown (D2).
 */
export function AdminFeedbackView(props: AdminFeedbackViewProps) {
  const { region } = props;
  const t = useTranslations("admin.feedback");
  const [filter, setFilter] = useState<Filter>("all");
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [selections, setSelections] = useState(0);
  const loaded = region.kind === "loaded";
  const items = loaded ? region.data : [];
  const shown = filter === "all" ? items : items.filter((item) => item.status === filter);
  // A report stays open when a change moves it out of the filter.
  const selected = items.find((item) => item.id === selectedId) ?? shown[0] ?? null;

  const filters = (
    <AdminSegment
      label={t("filter.label")}
      options={FILTERS.map((value) => ({
        value,
        label: loaded
          ? t("filterCount", {
              label: t(`filter.${value}`),
              count: value === "all" ? items.length : items.filter((item) => item.status === value).length,
            })
          : t(`filter.${value}`),
      }))}
      value={filter}
      onChange={loaded ? setFilter : undefined}
      describedBy={region.kind === "unavailable" ? LIST_SOON_ID : undefined}
    />
  );

  if (!("onReply" in props)) {
    return (
      <>
        {filters}
        <div className="jp-adminfeedback">
          <section aria-labelledby="admin-feedback-list">
            <h2 id="admin-feedback-list" className="sr-only">
              {t("list.label")}
            </h2>
            <ComingSoon id={LIST_SOON_ID} region />
          </section>
          <section aria-labelledby="admin-feedback-detail" className="jp-adminfeedback__detail">
            <h2 id="admin-feedback-detail" className="sr-only">
              {t("detail.label")}
            </h2>
            <div className="jp-adminfeedback__reply">
              <Label htmlFor="admin-feedback-reply">{t("detail.reply")}</Label>
              <Textarea id="admin-feedback-reply" disabled aria-describedby={DETAIL_SOON_ID} />
              <div>
                <button
                  type="button"
                  className="jp-btn jp-btn--secondary"
                  disabled
                  aria-describedby={DETAIL_SOON_ID}
                >
                  <Send size={16} aria-hidden="true" />
                  {t("detail.send")}
                </button>
              </div>
            </div>
            <ComingSoon id={DETAIL_SOON_ID} />
          </section>
        </div>
      </>
    );
  }

  return (
    <>
      {filters}
      <div className={selected === null ? undefined : "jp-adminfeedback"}>
        <section aria-labelledby="admin-feedback-list">
          <h2 id="admin-feedback-list" className="sr-only">
            {t("list.label")}
          </h2>
          {region.kind !== "loaded" ? (
            <AdminRegionLine kind={region.kind} empty={t("empty")} region />
          ) : shown.length === 0 ? (
            <AdminRegionLine kind="empty" empty={t("empty")} region />
          ) : (
            <ol className="jp-adminfeedback__list">
              {shown.map((item) => (
                <li key={item.id}>
                  <FeedbackListItem
                    item={item}
                    selected={item.id === selected?.id}
                    onSelect={() => {
                      setSelectedId(item.id);
                      setSelections((count) => count + 1);
                    }}
                  />
                </li>
              ))}
            </ol>
          )}
        </section>
        {selected === null ? null : (
          <FeedbackDetail
            key={selected.id}
            item={selected}
            selections={selections}
            onKeepOpen={() => setSelectedId(selected.id)}
            onReply={props.onReply}
            onStatus={props.onStatus}
          />
        )}
      </div>
    </>
  );
}

function FeedbackListItem({
  item,
  selected,
  onSelect,
}: {
  readonly item: AdminFeedbackItem;
  readonly selected: boolean;
  readonly onSelect: () => void;
}) {
  const t = useTranslations("admin.feedback");
  const format = useFormatter();
  const dash = useTranslations("admin.unavailable")("unknownValue");
  return (
    <button
      type="button"
      className="jp-adminfeedback__item"
      aria-current={selected ? "true" : undefined}
      onClick={onSelect}
    >
      <span className="jp-adminfeedback__itemhead">
        <span className={`jp-pill ${STATUS_TONE[item.status]}`}>{t(`status.${item.status}`)}</span>
        <span className="jp-adminfeedback__category">{t(`category.${item.category}`)}</span>
        <span className="jp-adminfeedback__time">{formatDateTime(format, item.receivedAt) ?? dash}</span>
      </span>
      <span className="jp-adminfeedback__excerpt">{excerpt(item.text)}</span>
      <span className="jp-adminfeedback__sender">{item.senderEmail}</span>
    </button>
  );
}

function FeedbackDetail({
  item,
  selections,
  onKeepOpen,
  onReply,
  onStatus,
}: {
  readonly item: AdminFeedbackItem;
  /** How many times a report has been chosen in the list; each choice brings the open report into view. */
  readonly selections: number;
  /** Keeps this report open when an action on it moves it out of the filter. */
  readonly onKeepOpen: () => void;
  readonly onReply: (id: string, text: string) => Promise<void>;
  readonly onStatus: (id: string, status: AdminFeedbackStatus) => void;
}) {
  const t = useTranslations("admin.feedback");
  const format = useFormatter();
  const dash = useTranslations("admin.unavailable")("unknownValue");
  const [reply, setReply] = useState("");
  const [status, setStatus] = useState<AdminFeedbackStatus | null>(null);
  const [sent, setSent] = useState(0);
  const [pending, startTransition] = useTransition();
  const sectionRef = useRef<HTMLElement>(null);
  const repliesRef = useRef<HTMLOListElement>(null);
  const replyRef = useRef<HTMLTextAreaElement>(null);
  const statusId = useId();
  const hintId = useId();
  const chosenStatus = status ?? item.status;

  useEffect(() => {
    if (selections === 0) return;
    sectionRef.current?.focus({ preventScroll: true });
    sectionRef.current?.scrollIntoView?.({ block: "start" });
  }, [selections]);

  useEffect(() => {
    if (sent === 0) return;
    const last = repliesRef.current?.lastElementChild;
    if (last instanceof HTMLElement) last.focus();
  }, [sent]);

  // A send that throws ends at the nearest error boundary rather than leaving the form disabled.
  function send() {
    const text = reply.trim();
    if (text === "") {
      flushSync(() => setReply(""));
      replyRef.current?.reportValidity();
      return;
    }
    // Outside the transition: an update inside it would commit only when the send completes, after the
    // report had already left the filter and been replaced.
    onKeepOpen();
    startTransition(async () => {
      await onReply(item.id, text);
      startTransition(() => {
        setReply("");
        setSent((count) => count + 1);
      });
      showAdminToast(t("detail.sent", { email: item.senderEmail }));
    });
  }

  function saveStatus() {
    onKeepOpen();
    if (chosenStatus !== item.status) onStatus(item.id, chosenStatus);
    setStatus(null);
  }

  return (
    <section
      ref={sectionRef}
      tabIndex={-1}
      aria-labelledby="admin-feedback-detail"
      className="jp-adminfeedback__detail"
    >
      <h2 id="admin-feedback-detail" className="sr-only">
        {t("detail.label")}
      </h2>
      <div className="jp-adminfeedback__detailhead">
        <span className={`jp-pill ${STATUS_TONE[item.status]}`}>{t(`status.${item.status}`)}</span>
        <span className="jp-pill jp-pill--neutral">{t(`category.${item.category}`)}</span>
        <span className="jp-adminfeedback__time">{formatDateTime(format, item.receivedAt) ?? dash}</span>
        <span className="jp-adminfeedback__status">
          <label htmlFor={statusId}>{t("detail.statusLabel")}</label>
          <select
            id={statusId}
            className="jp-adminfeedback__select"
            value={chosenStatus}
            onChange={(event) => setStatus(event.target.value as AdminFeedbackStatus)}
          >
            {STATUSES.map((option) => (
              <option key={option} value={option}>
                {t(`status.${option}`)}
              </option>
            ))}
          </select>
          <button type="button" className="jp-btn jp-btn--secondary jp-btn--sm" onClick={saveStatus}>
            {t("detail.saveStatus")}
          </button>
        </span>
      </div>
      <p className="jp-adminfeedback__from">{item.senderEmail}</p>
      <p className="jp-adminfeedback__text">{item.text}</p>
      <dl className="jp-admindl jp-adminfeedback__meta">
        <dt>{t("detail.meta.page")}</dt>
        <dd>
          <code>{item.page}</code>
        </dd>
        <dt>{t("detail.meta.screen")}</dt>
        <dd>{item.screen}</dd>
        <dt>{t("detail.meta.device")}</dt>
        <dd>{item.device}</dd>
        <dt>{t("detail.meta.version")}</dt>
        <dd>
          <code>{item.version}</code>
        </dd>
      </dl>
      <h3 className="jp-adminfeedback__subhead">{t("detail.replies")}</h3>
      {item.replies.length === 0 ? (
        <p className="jp-adminsoon">{t("detail.noReplies")}</p>
      ) : (
        <ol ref={repliesRef} className="jp-adminfeedback__replies">
          {item.replies.map((sentReply) => (
            <li key={sentReply.id} tabIndex={-1}>
              <span className="jp-adminfeedback__time">{formatDateTime(format, sentReply.sentAt) ?? dash}</span>
              <p>{sentReply.text}</p>
            </li>
          ))}
        </ol>
      )}
      <form
        className="jp-adminfeedback__reply"
        onSubmit={(event) => {
          event.preventDefault();
          send();
        }}
      >
        <Label htmlFor="admin-feedback-reply">{t("detail.reply")}</Label>
        <Textarea
          ref={replyRef}
          id="admin-feedback-reply"
          value={reply}
          onChange={(event) => setReply(event.target.value)}
          disabled={pending}
          required
          aria-describedby={hintId}
        />
        <p id={hintId} className="jp-adminfeedback__hint">
          {t("detail.replyHint", { email: item.senderEmail })}
        </p>
        <div>
          <button type="submit" className="jp-btn jp-btn--primary" disabled={pending}>
            <Send size={16} aria-hidden="true" />
            <AdminBusyLabel busy={pending} label={t("detail.send")} busyLabel={t("detail.sending")} />
          </button>
        </div>
      </form>
    </section>
  );
}

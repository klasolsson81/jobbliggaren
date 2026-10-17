"use client";

import { useEffect, useId, useRef, useState, useTransition, type MouseEvent } from "react";
import Link from "next/link";
import { Bell, X } from "lucide-react";
import { useFormatter, useTranslations } from "next-intl";
import { MatchChip } from "@/components/job-ads/match-chip";
import { useNotificationFeedContext } from "@/components/shell/notification-feed-provider";
import { markAllNotificationsReadAction } from "@/lib/actions/notification-feed";
import type { NotificationFeed, NotificationFeedItem, NotificationFeedSourceKind } from "@/lib/dto/notification-feed";
import { useDismissable } from "@/lib/hooks/use-dismissable";
import { readKey, unreadTotal } from "@/lib/hooks/use-notification-feed";
import { formatDate, formatNumber, formatTime, type JpFormatter } from "@/lib/i18n/format";
import { onPlainNav } from "@/lib/nav/modified-click";
import { previousSwedishDateSlug, swedishDateSlug, swedishDaysBetween } from "@/lib/time/swedish-calendar";

type NotificationsT = ReturnType<typeof useTranslations<"common.notifications">>;

/** The badge shows the exact number up to this; above it, "99+" (ADR 0120 clause 2). */
const BADGE_MAX = 99;

/**
 * The header bell (#1666): the signed-in user's new matches and new ads from followed companies, read
 * by the shell's `NotificationFeedProvider` and shown in a non-modal popover. Nothing is acknowledged by opening
 * or reading.
 * The panel holds notifications and nothing else (#2130): its only links are the rows' own, to the ad.
 *
 * #2151 - every row carries an X, a button of its own beside the row link, that takes just that notification out of the
 * bell: the row goes at once, the count follows when the server has agreed (an unread row falls out of the badge), and
 * a refusal brings the row back with a notice, because nothing was deleted. It writes the bell's own flags and nothing
 * else, so Översikt, `/matchningar` and `/foretag/bevakade/nya` keep their rows and numbers.
 *
 * #2130 - an unread row carries a dot, and a click on it (plain, modified or middle) marks THAT row read:
 * the dot goes at once, the count follows when the server has agreed, and nothing waits on it, because the
 * same click opens the ad. "Markera alla som lästa" marks each source's counted set read. Both write the
 * bell's own flags and no watermark, so Översikt and the lists keep their numbers.
 *
 * The badge is a measured number or nothing: no badge before the first answer or at zero, the exact
 * count up to 99, "99+" above, and an empty pill when a source refused to count (the user is told
 * unread items exist without a number). A failed read keeps the last answer and says when it was
 * from — it never becomes "Inga olästa".
 */
export function NotificationsBell() {
  const t = useTranslations("common.notifications");
  const format = useFormatter();
  const [open, setOpen] = useState(false);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const headingRef = useRef<HTMLDivElement>(null);
  const [marking, startMarking] = useTransition();
  const [markFailed, setMarkFailed] = useState(false);
  const [dismissFailed, setDismissFailed] = useState(false);
  // What the removal's polite region says after an X, with the title and the reason so that two removals in a row are two
  // announcements.
  const [dismissedNote, setDismissedNote] = useState("");
  // Set when a refused dismissal restores a row whose X should have focus again; an object, so asking twice runs the effect twice.
  const [restoreFocus, setRestoreFocus] = useState<{ readonly key: string } | null>(null);
  // Every way the panel closes (the button, Escape, a click outside, a link) forgets the last removal, so its message
  // never outlives the panel it belongs to.
  const close = () => {
    setOpen(false);
    setDismissFailed(false);
    setDismissedNote("");
  };
  const ref = useDismissable(open, close, triggerRef);
  const titleId = useId();
  const { state, refresh, increasedTo, pendingRead, markRead, pendingDismiss, dismiss } = useNotificationFeedContext();

  useEffect(() => {
    if (restoreFocus === null) return;
    ref.current?.querySelector<HTMLElement>(`[data-notif-dismiss="${restoreFocus.key}"]`)?.focus();
  }, [restoreFocus, ref]);

  const feed = state.status === "ready" ? state.feed : null;
  const total = feed ? unreadTotal(feed) : undefined;
  const canMarkRead = typeof total === "number" && total > 0;
  // The never-anything state carries its status alone, so the header says nothing then.
  const countLine = typeof total === "number" && feed !== null && (feed.items.length > 0 || total > 0) ? total : null;

  function toggle() {
    if (!open) void refresh();
    setMarkFailed(false);
    setDismissFailed(false);
    setDismissedNote("");
    setOpen((value) => !value);
  }

  // The X. The row leaves the DOM at once, so focus is moved BEFORE it goes: to the next row's X, else the previous
  // row's, else the panel heading, and never to the document body. A refusal puts the row back, says so, and gives focus
  // back to the restored row's X if it is still where the removal left it.
  function dismissRow(item: NotificationFeedItem, button: HTMLElement) {
    const row = button.closest("li");
    const neighbour = row?.nextElementSibling ?? row?.previousElementSibling ?? null;
    const target = neighbour?.querySelector<HTMLElement>("[data-notif-dismiss]") ?? headingRef.current;
    target?.focus();
    setDismissFailed(false);
    setDismissedNote(t("dismissedAnnounce", { title: item.title, reason: reasonOf(t, item) }));
    void dismiss(item.kind, item.jobAdId).then((ok) => {
      if (ok) return;
      setDismissedNote("");
      setDismissFailed(true);
      // Only if focus is still where the removal left it; the effect below runs once the restored row is in the DOM.
      const active = document.activeElement;
      if (active === target || active === document.body) setRestoreFocus({ key: readKey(item.kind, item.jobAdId) });
    });
  }

  function markAllRead() {
    if (marking || !feed) return;
    const tokens = {
      match: tokenFor(feed, "Match"),
      followedCompanyAd: tokenFor(feed, "FollowedCompanyAd"),
    };
    setMarkFailed(false);
    startMarking(async () => {
      const result = await markAllNotificationsReadAction(tokens);
      // "Markerar…" stays until the new state has been read, so the old number never flashes back.
      await refresh(true);
      if (result.success) {
        headingRef.current?.focus();
      } else {
        setMarkFailed(true);
      }
    });
  }

  const onNavigate = (event: MouseEvent<Element>) => onPlainNav(event, close);

  return (
    <div className="jp-notif-anchor">
      <button
        ref={triggerRef}
        type="button"
        className="jp-icon-btn"
        aria-label={buttonLabel(t, total)}
        aria-expanded={open}
        aria-haspopup="dialog"
        onClick={toggle}
      >
        <Bell size={18} aria-hidden="true" />
        {total === null && <span className="jp-notif-badge jp-notif-badge--unknown" aria-hidden="true" />}
        {typeof total === "number" && total > 0 && (
          <span className="jp-notif-badge" aria-hidden="true">
            {total > BADGE_MAX ? `${BADGE_MAX}+` : formatNumber(format, total)}
          </span>
        )}
      </button>
      {/* Always mounted: a screen reader only announces a change to a region that already exists. */}
      <span className="sr-only" role="status" aria-live="polite" aria-atomic="true">
        {increasedTo !== null ? t("announce", { count: increasedTo }) : ""}
      </span>
      {/* The removal has a region of its own, so it can never mask a new-notification announcement or bring an old one back. */}
      <span className="sr-only" role="status" aria-live="polite" aria-atomic="true">
        {dismissedNote}
      </span>
      {open && (
        <div ref={ref} role="dialog" aria-labelledby={titleId} className="jp-notif">
          <div className="jp-notif__head">
            <div className="jp-notif__titles">
              <div id={titleId} ref={headingRef} tabIndex={-1} className="jp-notif__title">
                {t("heading")}
              </div>
              {countLine !== null && <p className="jp-notif__count">{t("unreadCount", { count: countLine })}</p>}
            </div>
            {canMarkRead && (
              <button
                type="button"
                className="jp-btn jp-btn--ghost jp-btn--sm"
                aria-disabled={marking}
                onClick={markAllRead}
              >
                {marking ? t("marking") : t("markAllRead")}
              </button>
            )}
          </div>
          {markFailed && (
            <p className="jp-notif__notice" role="alert">
              {t("markReadFailed")}
            </p>
          )}
          {dismissFailed && (
            <p className="jp-notif__notice" role="alert">
              {t("dismissFailed")}
            </p>
          )}
          {state.status === "ready" && state.stale && (
            <p className="jp-notif__notice">{t("stale", { time: formatTime(format, new Date(state.fetchedAt)) })}</p>
          )}
          <PanelBody
            state={state}
            t={t}
            format={format}
            pendingRead={pendingRead}
            pendingDismiss={pendingDismiss}
            onRetry={() => void refresh(true)}
            onNavigate={onNavigate}
            onRead={(item) => markRead(item.kind, item.jobAdId)}
            onDismiss={dismissRow}
          />
        </div>
      )}
    </div>
  );
}

function PanelBody({
  state,
  t,
  format,
  pendingRead,
  pendingDismiss,
  onRetry,
  onNavigate,
  onRead,
  onDismiss,
}: {
  state: ReturnType<typeof useNotificationFeedContext>["state"];
  t: NotificationsT;
  format: JpFormatter;
  pendingRead: ReadonlySet<string>;
  pendingDismiss: ReadonlySet<string>;
  onRetry: () => void;
  onNavigate: (event: MouseEvent<Element>) => void;
  onRead: (item: NotificationFeedItem) => void;
  onDismiss: (item: NotificationFeedItem, button: HTMLElement) => void;
}) {
  if (state.status === "loading") {
    return (
      <div className="jp-notif__body">
        <ul className="jp-notif__list" aria-busy="true">
          {[0, 1, 2].map((key) => (
            <li key={key} className="jp-notif__row jp-notif__row--skeleton" aria-hidden="true">
              <span className="jp-skeleton jp-notif__skel-signal" />
              <span className="jp-skeleton jp-notif__skel-title" />
              <span className="jp-skeleton jp-notif__skel-company" />
            </li>
          ))}
        </ul>
        <span className="sr-only">{t("loading")}</span>
      </div>
    );
  }

  if (state.status !== "ready") {
    return (
      <div className="jp-notif__body jp-notif__message">
        <p>{t("loadFailed")}</p>
        {state.status === "failed" && (
          <button type="button" className="jp-btn jp-btn--ghost jp-btn--sm" onClick={onRetry}>
            {t("retry")}
          </button>
        )}
      </div>
    );
  }

  const total = unreadTotal(state.feed);
  // A row just taken out with the X is gone from the list at once, but its unread dot still counts toward the line below
  // until the server has agreed, exactly as a just-clicked row keeps its count: both fall together on confirmation.
  const allItems = state.feed.items;
  const items = allItems.filter((item) => !pendingDismiss.has(readKey(item.kind, item.jobAdId)));
  // Unread notifications the list does not carry. The number is exact or absent (ADR 0120), so a refused
  // count (null) has none. The line is a statement, never a link: the panel holds notifications and nothing else.
  // It counts the rows the SERVER says are unread, not the dots still on screen: a row just clicked has lost its
  // dot but not yet the count, and both fall together when the mark is confirmed, so the line stays exact.
  const unreadShown = allItems.filter((item) => item.isUnread).length;
  const notInList = typeof total === "number" ? total - unreadShown : null;
  const note =
    notInList !== null && notInList > 0
      ? t("notInList", { count: notInList })
      : total === null && unreadShown === 0
        ? t("notInListUnknown")
        : null;

  if (items.length === 0) {
    // A refused count means unread notifications exist, so "Du har inga notiser." would contradict the empty pill.
    return (
      <div className="jp-notif__body jp-notif__message">
        <p>{note ?? t("empty")}</p>
      </div>
    );
  }

  const now = new Date();
  return (
    <div className="jp-notif__body">
      <ul className="jp-notif__list" aria-label={t("listLabel")}>
        {items.map((item) => (
          <NotificationRow
            key={`${item.kind}:${item.jobAdId}`}
            item={item}
            when={formatWhen(t, format, item.occurredAt, now)}
            pending={pendingRead.has(readKey(item.kind, item.jobAdId))}
            onNavigate={onNavigate}
            onRead={onRead}
            onDismiss={onDismiss}
          />
        ))}
      </ul>
      {note !== null && <p className="jp-notif__notinlist">{note}</p>}
    </div>
  );
}

function NotificationRow({
  item,
  when,
  pending,
  onNavigate,
  onRead,
  onDismiss,
}: {
  item: NotificationFeedItem;
  when: string;
  /** The user clicked this row and the mark is on its way: it already reads as read. */
  pending: boolean;
  onNavigate: (event: MouseEvent<Element>) => void;
  onRead: (item: NotificationFeedItem) => void;
  onDismiss: (item: NotificationFeedItem, button: HTMLElement) => void;
}) {
  const t = useTranslations("common.notifications");
  const tTags = useTranslations("jobads.ui.tags");
  const baseId = useId();
  const signalId = `${baseId}-signal`;
  const unreadId = `${baseId}-unread`;
  const timeId = `${baseId}-time`;
  const companyId = `${baseId}-company`;
  const unread = item.isUnread && !pending;
  const describedBy = [signalId, unread ? unreadId : null, timeId, companyId].filter(Boolean).join(" ");
  // A row already read, or already marked, has nothing to mark. Every way of opening the row counts: a plain
  // click, a modified click (new tab) and the middle button, which fires auxclick and never click.
  const markRead = () => {
    if (item.isUnread && !pending) onRead(item);
  };

  return (
    <li className={unread ? "jp-notif__row jp-notif__row--unread" : "jp-notif__row"}>
      <div className="jp-notif__signal">
        {item.kind === "Match" && item.grade !== null ? (
          <MatchChip grade={item.grade} id={signalId} />
        ) : (
          <span id={signalId} className="jp-tag jp-tag--neutral" data-tag="followed">
            {tTags("followed")}
          </span>
        )}
        <time id={timeId} className="jp-notif__time" dateTime={item.occurredAt}>
          {when}
        </time>
      </div>
      {unread && (
        <>
          <span className="jp-notif__dot" aria-hidden="true" />
          <span id={unreadId} className="sr-only">
            {t("unread")}
          </span>
        </>
      )}
      <Link
        href={`/jobb/${item.jobAdId}`}
        className="jp-job__rowlink jp-notif__title-link"
        aria-describedby={describedBy}
        onClick={(event) => {
          markRead();
          onNavigate(event);
        }}
        onAuxClick={(event) => {
          if (event.button === 1) markRead();
        }}
      >
        {item.title}
      </Link>
      <p id={companyId} className="jp-notif__company">
        {item.companyName}
      </p>
      {/* A sibling of the row link and never inside it, above its overlay, so a click here opens nothing. */}
      <button
        type="button"
        className="jp-icon-btn jp-notif__dismiss"
        data-notif-dismiss={readKey(item.kind, item.jobAdId)}
        aria-label={t("dismiss", { reason: reasonOf(t, item), title: item.title, company: item.companyName })}
        onClick={(event) => onDismiss(item, event.currentTarget)}
      >
        <X size={16} aria-hidden="true" />
      </button>
    </li>
  );
}

/** Why the notification is there, in words. */
function reasonOf(t: NotificationsT, item: NotificationFeedItem): string {
  return item.kind === "Match" ? t("reasonMatch") : t("reasonFollowed");
}

function tokenFor(feed: NotificationFeed, kind: NotificationFeedSourceKind): string | null {
  return feed.sources.find((source) => source.kind === kind)?.acknowledgedThrough ?? null;
}

function buttonLabel(t: NotificationsT, total: number | null | undefined): string {
  if (total === null) return t("buttonAriaLabelUnknown");
  if (total === undefined || total === 0) return t("buttonAriaLabel");
  if (total > BADGE_MAX) return t("buttonAriaLabelMany");
  return t("buttonAriaLabelUnread", { count: total });
}

/**
 * When a notification arrived, in the Swedish calendar: "idag, kl. 09:14" · "igår, kl. 14:32" ·
 * the weekday within the week · "3 apr" this year · "3 apr 2025" before it.
 */
function formatWhen(t: NotificationsT, format: JpFormatter, iso: string, now: Date): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;

  const day = swedishDateSlug(date);
  if (day === swedishDateSlug(now)) return t("timeToday", { time: formatTime(format, date) });
  if (day === previousSwedishDateSlug(now)) return t("timeYesterday", { time: formatTime(format, date) });
  if (swedishDaysBetween(date, now) < 7) return format.dateTime(date, { weekday: "long" });
  if (day.slice(0, 4) === swedishDateSlug(now).slice(0, 4)) {
    return format.dateTime(date, { day: "numeric", month: "short" });
  }
  return formatDate(format, iso) ?? iso;
}

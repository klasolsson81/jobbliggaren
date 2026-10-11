"use client";

import { useId, useRef, useState, useTransition, type MouseEvent } from "react";
import Link from "next/link";
import { Bell } from "lucide-react";
import { useFormatter, useTranslations } from "next-intl";
import { MatchChip } from "@/components/job-ads/match-chip";
import { markNotificationsReadAction } from "@/lib/actions/notification-feed";
import type { NotificationFeed, NotificationFeedItem, NotificationFeedSourceKind } from "@/lib/dto/notification-feed";
import { useDismissable } from "@/lib/hooks/use-dismissable";
import { unreadTotal, useNotificationFeed } from "@/lib/hooks/use-notification-feed";
import { formatDate, formatNumber, formatTime, type JpFormatter } from "@/lib/i18n/format";
import { onPlainNav } from "@/lib/nav/modified-click";
import { previousSwedishDateSlug, swedishDateSlug, swedishDaysBetween } from "@/lib/time/swedish-calendar";

type NotificationsT = ReturnType<typeof useTranslations<"common.notifications">>;

/** The badge shows the exact number up to this; above it, "99+" (ADR 0120 clause 2). */
const BADGE_MAX = 99;

/**
 * The header bell (#1666): the signed-in user's new matches and new ads from followed companies, read
 * by `useNotificationFeed` and shown in a non-modal popover. A notification is a (source, ad) pair,
 * so an ad can appear once per reason. Nothing is acknowledged by opening or reading; "Markera alla
 * som lästa" is the one write, and it acknowledges through each source's existing `/seen` endpoint.
 * The panel holds notifications and nothing else (#2130): its only links are the rows' own, to the ad.
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
  const close = () => setOpen(false);
  const ref = useDismissable(open, close, triggerRef);
  const titleId = useId();
  const { state, refresh, increasedTo } = useNotificationFeed();
  const [marking, startMarking] = useTransition();
  const [markFailed, setMarkFailed] = useState(false);

  const feed = state.status === "ready" ? state.feed : null;
  const total = feed ? unreadTotal(feed) : undefined;
  const canMarkRead = typeof total === "number" && total > 0;
  // The never-anything state carries its status alone, so the header says nothing then.
  const countLine = typeof total === "number" && feed !== null && (feed.items.length > 0 || total > 0) ? total : null;

  function toggle() {
    if (!open) void refresh();
    setMarkFailed(false);
    setOpen((value) => !value);
  }

  function markAllRead() {
    if (marking || !feed) return;
    const tokens = {
      match: tokenFor(feed, "Match"),
      followedCompanyAd: tokenFor(feed, "FollowedCompanyAd"),
    };
    setMarkFailed(false);
    startMarking(async () => {
      const result = await markNotificationsReadAction(tokens);
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
          {state.status === "ready" && state.stale && (
            <p className="jp-notif__notice">{t("stale", { time: formatTime(format, new Date(state.fetchedAt)) })}</p>
          )}
          <PanelBody state={state} t={t} format={format} onRetry={() => void refresh(true)} onNavigate={onNavigate} />
        </div>
      )}
    </div>
  );
}

function PanelBody({
  state,
  t,
  format,
  onRetry,
  onNavigate,
}: {
  state: ReturnType<typeof useNotificationFeed>["state"];
  t: NotificationsT;
  format: JpFormatter;
  onRetry: () => void;
  onNavigate: (event: MouseEvent<Element>) => void;
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

  const { items } = state.feed;
  const total = unreadTotal(state.feed);
  // Unread notifications the list does not carry. The number is exact or absent (ADR 0120), so a refused
  // count (null) has none. The line is a statement, never a link: the panel holds notifications and nothing else.
  const unreadShown = items.filter((item) => item.isUnread).length;
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
            onNavigate={onNavigate}
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
  onNavigate,
}: {
  item: NotificationFeedItem;
  when: string;
  onNavigate: (event: MouseEvent<Element>) => void;
}) {
  const tTags = useTranslations("jobads.ui.tags");
  const baseId = useId();
  const signalId = `${baseId}-signal`;
  const newId = `${baseId}-new`;
  const timeId = `${baseId}-time`;
  const companyId = `${baseId}-company`;
  const describedBy = [signalId, item.isUnread ? newId : null, timeId, companyId].filter(Boolean).join(" ");

  return (
    <li className={item.isUnread ? "jp-notif__row jp-notif__row--unread" : "jp-notif__row"}>
      <div className="jp-notif__signal">
        {item.kind === "Match" && item.grade !== null ? (
          <MatchChip grade={item.grade} id={signalId} />
        ) : (
          <span id={signalId} className="jp-tag jp-tag--neutral" data-tag="followed">
            {tTags("followed")}
          </span>
        )}
        {item.isUnread && (
          <span id={newId} className="jp-tag jp-tag--accent" data-tag="new">
            {tTags("new")}
          </span>
        )}
        <time id={timeId} className="jp-notif__time" dateTime={item.occurredAt}>
          {when}
        </time>
      </div>
      <Link
        href={`/jobb/${item.jobAdId}`}
        className="jp-job__rowlink jp-notif__title-link"
        aria-describedby={describedBy}
        onClick={onNavigate}
      >
        {item.title}
      </Link>
      <p id={companyId} className="jp-notif__company">
        {item.companyName}
      </p>
    </li>
  );
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

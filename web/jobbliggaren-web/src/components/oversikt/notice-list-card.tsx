"use client";

import { useRef, type ReactNode } from "react";
import { useTranslations } from "next-intl";
import type { SectionNoticeData } from "./notice-section";
import { useNoticeList } from "./use-notice-list";

interface NoticeListCardProps {
  /** `aria-labelledby` target — the card's heading id. */
  readonly id: string;
  readonly title: string;
  readonly notices: ReadonlyArray<SectionNoticeData>;
  /** The single row rendered when nothing is unread — the card never disappears. */
  readonly emptyText: string;
  /** Column span in the 12-column grid. */
  readonly span: 8 | 12;
  /** Extra class on the section: `jp-ov-card--requires` for the warning-barred card. */
  readonly modifier?: string;
  /** The list element: `ol` for the action queue, `ul` for the event feed. */
  readonly listAs: "ol" | "ul";
  readonly listClassName: string;
  readonly renderRow: (
    notice: SectionNoticeData,
    read: boolean,
    onDismiss: (id: string) => void,
    onRestore: (id: string) => void,
  ) => ReactNode;
}

/**
 * The shell the two notice-list cards on `/oversikt` share (ADR 0140): head with the unread
 * count, the unread rows, the empty row, the read rows behind the Visa/Dölj foot. State and
 * focus moves come from `useNoticeList`; the fallback focus target after the last restore is the
 * card itself (`tabIndex={-1}`, programmatic only), since the card has no gear of its own.
 *
 * The foot keeps the guest ledger's classes on purpose: `MarkAllReadRow` moves focus to the last
 * `.jp-notice-foot__toggle` in the document, and that contract is the reason the foot exists in
 * both cards rather than once on the page.
 */
export function NoticeListCard({
  id,
  title,
  notices,
  emptyText,
  span,
  modifier,
  listAs: List,
  listClassName,
  renderRow,
}: NoticeListCardProps) {
  const t = useTranslations("oversikt");
  const rootRef = useRef<HTMLElement>(null);
  const {
    unread,
    read,
    showRead,
    toggleShowRead,
    handleDismiss,
    handleRestore,
    footToggleRef,
  } = useNoticeList(notices, { fallbackRef: rootRef });

  const empty = unread.length === 0;

  return (
    <section
      ref={rootRef}
      tabIndex={-1}
      className={["jp-ov-card", "jp-ov-card--list", modifier].filter(Boolean).join(" ")}
      data-span={span}
      data-empty={empty ? "true" : undefined}
      aria-labelledby={id}
    >
      <div className="jp-ov-card__head">
        <h2 className="jp-ov-card__title jp-ov-card__title--lg" id={id}>
          {title}
        </h2>
        <span className="jp-ov-card__count">
          {t("notices.unreadCount", { count: unread.length })}
        </span>
      </div>

      <List className={listClassName}>
        {unread.length > 0 ? (
          unread.map((n) => renderRow(n, false, handleDismiss, handleRestore))
        ) : (
          <li className="jp-ov-list-empty">{emptyText}</li>
        )}
        {showRead && read.map((n) => renderRow(n, true, handleDismiss, handleRestore))}
        {read.length > 0 && (
          <li className="jp-notice-foot">
            <span className="jp-notice-foot__count">
              {t("notices.readCount", { count: read.length })}
            </span>
            <button
              ref={footToggleRef}
              type="button"
              className="jp-notice-foot__toggle"
              aria-expanded={showRead}
              onClick={toggleShowRead}
            >
              {showRead ? t("notices.hideRead") : t("notices.showRead")}
            </button>
          </li>
        )}
      </List>
    </section>
  );
}

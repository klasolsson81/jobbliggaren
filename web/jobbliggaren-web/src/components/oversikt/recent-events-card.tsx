"use client";

import Link from "next/link";
import { useTranslations } from "next-intl";
import { ArrowRight } from "lucide-react";
import { NOTICE_ICONS } from "./notice-icons";
import { NoticeDismissButton } from "./notice-dismiss-button";
import { NoticeListCard } from "./notice-list-card";
import type { SectionNoticeData } from "./notice-types";

interface RecentEventsCardProps {
  /** The `info` notices — matching, followed-company ads, the latest search. */
  readonly notices: ReadonlyArray<SectionNoticeData>;
}

/**
 * "Senaste händelser" — the information feed at the foot of `/oversikt` (ADR 0140). One hairline
 * row per notice: icon box, mono label, text, a text-link CTA, the time and the dismiss control.
 * The CTA is a link, not a button: nothing here demands an action, so nothing here wears a
 * button.
 *
 * A real event log — status changes, company events, timestamps that are measured rather than
 * "idag" — is backend work under #1666; this card renders the three notices that exist today.
 *
 * A Client Component only because `renderRow` is a function prop into `NoticeListCard`, and a
 * function cannot cross the RSC boundary — the card holds no state of its own.
 */
export function RecentEventsCard({ notices }: RecentEventsCardProps) {
  const t = useTranslations("oversikt.cards");
  return (
    <NoticeListCard
      id="oversikt-card-events"
      title={t("events")}
      notices={notices}
      emptyText={t("eventsEmpty")}
      span={12}
      listAs="ul"
      listClassName="jp-ov-events"
      renderRow={(n, read, onDismiss, onRestore) => {
        const Icon = NOTICE_ICONS[n.type];
        return (
          <li
            key={n.id}
            className={read ? "jp-ov-event jp-ov-event--read" : "jp-ov-event"}
            data-kind={n.kind}
          >
            <span className="jp-ov-event__icon" aria-hidden="true">
              <Icon size={18} aria-hidden="true" />
            </span>
            <span className="jp-ov-event__label">{n.label}</span>
            <span className="jp-ov-event__text">{n.text}</span>
            <Link href={n.href} className="jp-notice__cta">
              {n.cta} <ArrowRight size={13} aria-hidden="true" />
            </Link>
            <span className="jp-notice__time">{n.time}</span>
            {n.dismissible !== false && (
              <NoticeDismissButton
                id={n.id}
                read={read}
                onDismiss={onDismiss}
                onRestore={onRestore}
              />
            )}
          </li>
        );
      }}
    />
  );
}

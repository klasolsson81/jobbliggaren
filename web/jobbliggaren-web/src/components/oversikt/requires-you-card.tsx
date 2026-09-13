"use client";

import Link from "next/link";
import { useTranslations } from "next-intl";
import { ArrowRight } from "lucide-react";
import { NOTICE_ICONS } from "./notice-icons";
import { NoticeDismissButton } from "./notice-dismiss-button";
import { NoticeListCard } from "./notice-list-card";
import type { SectionNoticeData } from "./notice-types";

interface RequiresYouCardProps {
  /** The action notices — every kind but `info`. The orchestrator does the split. */
  readonly notices: ReadonlyArray<SectionNoticeData>;
}

/**
 * "Kräver dig" — the action queue at the top of `/oversikt` (ADR 0140): follow-ups, deadlines,
 * interviews and offers, one row each with an icon box in the notice's kind colour, its label
 * and time, its text and its row action. The row CTA is `.jp-btn--sm .jp-btn--emphasis`:
 * emphasised, never solid — N rows would be N solid buttons (DESIGN.md §6, CTO-bind 2026-07-12).
 *
 * The empty state keeps the card and swaps the warning bar for a neutral one (CSS on
 * `data-empty`): a card that vanished would take the grid's shape with it.
 *
 * A Client Component only because `renderRow` is a function prop into `NoticeListCard`, and a
 * function cannot cross the RSC boundary — the card holds no state of its own.
 */
export function RequiresYouCard({ notices }: RequiresYouCardProps) {
  const t = useTranslations("oversikt.cards");
  return (
    <NoticeListCard
      id="oversikt-card-requires-you"
      title={t("requiresYou")}
      notices={notices}
      emptyText={t("requiresYouEmpty")}
      span={8}
      modifier="jp-ov-card--requires"
      listAs="ol"
      listClassName="jp-ov-actions"
      renderRow={(n, read, onDismiss, onRestore) => {
        const Icon = NOTICE_ICONS[n.type];
        return (
          <li
            key={n.id}
            className={read ? "jp-ov-action jp-ov-action--read" : "jp-ov-action"}
            data-kind={n.kind}
          >
            <span className="jp-ov-action__icon" aria-hidden="true">
              <Icon size={20} aria-hidden="true" />
            </span>
            <div className="jp-ov-action__body">
              <p className="jp-ov-action__meta">
                <span className="jp-ov-action__label">{n.label}</span>
                {n.time !== "" && <span className="jp-ov-action__time">{n.time}</span>}
              </p>
              <p className="jp-ov-action__text">{n.text}</p>
            </div>
            <Link
              href={n.href}
              className="jp-btn jp-btn--sm jp-btn--emphasis jp-ov-action__cta"
            >
              {n.cta} <ArrowRight size={13} aria-hidden="true" />
            </Link>
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

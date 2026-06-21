"use client";

import Link from "next/link";
import { useTranslations } from "next-intl";
import { ArrowRight, X } from "lucide-react";
import type { ReactNode } from "react";

export type NoticeKind = "info" | "warning" | "brand" | "success";

export interface NoticeData {
  readonly id: string;
  readonly kind: NoticeKind;
  readonly label: string;
  readonly text: ReactNode;
  readonly cta: string;
  readonly href: string;
  readonly time: string;
  /**
   * F4-12 PR-B (ADR 0076): en notis kan vara icke-avfärdbar (default `true`).
   * `false` på den persistenta setup-nudgen — den ska inte gå att markera som
   * läst, den löses upp först när användaren angett ett yrke. Då renderas
   * ingen dismiss-knapp (X).
   */
  readonly dismissible?: boolean;
}

interface NoticeRowProps {
  readonly notice: NoticeData;
  readonly onDismiss: (id: string) => void;
}

export function NoticeRow({ notice, onDismiss }: NoticeRowProps) {
  const t = useTranslations("oversikt");
  return (
    <li className={`jp-notice jp-notice--${notice.kind}`}>
      <span className="jp-notice__strip" aria-hidden="true" />
      <span className="jp-notice__label">{notice.label}</span>
      <span className="jp-notice__text">{notice.text}</span>
      <Link href={notice.href} className="jp-notice__cta">
        {notice.cta} <ArrowRight size={13} aria-hidden="true" />
      </Link>
      {notice.time !== "" && (
        <span className="jp-notice__time">{notice.time}</span>
      )}
      {notice.dismissible !== false && (
        <button
          type="button"
          className="jp-notice__dismiss"
          aria-label={t("notices.dismiss")}
          title={t("notices.dismiss")}
          onClick={() => onDismiss(notice.id)}
        >
          <X size={16} aria-hidden="true" />
        </button>
      )}
    </li>
  );
}

"use client";

import Link from "next/link";
import { useTranslations } from "next-intl";
import { ArrowRight, RotateCcw, X } from "lucide-react";
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
  /** Markera raden som läst. Utelämnas i läst-läge (då används `onRestore`). */
  readonly onDismiss?: (id: string) => void;
  /**
   * #726 läst-läge: en dismissad notis göms inte permanent utan flyttas till
   * sektionens dolda läst-läge. `read` dimmar raden (opacity .55) och byter
   * X-knappen mot en RotateCcw-knapp som återställer (av-markerar) notisen.
   */
  readonly read?: boolean;
  readonly onRestore?: (id: string) => void;
}

export function NoticeRow({ notice, onDismiss, read, onRestore }: NoticeRowProps) {
  const t = useTranslations("oversikt");
  const className = read
    ? `jp-notice jp-notice--${notice.kind} jp-notice--read`
    : `jp-notice jp-notice--${notice.kind}`;
  return (
    <li className={className}>
      <span className="jp-notice__strip" aria-hidden="true" />
      <span className="jp-notice__label">{notice.label}</span>
      <span className="jp-notice__text">{notice.text}</span>
      <Link href={notice.href} className="jp-notice__cta">
        {notice.cta} <ArrowRight size={13} aria-hidden="true" />
      </Link>
      {notice.time !== "" && (
        <span className="jp-notice__time">{notice.time}</span>
      )}
      {read
        ? onRestore && (
            <button
              type="button"
              className="jp-notice__dismiss"
              aria-label={t("notices.restore")}
              title={t("notices.restore")}
              onClick={() => onRestore(notice.id)}
            >
              <RotateCcw size={14} aria-hidden="true" />
            </button>
          )
        : notice.dismissible !== false &&
          onDismiss && (
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

"use client";

import Link from "next/link";
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
}

interface NoticeRowProps {
  readonly notice: NoticeData;
  readonly onDismiss: (id: string) => void;
}

export function NoticeRow({ notice, onDismiss }: NoticeRowProps) {
  return (
    <li className={`jp-notice jp-notice--${notice.kind}`}>
      <span className="jp-notice__strip" aria-hidden="true" />
      <span className="jp-notice__label">{notice.label}</span>
      <span className="jp-notice__text">{notice.text}</span>
      <Link href={notice.href} className="jp-notice__cta">
        {notice.cta} <ArrowRight size={13} aria-hidden="true" />
      </Link>
      <span className="jp-notice__time">{notice.time}</span>
      <button
        type="button"
        className="jp-notice__dismiss"
        aria-label="Markera som läst"
        title="Markera som läst"
        onClick={() => onDismiss(notice.id)}
      >
        <X size={16} aria-hidden="true" />
      </button>
    </li>
  );
}

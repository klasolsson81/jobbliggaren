"use client";

import { useTranslations } from "next-intl";
import { RotateCcw, X } from "lucide-react";

interface NoticeDismissButtonProps {
  readonly id: string;
  /** Read rows swap the X for a restore (RotateCcw) control — same slot, inverse action. */
  readonly read: boolean;
  readonly onDismiss: (id: string) => void;
  readonly onRestore: (id: string) => void;
}

/**
 * The dismiss/restore control the two list cards on `/oversikt` share. Same class, same size
 * and the same accessible names as the guest ledger's `NoticeRow`, which keeps its own copy so
 * that surface stays byte-identical (ADR 0140).
 */
export function NoticeDismissButton({ id, read, onDismiss, onRestore }: NoticeDismissButtonProps) {
  const t = useTranslations("oversikt");
  const label = read ? t("notices.restore") : t("notices.dismiss");
  return (
    <button
      type="button"
      className="jp-notice__dismiss"
      aria-label={label}
      title={label}
      onClick={() => (read ? onRestore(id) : onDismiss(id))}
    >
      {read ? (
        <RotateCcw size={14} aria-hidden="true" />
      ) : (
        <X size={16} aria-hidden="true" />
      )}
    </button>
  );
}

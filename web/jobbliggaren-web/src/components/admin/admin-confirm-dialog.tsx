"use client";

import { useRef, useState } from "react";
import { AlertDialog } from "radix-ui";
import { useTranslations } from "next-intl";

interface AdminConfirmDialogProps {
  readonly open: boolean;
  readonly title: string;
  readonly body: string;
  readonly confirmLabel: string;
  /** Resolves to a refusal to show in the dialog, or null when the action went through. */
  readonly onConfirm: () => Promise<string | null>;
  readonly onCancel: () => void;
  /** Where focus goes when the dialog closes; the control that opened it may be gone by then. */
  readonly onCloseAutoFocus?: (event: Event) => void;
}

/**
 * The confirmation a destructive account action asks for (DESIGN.md §6). Focus starts on Avbryt,
 * the confirming button names the action, and a refusal stays in the dialog as an alert. The dialog
 * closes only when the action went through: the caller closes it then.
 */
export function AdminConfirmDialog({
  open,
  title,
  body,
  confirmLabel,
  onConfirm,
  onCancel,
  onCloseAutoFocus,
}: AdminConfirmDialogProps) {
  const t = useTranslations("admin.users.confirm");
  const cancelRef = useRef<HTMLButtonElement>(null);
  const [pending, setPending] = useState(false);
  const [refusal, setRefusal] = useState<string | null>(null);

  async function confirm() {
    setPending(true);
    setRefusal(null);
    const outcome = await onConfirm();
    setPending(false);
    if (outcome !== null) setRefusal(outcome);
  }

  return (
    <AlertDialog.Root
      open={open}
      onOpenChange={(next) => {
        if (!next && !pending) onCancel();
      }}
    >
      <AlertDialog.Portal>
        <AlertDialog.Overlay className="jp-adminconfirm-scrim" />
        <AlertDialog.Content
          className="jp-adminconfirm"
          onOpenAutoFocus={(event) => {
            event.preventDefault();
            cancelRef.current?.focus();
          }}
          onCloseAutoFocus={onCloseAutoFocus}
        >
          <AlertDialog.Title className="jp-adminconfirm__title">{title}</AlertDialog.Title>
          <AlertDialog.Description className="jp-adminconfirm__body">{body}</AlertDialog.Description>
          {refusal === null ? null : (
            <p className="jp-adminconfirm__refusal" role="alert">
              {refusal}
            </p>
          )}
          <div className="jp-adminconfirm__actions">
            <AlertDialog.Cancel ref={cancelRef} className="jp-btn jp-btn--secondary" disabled={pending}>
              {t("cancel")}
            </AlertDialog.Cancel>
            <button type="button" className="jp-btn jp-btn--danger" disabled={pending} onClick={confirm}>
              {confirmLabel}
            </button>
          </div>
        </AlertDialog.Content>
      </AlertDialog.Portal>
    </AlertDialog.Root>
  );
}

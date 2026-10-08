"use client";

// "use client": a controlled Radix alert dialog with a running command and its refusal.
import { useEffect, useRef, useState, useTransition } from "react";
import { AlertDialog } from "radix-ui";
import { useTranslations } from "next-intl";
import { AdminBusyLabel } from "./admin-busy-label";

interface AdminConfirmDialogProps {
  readonly open: boolean;
  readonly title: string;
  readonly body: string;
  readonly confirmLabel: string;
  /** The confirming button's label while the command runs, such as "Suspenderar…". */
  readonly busyLabel: string;
  /** Resolves to a refusal to show in the dialog, or null when the action went through. */
  readonly onConfirm: () => Promise<string | null>;
  readonly onCancel: () => void;
  /** Where focus goes when the dialog closes; the control that opened it may be gone by then. */
  readonly onCloseAutoFocus?: (event: Event) => void;
  /**
   * The confirming button's tone. `danger`, the default, is for an action that removes or locks
   * something, such as suspending an account. `neutral` is for one that destroys nothing and whose
   * consequence the body states, such as a notice sent again: its button is the dialog's one primary.
   */
  readonly tone?: "danger" | "neutral";
}

/**
 * The confirmation an administrator's action asks for before it runs (DESIGN.md §6): a destructive
 * account action, or one whose consequence must be read first. Focus starts on Avbryt, the confirming
 * button names the action, and a refusal stays in the dialog as an alert that takes focus. The dialog
 * closes only when the action went through: the caller closes it then. A command that throws ends at
 * the nearest error boundary rather than holding the dialog open.
 */
export function AdminConfirmDialog({
  open,
  title,
  body,
  confirmLabel,
  busyLabel,
  onConfirm,
  onCancel,
  onCloseAutoFocus,
  tone = "danger",
}: AdminConfirmDialogProps) {
  const t = useTranslations("admin.users.confirm");
  const cancelRef = useRef<HTMLButtonElement>(null);
  const refusalRef = useRef<HTMLParagraphElement>(null);
  const [pending, startTransition] = useTransition();
  const [refusal, setRefusal] = useState<string | null>(null);

  useEffect(() => {
    if (refusal !== null) refusalRef.current?.focus();
  }, [refusal]);

  function confirm() {
    setRefusal(null);
    startTransition(async () => {
      const outcome = await onConfirm();
      startTransition(() => {
        if (outcome !== null) setRefusal(outcome);
      });
    });
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
            <p ref={refusalRef} tabIndex={-1} className="jp-adminconfirm__refusal" role="alert">
              {refusal}
            </p>
          )}
          <div className="jp-adminconfirm__actions">
            <AlertDialog.Cancel ref={cancelRef} className="jp-btn jp-btn--secondary" disabled={pending}>
              {t("cancel")}
            </AlertDialog.Cancel>
            <button
              type="button"
              className={tone === "neutral" ? "jp-btn jp-btn--primary" : "jp-btn jp-btn--danger"}
              disabled={pending}
              onClick={confirm}
            >
              <AdminBusyLabel busy={pending} label={confirmLabel} busyLabel={busyLabel} />
            </button>
          </div>
        </AlertDialog.Content>
      </AlertDialog.Portal>
    </AlertDialog.Root>
  );
}

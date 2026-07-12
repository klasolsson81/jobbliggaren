"use client";

import { useState, useTransition } from "react";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { deleteApplicationAction } from "@/lib/actions/applications";

interface DeleteApplicationDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  applicationId: string;
  /**
   * Called after a successful HARD delete (the dialog has already closed). The
   * detail page uses it to navigate away from the now-deleted application; the
   * list surfaces omit it (the revalidatePath server-recompute drops the row).
   */
  onDeleted?: () => void;
}

/**
 * #782 (ADR 0104) — destructive confirm for the per-application HARD delete
 * ("Radera ansökan"). Controlled (open state owned by the caller) so the pipeline
 * island mounts ONE shared instance via ApplicationActionsProvider (never N per
 * row), and the detail footer reuses the same body. Reuses the EXACT shadcn Dialog
 * destructive-confirm idiom as WithdrawApplicationButton/StatusEditCard (ADR 0047:
 * the consequence is stated BEFORE the action; the confirm button is the specific
 * "Radera ansökan", never "Bekräfta"/"OK") — one destructive-confirm pattern, no
 * divergent flow. Irreversible by design (no undo), matching the copy.
 */
export function DeleteApplicationDialog({
  open,
  onOpenChange,
  applicationId,
  onDeleted,
}: DeleteApplicationDialogProps) {
  const tUi = useTranslations("applications.ui");
  const [error, setError] = useState<string | null>(null);
  const [isPending, startTransition] = useTransition();

  function confirm() {
    setError(null);
    startTransition(async () => {
      const result = await deleteApplicationAction(applicationId);
      if (!result.success) {
        setError(result.error);
        return;
      }
      onOpenChange(false);
      onDeleted?.();
    });
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next && !isPending) {
          onOpenChange(false);
          setError(null);
        }
      }}
    >
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{tUi("delete.confirmTitle")}</DialogTitle>
          <DialogDescription>{tUi("delete.confirmBody")}</DialogDescription>
        </DialogHeader>
        {error && (
          <p role="alert" className="text-body-sm text-danger-700">
            {error}
          </p>
        )}
        <DialogFooter>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            disabled={isPending}
            onClick={() => {
              onOpenChange(false);
              setError(null);
            }}
          >
            {tUi("common.cancel")}
          </Button>
          <Button
            type="button"
            variant="destructive"
            size="sm"
            disabled={isPending}
            onClick={confirm}
          >
            {isPending ? tUi("delete.deleting") : tUi("delete.confirmButton")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

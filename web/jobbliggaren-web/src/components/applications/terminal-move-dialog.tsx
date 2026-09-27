"use client";

// Client Component: a controlled Radix Dialog, opened from the status controls' click handlers.

import { flushSync } from "react-dom";
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
import { applicationStatusLabel } from "@/lib/applications/status";
import type { ApplicationStatus } from "@/lib/dto/applications";
import { useReturnFocus } from "@/lib/hooks/use-return-focus";

interface TerminalMoveDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** The status in `MINIMISING_STATUSES` that the move goes to. */
  target: ApplicationStatus;
  /** Called after the dialog closes; the caller runs the move. */
  onConfirm: () => void;
}

/**
 * The consequence of a move that deletes the saved copy's text, shown before the move
 * (ADR 0047 point 3, DESIGN.md §6). Callers open it when `needsTerminalMoveConfirmation`
 * says so. It does not run the transition: the caller does, with its own pending and error
 * state, so the move starts only after the confirmation.
 */
export function TerminalMoveDialog({
  open,
  onOpenChange,
  target,
  onConfirm,
}: TerminalMoveDialogProps) {
  const t = useTranslations("applications.enums");
  const tUi = useTranslations("applications.ui");
  const status = applicationStatusLabel(t, target);
  const { onCloseAutoFocus, returnFocus } = useReturnFocus(open);

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent onCloseAutoFocus={onCloseAutoFocus}>
        <DialogHeader>
          <DialogTitle>{tUi("terminalMove.title", { status })}</DialogTitle>
          <DialogDescription>{tUi("terminalMove.body")}</DialogDescription>
        </DialogHeader>
        <DialogFooter>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={() => onOpenChange(false)}
          >
            {tUi("common.cancel")}
          </Button>
          <Button
            type="button"
            variant="destructive"
            size="sm"
            onClick={() => {
              flushSync(() => onOpenChange(false));
              returnFocus();
              onConfirm();
            }}
          >
            {tUi("terminalMove.confirm", { status })}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

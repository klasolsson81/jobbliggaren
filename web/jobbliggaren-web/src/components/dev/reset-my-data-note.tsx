"use client";

import { useState, useTransition } from "react";
import { useRouter } from "next/navigation";
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
import { resetMyDataAction } from "@/lib/dev/reset-actions";

/**
 * DEV-ONLY — REMOVE BEFORE LAUNCH (Klas), with the flag and the endpoint
 * (docs/runbooks/release-checklist.md). An unobtrusive note at the bottom of /oversikt
 * that wipes the caller's own test data and lets the welcome setup run again.
 *
 * The caller MUST render this only when the reset is actually enabled — defence in depth
 * alongside the backend, which maps the route only under `IsDevelopment()` or an explicit
 * `DevTools:EnableResetMyData`, and refuses a second time inside the handler.
 *
 * <b>It is a confirmation dialog, not a bare submit.</b> It was a one-click irreversible
 * wipe while it lived only in Development. Reachable on a box that is about to have real
 * test users on it, one misplaced click is a different proposition, so it now follows the
 * house destructive idiom (ADR 0047, template `delete-application-dialog.tsx`): the
 * consequence is stated before the action, and the confirm button carries the verb rather
 * than "Bekräfta".
 */
export function ResetMyDataNote() {
  const t = useTranslations("common");
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [isPending, startTransition] = useTransition();

  function confirm() {
    setError(null);
    startTransition(async () => {
      const result = await resetMyDataAction();
      if (!result.success) {
        setError(result.error);
        return;
      }
      setOpen(false);
      // The action revalidates /oversikt; refresh so the re-armed welcome modal mounts
      // without a manual reload.
      router.refresh();
    });
  }

  return (
    <div className="mt-8 rounded-md border border-dashed border-border bg-muted/40 p-4 text-body-sm leading-5 text-text-secondary">
      <p className="mb-2">{t("dev.note")}</p>
      <Button
        type="button"
        variant="outline"
        size="sm"
        onClick={() => setOpen(true)}
      >
        {t("dev.resetButton")}
      </Button>

      <Dialog
        open={open}
        onOpenChange={(next) => {
          if (!next && !isPending) {
            setOpen(false);
            setError(null);
          }
        }}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{t("dev.confirmTitle")}</DialogTitle>
            <DialogDescription>{t("dev.confirmBody")}</DialogDescription>
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
                setOpen(false);
                setError(null);
              }}
            >
              {t("dev.cancel")}
            </Button>
            <Button
              type="button"
              variant="destructive"
              size="sm"
              disabled={isPending}
              onClick={confirm}
            >
              {isPending ? t("dev.resetting") : t("dev.confirmButton")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

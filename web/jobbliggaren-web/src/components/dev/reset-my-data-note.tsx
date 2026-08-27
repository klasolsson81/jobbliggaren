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
  DialogTrigger,
} from "@/components/ui/dialog";
import { resetMyDataAction } from "@/lib/dev/reset-actions";

/**
 * DEV-ONLY — REMOVE BEFORE LAUNCH (Klas), with the flag and the endpoint
 * (docs/runbooks/release-checklist.md). An unobtrusive note at the bottom of /oversikt
 * that wipes the caller's own test data and lets the welcome setup run again.
 *
 * The caller renders this on `NODE_ENV !== "production" || DEV_TOOLS_RESET_ENABLED` — a
 * wider predicate than the backend's, which is the flag alone. In Development the two
 * agree because `appsettings.Development.json` sets the flag; override it off there and
 * the button renders while every press is refused. Read the whole thing for what it is: a
 * RENDER gate, not an authorisation one. The module is imported unconditionally, so the
 * server action's id exists in the module graph and stays callable whatever the flag says.
 * The authoritative gates are both on the backend — the route is mapped only under
 * `DevTools:EnableResetMyData`, and the handler refuses again on the same flag.
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
      <Dialog
        open={open}
        onOpenChange={(next) => {
          // BOTH directions. The dialog is controlled (`open` is always defined), so
          // Radix never flips its own state: the trigger's open arrives here and nowhere
          // else. Handling only the close branch threw it away and the dialog could not
          // be opened at all.
          if (next) {
            setError(null);
            setOpen(true);
            return;
          }
          if (!isPending) {
            setOpen(false);
            setError(null);
          }
        }}
      >
        {/* DialogTrigger, not a bare Button with onClick. Radix's close handler calls
            preventDefault() unconditionally and then focuses triggerRef; with no trigger
            registered that ref is null, FocusScope's own restoration is suppressed by the
            preventDefault, and focus lands on <body> (WCAG 2.4.3). Same defect as #748;
            a different remedy — that one kept a triggerless controlled dialog and restored
            focus in onCloseAutoFocus, this one registers the trigger, which also supplies
            aria-haspopup and aria-expanded for free. */}
        <DialogTrigger asChild>
          <Button type="button" variant="outline" size="sm">
            {t("dev.resetButton")}
          </Button>
        </DialogTrigger>
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
              // Width is held across the label swap so the footer does not reflow while
              // the reset runs (DESIGN.md 6: replace the label, keep the width).
              className="min-w-[10.5rem]"
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

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
import { transitionStatusAction } from "@/lib/actions/applications";
import { applicationStatusLabel } from "@/lib/applications/status";
import type { ApplicationStatus } from "@/lib/types/applications";

interface WithdrawApplicationButtonProps {
  applicationId: string;
  currentStatus: ApplicationStatus;
}

/**
 * Footer-handling "Återta ansökan" = Withdrawn-transition (DOMÄN-KORREKT
 * soft-state-övergång som BEHÅLLER posten som en terminal status). Att helt TA
 * BORT en ansökan är en SEPARAT affordans ("Radera ansökan", #782/ADR 0104 — en
 * riktig hard-delete via DELETE /api/v1/applications/{id}, DeleteApplicationButton);
 * de två intenten samexisterar åtskilt (återta = behåll posten; ta bort = utplåna).
 * Renderas av anroparen ENDAST när Withdrawn ∈ getAllowedTransitions
 * (annars utelämnas helt — ingen disabled-teater, ADR 0053-amendment-anda).
 *
 * Withdrawn är en DESTRUKTIV övergång (DESTRUCTIVE_STATUSES). ADR 0047
 * Area 5 (design-reviewer hård-veto): konsekvensen kommuniceras FÖRE
 * handling via Dialog-bekräftelse med pre-action-konsekvenstext. Återanvänder
 * EXAKT samma shadcn Dialog-bekräftelseidiom + transitionStatusAction som
 * StatusEditCard — ingen parallell flödesväg, ingen ADR 0047-divergens
 * (DRY av destruktiv-confirm-mönstret, CLAUDE.md §9.1). Status-byte i
 * StatusEditCard och här går genom samma Server Action → samma
 * revalidatePath, samma backend-invariant (ALLOWED_TRANSITIONS).
 */
export function WithdrawApplicationButton({
  applicationId,
  currentStatus,
}: WithdrawApplicationButtonProps) {
  const t = useTranslations("applications.enums");
  const tUi = useTranslations("applications.ui");
  const [open, setOpen] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [isPending, startTransition] = useTransition();

  const currentLabel = applicationStatusLabel(t, currentStatus);
  const withdrawnLabel = applicationStatusLabel(t, "Withdrawn");

  function confirm() {
    setError(null);
    startTransition(async () => {
      const result = await transitionStatusAction(
        applicationId,
        "Withdrawn"
      );
      if (!result.success) {
        setError(result.error);
        return;
      }
      setOpen(false);
    });
  }

  return (
    <>
      <Button
        type="button"
        variant="ghost"
        size="sm"
        className="text-danger-700"
        onClick={() => setOpen(true)}
      >
        {tUi("withdraw.action")}
      </Button>

      <Dialog
        open={open}
        onOpenChange={(o) => {
          if (!o && !isPending) {
            setOpen(false);
            setError(null);
          }
        }}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{tUi("withdraw.confirmTitle")}</DialogTitle>
            <DialogDescription>
              {tUi.rich("withdraw.confirmBody", {
                from: currentLabel,
                to: withdrawnLabel,
                b: (chunks) => <strong>{chunks}</strong>,
              })}
            </DialogDescription>
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
              {tUi("common.cancel")}
            </Button>
            <Button
              type="button"
              variant="destructive"
              size="sm"
              disabled={isPending}
              onClick={confirm}
            >
              {isPending ? tUi("withdraw.withdrawing") : tUi("withdraw.action")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}

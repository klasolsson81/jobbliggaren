"use client";

import { useState, useTransition } from "react";
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
import { getStatusLabel } from "@/lib/applications/status";
import type { ApplicationStatus } from "@/lib/types/applications";

interface WithdrawApplicationButtonProps {
  applicationId: string;
  currentStatus: ApplicationStatus;
}

/**
 * Footer-handling "Återta ansökan" = Withdrawn-transition (DOMÄN-KORREKT
 * soft-state-övergång, EJ hard-delete — ingen DELETE-endpoint finns).
 * v3-prototypens "Ta bort ansökan" var mock; real domän = Withdrawn.
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
  const [open, setOpen] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [isPending, startTransition] = useTransition();

  const currentLabel = getStatusLabel(currentStatus);
  const withdrawnLabel = getStatusLabel("Withdrawn");

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
        Återta ansökan
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
            <DialogTitle>Återta ansökan?</DialogTitle>
            <DialogDescription>
              Ansökan ändras från <strong>{currentLabel}</strong> till{" "}
              <strong>{withdrawnLabel}</strong>. En återtagen ansökan
              avslutas och kan inte ändras vidare utan manuell åtgärd.
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
              Avbryt
            </Button>
            <Button
              type="button"
              variant="destructive"
              size="sm"
              disabled={isPending}
              onClick={confirm}
            >
              {isPending ? "Återtar…" : "Återta ansökan"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}

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
import {
  getAllowedTransitions,
  getStatusLabel,
  isDestructiveTransition,
} from "@/lib/applications/status";
import type { ApplicationStatus } from "@/lib/types/applications";

interface TransitionFormProps {
  applicationId: string;
  currentStatus: ApplicationStatus;
}

export function TransitionForm({ applicationId, currentStatus }: TransitionFormProps) {
  const [isPending, startTransition] = useTransition();
  const [error, setError] = useState<string | null>(null);
  const [pendingTarget, setPendingTarget] = useState<ApplicationStatus | null>(null);

  const transitions = getAllowedTransitions(currentStatus);

  if (transitions.length === 0) return null;

  function handleTransition(target: ApplicationStatus) {
    if (isDestructiveTransition(target)) {
      setPendingTarget(target);
      return;
    }
    executeTransition(target);
  }

  function executeTransition(target: ApplicationStatus) {
    setError(null);
    startTransition(async () => {
      const result = await transitionStatusAction(applicationId, target);
      if (!result.success) {
        setError(result.error);
      }
      setPendingTarget(null);
    });
  }

  return (
    <div className="flex flex-col gap-3">
      <h3 className="text-body font-medium text-text-primary">Byt status</h3>
      <div className="flex flex-wrap gap-2">
        {transitions.map((target) => (
          <Button
            key={target}
            variant={isDestructiveTransition(target) ? "destructive" : "outline"}
            size="sm"
            disabled={isPending}
            onClick={() => handleTransition(target)}
          >
            {getStatusLabel(target)}
          </Button>
        ))}
      </div>
      {error && <p className="text-body-sm text-danger-600">{error}</p>}

      <Dialog
        open={pendingTarget !== null}
        onOpenChange={(open) => { if (!open) setPendingTarget(null); }}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Är du säker?</DialogTitle>
            <DialogDescription>
              Du är på väg att markera ansökan som{" "}
              <strong>{pendingTarget ? getStatusLabel(pendingTarget) : ""}</strong>.
              Det går inte att ångra utan manuell åtgärd.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button
              variant="outline"
              size="sm"
              onClick={() => setPendingTarget(null)}
            >
              Avbryt
            </Button>
            <Button
              variant="destructive"
              size="sm"
              disabled={isPending}
              onClick={() => pendingTarget && executeTransition(pendingTarget)}
            >
              Bekräfta
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

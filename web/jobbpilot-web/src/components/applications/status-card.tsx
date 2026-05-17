"use client";

import { useState, useTransition } from "react";
import { Button } from "@/components/ui/button";
import { StatusPill, type PillTone } from "@/components/ui/status-pill";
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
  STATUS_BADGE_VARIANT,
  type BadgeVariant,
} from "@/lib/applications/status";
import type { ApplicationStatus } from "@/lib/types/applications";

interface StatusCardProps {
  applicationId: string;
  currentStatus: ApplicationStatus;
  createdAt: string;
  updatedAt: string;
}

const PILL_TONE: Record<BadgeVariant, PillTone> = {
  Info: "info",
  Brand: "brand",
  Success: "success",
  Warning: "warning",
  Danger: "danger",
  Neutral: "neutral",
};

/**
 * Status-kort (GOV.UK summary-card-mönster). Nuvarande status är alltid
 * synlig och förankrad i samma kort som ändringen. "Ändra status" är en
 * progressiv disclosure (aria-expanded) — inte en farlig dold select.
 * Destruktiva övergångar bekräftas i dialog innan de utförs.
 */
export function StatusCard({
  applicationId,
  currentStatus,
  createdAt,
  updatedAt,
}: StatusCardProps) {
  const [isPending, startTransition] = useTransition();
  const [error, setError] = useState<string | null>(null);
  const [isChanging, setIsChanging] = useState(false);
  const [pendingTarget, setPendingTarget] = useState<ApplicationStatus | null>(
    null
  );

  const transitions = getAllowedTransitions(currentStatus);
  const tone = PILL_TONE[STATUS_BADGE_VARIANT[currentStatus]] ?? "neutral";

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
      } else {
        setIsChanging(false);
      }
      setPendingTarget(null);
    });
  }

  return (
    <section
      aria-labelledby="status-card-title"
      className="rounded-md border border-border bg-surface-primary"
    >
      <div className="flex items-center justify-between gap-4 border-b border-border px-4 py-3">
        <h2
          id="status-card-title"
          className="text-h3 font-medium text-text-primary"
        >
          Status
        </h2>
        {transitions.length > 0 && (
          <Button
            type="button"
            variant="outline"
            size="sm"
            aria-expanded={isChanging}
            aria-controls="status-change-region"
            onClick={() => {
              setIsChanging((v) => !v);
              setError(null);
            }}
          >
            {isChanging ? "Avbryt ändring" : "Ändra status"}
          </Button>
        )}
      </div>

      <div className="flex flex-col gap-4 px-4 py-4">
        <dl className="flex flex-col gap-3">
          <div className="flex items-center gap-2">
            <dt className="text-body-sm text-text-secondary">Nuvarande status:</dt>
            <dd>
              <StatusPill tone={tone}>{getStatusLabel(currentStatus)}</StatusPill>
            </dd>
          </div>
          <div className="flex flex-wrap gap-x-6 gap-y-1 text-body-sm text-text-secondary">
            <div className="flex gap-1">
              <dt>Skapad:</dt>
              <dd className="font-mono">{createdAt}</dd>
            </div>
            <div className="flex gap-1">
              <dt>Uppdaterad:</dt>
              <dd className="font-mono">{updatedAt}</dd>
            </div>
          </div>
        </dl>

        {isChanging && transitions.length > 0 && (
          <div
            id="status-change-region"
            className="flex flex-col gap-3 border-t border-border pt-4"
          >
            <p className="text-body-sm text-text-secondary">
              Välj ny status. Nuvarande status är{" "}
              <span className="font-medium text-text-primary">
                {getStatusLabel(currentStatus)}
              </span>
              .
            </p>
            <div className="flex flex-wrap gap-2">
              {transitions.map((target) => (
                <Button
                  key={target}
                  type="button"
                  variant={
                    isDestructiveTransition(target) ? "destructive" : "outline"
                  }
                  size="sm"
                  disabled={isPending}
                  onClick={() => handleTransition(target)}
                >
                  {getStatusLabel(target)}
                </Button>
              ))}
            </div>
            {error && (
              <p role="alert" className="text-body-sm text-danger-700">
                {error}
              </p>
            )}
          </div>
        )}
      </div>

      <Dialog
        open={pendingTarget !== null}
        onOpenChange={(open) => {
          if (!open) setPendingTarget(null);
        }}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>
              Markera som{" "}
              {pendingTarget ? getStatusLabel(pendingTarget) : ""}?
            </DialogTitle>
            <DialogDescription>
              Ansökan ändras från{" "}
              <strong>{getStatusLabel(currentStatus)}</strong> till{" "}
              <strong>
                {pendingTarget ? getStatusLabel(pendingTarget) : ""}
              </strong>
              . Det går inte att ångra utan manuell åtgärd.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => setPendingTarget(null)}
            >
              Avbryt
            </Button>
            <Button
              type="button"
              variant="destructive"
              size="sm"
              disabled={isPending}
              onClick={() =>
                pendingTarget && executeTransition(pendingTarget)
              }
            >
              {pendingTarget
                ? `Markera som ${getStatusLabel(pendingTarget)}`
                : "Bekräfta"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </section>
  );
}

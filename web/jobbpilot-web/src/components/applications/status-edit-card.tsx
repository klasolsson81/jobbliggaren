"use client";

import { useId, useState, useTransition } from "react";
import { Button } from "@/components/ui/button";
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
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

interface StatusEditCardProps {
  applicationId: string;
  currentStatus: ApplicationStatus;
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
 * Status-redigeringskort (ersätter StatusCard). Persistent — ingen
 * disclosure (Klas: inline-expand bröt flödet). Nuvarande status visas EN
 * gång som förankrad StatusPill (detaljhuvud-accent). Radiogruppen
 * innehåller ENDAST tillåtna övergångar (ALLOWED_TRANSITIONS) — ingen låst
 * self-radio (oväljbar affordans). Variant A: en [Spara]-knapp, disabled
 * tills val ≠ nuvarande status. L1: synlig instruktionsrad kopplad via
 * aria-labelledby. L2: destruktiv övergång (Nekad/Återtagen) behåller
 * Dialog-bekräftelsen — inline konsekvenstext är additiv förvarning, ej
 * ersättning. 1 övergång → enskild primär knapp. 0 övergångar → civic-text.
 */
export function StatusEditCard({
  applicationId,
  currentStatus,
}: StatusEditCardProps) {
  const [isPending, startTransition] = useTransition();
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState<ApplicationStatus | "">("");
  const [pendingTarget, setPendingTarget] = useState<ApplicationStatus | null>(
    null
  );

  const transitions = getAllowedTransitions(currentStatus);
  const tone = PILL_TONE[STATUS_BADGE_VARIANT[currentStatus]] ?? "neutral";

  const instructionId = useId();
  const errorId = useId();

  function executeTransition(target: ApplicationStatus) {
    setError(null);
    startTransition(async () => {
      const result = await transitionStatusAction(applicationId, target);
      if (!result.success) {
        setError(result.error);
      } else {
        setSelected("");
      }
      setPendingTarget(null);
    });
  }

  function handleSave(target: ApplicationStatus) {
    if (isDestructiveTransition(target)) {
      setPendingTarget(target);
      return;
    }
    executeTransition(target);
  }

  const currentLabel = getStatusLabel(currentStatus);
  const selectedIsDestructive =
    selected !== "" && isDestructiveTransition(selected);
  const singleTransition = transitions.length === 1 ? transitions[0] : null;

  return (
    <section
      aria-labelledby="status-edit-title"
      className="rounded-md border border-border-default bg-surface-primary"
    >
      <div className="border-b border-border-default px-4 py-3">
        <h2
          id="status-edit-title"
          className="text-h3 font-semibold text-text-primary"
        >
          Status
        </h2>
      </div>

      <div className="flex flex-col gap-4 px-4 py-4">
        <div className="flex items-center gap-2">
          <span className="text-body-sm text-text-secondary">
            Nuvarande status:
          </span>
          <StatusPill tone={tone}>{currentLabel}</StatusPill>
        </div>

        {transitions.length === 0 && (
          <p className="text-body-sm text-text-secondary">
            Den här ansökan är avslutad och kan inte ändras.
          </p>
        )}

        {singleTransition && (
          <div className="flex flex-col gap-3 border-t border-border-default pt-4">
            <p
              id={instructionId}
              className="text-body-sm text-text-secondary"
            >
              Nästa steg för den här ansökan är{" "}
              <span className="font-medium text-text-primary">
                {getStatusLabel(singleTransition)}
              </span>
              .
            </p>
            <div className="flex justify-end">
              <Button
                type="button"
                disabled={isPending}
                onClick={() => handleSave(singleTransition)}
              >
                {isPending
                  ? "Sparar…"
                  : `Markera som ${getStatusLabel(singleTransition)}`}
              </Button>
            </div>
            {error && (
              <p
                id={errorId}
                role="alert"
                className="text-body-sm text-danger-700"
              >
                {error}
              </p>
            )}
          </div>
        )}

        {transitions.length > 1 && (
          <div className="flex flex-col gap-3 border-t border-border-default pt-4">
            <p
              id={instructionId}
              className="text-body-sm text-text-secondary"
            >
              Välj ny status. Nuvarande status är{" "}
              <span className="font-medium text-text-primary">
                {currentLabel}
              </span>
              .
            </p>

            <RadioGroup
              aria-labelledby={instructionId}
              value={selected}
              onValueChange={(v) => {
                setSelected(v as ApplicationStatus);
                setError(null);
              }}
              disabled={isPending}
            >
              {transitions.map((target) => (
                <RadioGroupItem
                  key={target}
                  id={`status-${target}`}
                  value={target}
                >
                  {getStatusLabel(target)}
                </RadioGroupItem>
              ))}
            </RadioGroup>

            {selectedIsDestructive && (
              <p className="text-body-sm text-danger-700">
                {getStatusLabel(selected as ApplicationStatus)} avslutar
                ansökan. Det går inte att ångra utan manuell åtgärd.
              </p>
            )}

            {error && (
              <p
                id={errorId}
                role="alert"
                className="text-body-sm text-danger-700"
              >
                {error}
              </p>
            )}

            <div className="flex justify-end">
              <Button
                type="button"
                disabled={isPending || selected === "" || selected === currentStatus}
                onClick={() =>
                  selected !== "" && handleSave(selected)
                }
              >
                {isPending ? "Sparar…" : "Spara"}
              </Button>
            </div>
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
              Markera som {pendingTarget ? getStatusLabel(pendingTarget) : ""}?
            </DialogTitle>
            <DialogDescription>
              Ansökan ändras från <strong>{currentLabel}</strong> till{" "}
              <strong>
                {pendingTarget ? getStatusLabel(pendingTarget) : ""}
              </strong>
              . Det går inte att ångra utan manuell åtgärd.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button
              type="button"
              variant="ghost"
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

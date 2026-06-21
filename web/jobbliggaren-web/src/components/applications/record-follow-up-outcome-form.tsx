"use client";

import { useActionState, useEffect, useState } from "react";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { useTranslations } from "next-intl";
import {
  recordFollowUpOutcomeAction,
  type ActionResult,
} from "@/lib/actions/applications";
import { followUpOutcomeLabel } from "@/lib/applications/status";
import type { FollowUpOutcome } from "@/lib/types/applications";

interface RecordFollowUpOutcomeFormProps {
  applicationId: string;
  followUpId: string;
  /** Callas efter lyckad spar — driver disclosure-collapse i parent (Prompt 4). */
  onSuccess?: () => void;
  /** Avbryt-knapp som kollapsar disclosure-raden i parent. */
  onCancel?: () => void;
}

/**
 * Sätt utfall på en BEFINTLIG uppföljning. Utfallet är medvetet
 * irreversibelt i domänen — UI:t kommunicerar konsekvensen FÖRE handling
 * (konsekvenstext) och kräver ett explicit bekräftelse-stadium
 * (GOV.UK/Wroblewski check-before-submit) så fel utfall inte sätts
 * oåterkalleligt av misstag.
 */
export function RecordFollowUpOutcomeForm({
  applicationId,
  followUpId,
  onSuccess,
  onCancel,
}: RecordFollowUpOutcomeFormProps) {
  const t = useTranslations("applications.enums");
  const action = recordFollowUpOutcomeAction.bind(
    null,
    applicationId,
    followUpId
  );
  const [state, formAction, isPending] = useActionState<
    ActionResult | null,
    FormData
  >(async (_prev, formData) => action(formData), null);

  const [outcome, setOutcome] = useState<string>("");
  const [confirming, setConfirming] = useState(false);

  useEffect(() => {
    if (state?.success) onSuccess?.();
  }, [state, onSuccess]);

  const selectId = `outcome-${followUpId}`;
  const errorId = `outcome-error-${followUpId}`;
  const noticeId = `outcome-notice-${followUpId}`;
  const hasError = state ? !state.success : false;

  const outcomeLabel = outcome
    ? followUpOutcomeLabel(t, outcome as FollowUpOutcome)
    : "";

  return (
    <form
      action={formAction}
      className="mt-3 flex flex-col gap-3 border-t border-border pt-3"
    >
      <p
        id={noticeId}
        className="text-body-sm text-text-secondary"
      >
        Utfallet kan inte ändras när det har sparats. Kontrollera att det
        stämmer innan du sparar.
      </p>

      <div className="flex flex-col gap-1.5">
        <Label htmlFor={selectId} className="text-body-sm">
          Utfall
        </Label>
        <Select
          name="outcome"
          required
          disabled={isPending}
          value={outcome}
          onValueChange={(v) => {
            setOutcome(v);
            setConfirming(false);
          }}
        >
          <SelectTrigger
            id={selectId}
            className="w-56"
            aria-invalid={hasError}
            aria-describedby={
              hasError ? `${noticeId} ${errorId}` : noticeId
            }
          >
            <SelectValue placeholder="Välj utfall" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="Responded">Svar mottaget</SelectItem>
            <SelectItem value="NoResponse">Inget svar</SelectItem>
          </SelectContent>
        </Select>
      </div>

      {!confirming ? (
        <div className="flex flex-wrap gap-2">
          <Button
            type="button"
            size="sm"
            variant="secondary"
            disabled={isPending || outcome === ""}
            onClick={() => setConfirming(true)}
          >
            Spara utfall
          </Button>
          {onCancel && (
            <Button
              type="button"
              size="sm"
              variant="ghost"
              disabled={isPending}
              onClick={onCancel}
            >
              Avbryt
            </Button>
          )}
        </div>
      ) : (
        <div className="flex flex-col gap-2 rounded-md border border-border bg-surface-secondary px-3 py-3">
          <p className="text-body-sm text-text-primary">
            Spara utfallet{" "}
            <span className="font-medium">{outcomeLabel}</span>? Detta går
            inte att ändra efteråt.
          </p>
          <div className="flex flex-wrap gap-2">
            <Button
              type="submit"
              size="sm"
              variant="secondary"
              disabled={isPending}
            >
              {isPending ? "Sparar…" : `Spara ${outcomeLabel}`}
            </Button>
            <Button
              type="button"
              size="sm"
              variant="ghost"
              disabled={isPending}
              onClick={() => setConfirming(false)}
            >
              Avbryt
            </Button>
          </div>
        </div>
      )}

      {hasError && (
        <p
          id={errorId}
          role="alert"
          className="text-body-sm text-danger-700"
        >
          {state && !state.success ? state.error : null}
        </p>
      )}
    </form>
  );
}

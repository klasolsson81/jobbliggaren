"use client";

import { useState, useTransition } from "react";
import { useFormatter, useTranslations } from "next-intl";
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
import { showApplicationToast } from "@/lib/applications/toast-store";
import { formatDate } from "@/lib/i18n/format";
import type { ApplicationDto } from "@/lib/dto/applications";

export interface FinishDraftDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  application: ApplicationDto;
  /** Ankrad topp-position (nära klicket, §9); null → Radix-centrerad default. */
  top?: number | null;
}

/**
 * "Slutför och skicka"-dialogen (#630 PR 7, design §9): mellansteget när ett
 * UTKAST skickas från listytorna (rad-CTA, kökort — prototyp-facit;
 * detaljmodalens stegväljare/CTA byter direkt). Bekräftelsen = transition till Skickad —
 * `AppliedAt` stämplas write-once server-side och väntetiden räknas från i dag.
 * Den riktiga utkastredigeraren är ett senare spår (handoff §17); dialogens
 * copy lovar därför bara det som faktiskt händer (§5 — fabricera aldrig).
 * Bytet får ångra-toasten som alla transitioner (ADR 0092 D3).
 */
export function FinishDraftDialog({
  open,
  onOpenChange,
  application,
  top,
}: FinishDraftDialogProps) {
  const tUi = useTranslations("applications.ui");
  const format = useFormatter();
  const [error, setError] = useState<string | null>(null);
  const [isPending, startTransition] = useTransition();

  const jobAd = application.jobAd ?? null;
  const title =
    jobAd?.title ??
    tUi("row.fallbackTitle", { shortId: application.id.slice(0, 8) });
  const applyBy = formatDate(format, jobAd?.expiresAt);

  const send = () => {
    setError(null);
    startTransition(async () => {
      const result = await transitionStatusAction(application.id, "Submitted");
      if (result.success) {
        showApplicationToast({
          kind: "statusChange",
          applicationId: application.id,
          company: jobAd?.company ?? `#${application.id.slice(0, 8)}`,
          from: application.status,
          to: "Submitted",
        });
        onOpenChange(false);
      } else {
        setError(result.error);
      }
    });
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        className="w-full max-w-[480px]"
        style={top != null ? { top: `${top}px`, transform: "translateX(-50%)" } : undefined}
      >
        <DialogHeader>
          <DialogTitle>{tUi("finishDraft.title")}</DialogTitle>
          <DialogDescription>{tUi("finishDraft.body")}</DialogDescription>
        </DialogHeader>

        {/* Annons-sammanfattning (§9) — det list-datat faktiskt bär: roll,
            företag, ev. sista ansökningsdag. */}
        <div className="mt-2 rounded-md border border-border bg-surface-secondary p-3">
          <p className="text-body font-semibold text-text-primary">{title}</p>
          {jobAd?.company && (
            <p className="text-body-sm text-text-primary">{jobAd.company}</p>
          )}
          {applyBy && (
            <p className="mt-1 text-body-sm text-text-primary">
              {tUi("finishDraft.applyBy")} <b>{applyBy}</b>
            </p>
          )}
        </div>

        {error != null && (
          <p role="alert" className="text-body-sm text-danger-600">
            {error}
          </p>
        )}

        <DialogFooter className="mt-4">
          <Button
            type="button"
            variant="ghost"
            disabled={isPending}
            onClick={() => onOpenChange(false)}
          >
            {tUi("common.cancel")}
          </Button>
          <Button type="button" disabled={isPending} onClick={send}>
            {isPending ? tUi("common.saving") : tUi("finishDraft.send")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

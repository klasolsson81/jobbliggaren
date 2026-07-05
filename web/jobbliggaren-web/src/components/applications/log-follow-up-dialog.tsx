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
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { logFollowUpAction } from "@/lib/actions/applications";
import { showApplicationToast } from "@/lib/applications/toast-store";

export interface LogFollowUpDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  applicationId: string;
  /** Kontextraden "roll · företag" (design §9). null → radens korta id. */
  contextTitle: string | null;
  contextCompany: string | null;
  /** Visningsnamn i uppföljningstoasten ("{company}: uppföljning sparad …"). */
  toastCompany: string;
  /** Ankrad topp-position (nära klicket, §9); null → Radix-centrerad default. */
  top?: number | null;
  /** Callas efter lyckad spar (utöver stängning) — drawern m.fl. kan haka på. */
  onSuccess?: () => void;
}

/**
 * "Logga uppföljning"-dialogen (#630 PR 7, design §9): loggar en redan UTFÖRD
 * kontakt med dagens datum. Först vid Spara skapas uppföljningsposten +
 * tidslinjeraden och väntetiden räknas om (LastFollowUpAt server-side, ADR 0092
 * D5) — knappen "sväljer" aldrig åtgärden tyst. Uppföljningstoasten har ingen
 * Ångra (design §10).
 *
 * Ingen exempeltext i fältet (Klas-regel): label + hjälptexten bär
 * instruktionen. Noteringen är frivillig — max 2000 tecken (backend-spegel).
 */
export function LogFollowUpDialog({
  open,
  onOpenChange,
  applicationId,
  contextTitle,
  contextCompany,
  toastCompany,
  top,
  onSuccess,
}: LogFollowUpDialogProps) {
  const tUi = useTranslations("applications.ui");
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [isPending, startTransition] = useTransition();

  const context =
    contextTitle != null
      ? contextCompany != null
        ? `${contextTitle} · ${contextCompany}`
        : contextTitle
      : tUi("row.fallbackTitle", { shortId: applicationId.slice(0, 8) });

  const save = () => {
    setError(null);
    startTransition(async () => {
      const result = await logFollowUpAction(applicationId, note);
      if (result.success) {
        showApplicationToast({ kind: "followUpLogged", company: toastCompany });
        onSuccess?.();
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
        // Nära-klick-position (§9): inline-style vinner över klassens
        // top-1/2 + translate — X-centreringen behålls, Y ankras.
        style={top != null ? { top: `${top}px`, transform: "translateX(-50%)" } : undefined}
      >
        <DialogHeader>
          <DialogTitle>{tUi("logFollowUp.title")}</DialogTitle>
          <DialogDescription className="jp-mono text-micro text-text-secondary">
            {context}
          </DialogDescription>
        </DialogHeader>

        <div className="mt-2 flex flex-col gap-1.5">
          <Label htmlFor="log-follow-up-note">{tUi("logFollowUp.noteLabel")}</Label>
          <Textarea
            id="log-follow-up-note"
            value={note}
            onChange={(e) => setNote(e.target.value)}
            rows={4}
            maxLength={2000}
            disabled={isPending}
            aria-describedby="log-follow-up-hint"
          />
          <p id="log-follow-up-hint" className="text-body-sm text-text-primary">
            {tUi("logFollowUp.hint")}
          </p>
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
          <Button type="button" disabled={isPending} onClick={save}>
            {isPending ? tUi("common.saving") : tUi("logFollowUp.save")}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

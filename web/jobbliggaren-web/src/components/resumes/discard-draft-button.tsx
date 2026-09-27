"use client";

// "use client": confirm-dialog-ö för hubbens åtgärdskort (Fas 4b PR-8.3). Kräver
// open-state, useTransition för discard-server-action och en live region för det
// inline-felet — inget av detta kan göras i en Server Component. Speglar
// destructive-confirm-mönstret från DeleteResumeDialog (specifik knapp-text,
// aldrig "Är du säker").

import { useState, useTransition } from "react";
import { useTranslations } from "next-intl";
import { Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { discardParsedResumeAction } from "@/lib/actions/resumes";

interface DiscardDraftButtonProps {
  parsedId: string;
}

/**
 * "Ta bort utkastet" — kastar det inlästa men osparade parsed-CV:t. Öppnar en
 * bekräfta-dialog; på bekräftelse kallas `discardParsedResumeAction` i en
 * transition. Vid framgång revaliderar servern `/cv` (åtgärdskortet försvinner)
 * och dialogen stängs; vid fel visas felet inline i en `role="alert"`-live region
 * och dialogen hålls öppen.
 */
export function DiscardDraftButton({ parsedId }: DiscardDraftButtonProps) {
  const t = useTranslations("pages.cv.pending");
  const [open, setOpen] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [isPending, startTransition] = useTransition();

  function handleConfirm() {
    setError(null);
    startTransition(async () => {
      const result = await discardParsedResumeAction(parsedId);
      if (result.success) {
        setOpen(false);
      } else {
        setError(result.error);
      }
    });
  }

  return (
    <>
      <Button
        type="button"
        variant="ghost"
        onClick={() => setOpen(true)}
      >
        <Trash2 aria-hidden="true" />
        {t("discard")}
      </Button>
      <Dialog
        open={open}
        onOpenChange={(next) => {
          if (isPending) return;
          setOpen(next);
          if (!next) setError(null);
        }}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{t("discardTitle")}</DialogTitle>
            <DialogDescription>{t("discardBody")}</DialogDescription>
          </DialogHeader>
          {error && (
            <p role="alert" className="text-body-sm text-danger-600">
              {error}
            </p>
          )}
          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              onClick={() => setOpen(false)}
              disabled={isPending}
            >
              {t("discardCancel")}
            </Button>
            <Button
              type="button"
              variant="destructive"
              onClick={handleConfirm}
              disabled={isPending}
            >
              {t("discardConfirm")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}

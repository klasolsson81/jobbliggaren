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
import { deleteResumeAction } from "@/lib/actions/resumes";

interface DeleteResumeDialogProps {
  resumeId: string;
  resumeName: string;
  /**
   * Triggerns klasser. Speglar `CvPreview`s levererade prop-mönster, och finns av
   * samma skäl: kontrollen sitter numera i `ResumeCard`s actions-rad bredvid
   * `.jp-btn`-kontroller, och två knappfamiljer i samma rad skiljde 2,3x i
   * kantkontrast (#1373). Default är kvar för varje framtida yta utan `.jp-btn`-grannar.
   */
  triggerClassName?: string;
  /**
   * Tillgängligt namn på triggern. `/cv` renderar ett kort per CV, så utan detta
   * far en skärmläsaranvändare N identiska "Radera CV" i knapp-rotorn utan att
   * kunna skilja dem åt. Mönstret är `criterion-row.tsx`s.
   */
  triggerAriaLabel?: string;
}

export function DeleteResumeDialog({
  resumeId,
  resumeName,
  triggerClassName,
  triggerAriaLabel,
}: DeleteResumeDialogProps) {
  const t = useTranslations("resumes");
  const [open, setOpen] = useState(false);
  const [isPending, startTransition] = useTransition();
  const [error, setError] = useState<string | null>(null);

  function handleConfirm() {
    setError(null);
    startTransition(async () => {
      const result = await deleteResumeAction(resumeId);
      // Sedan #1373 redirectar action:en inte längre — kontrollen sitter på hubben,
      // och `revalidatePath("/cv")` tar bort kortet där användaren står. Stäng
      // dialogen själva vid framgång; håll den öppen med felet vid misslyckande.
      if (result.success) {
        setOpen(false);
      } else {
        setError(result.error);
      }
    });
  }

  return (
    <>
      {triggerClassName ? (
        <button
          type="button"
          className={triggerClassName}
          aria-label={triggerAriaLabel}
          onClick={() => setOpen(true)}
        >
          {t("delete.trigger")}
        </button>
      ) : (
        <Button
          type="button"
          variant="destructive"
          size="sm"
          aria-label={triggerAriaLabel}
          onClick={() => setOpen(true)}
        >
          {t("delete.trigger")}
        </Button>
      )}
      <Dialog open={open} onOpenChange={(o) => { if (!isPending) setOpen(o); }}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{t("delete.title")}</DialogTitle>
            <DialogDescription>
              {t.rich("delete.description", {
                name: () => <strong>{resumeName}</strong>,
              })}
            </DialogDescription>
          </DialogHeader>
          {error && <p className="text-body-sm text-danger-600">{error}</p>}
          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() => setOpen(false)}
              disabled={isPending}
            >
              {t("delete.cancel")}
            </Button>
            <Button
              type="button"
              variant="destructive"
              size="sm"
              onClick={handleConfirm}
              disabled={isPending}
            >
              {t("delete.confirm")}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}

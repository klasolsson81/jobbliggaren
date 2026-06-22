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
}

export function DeleteResumeDialog({
  resumeId,
  resumeName,
}: DeleteResumeDialogProps) {
  const t = useTranslations("resumes");
  const [open, setOpen] = useState(false);
  const [isPending, startTransition] = useTransition();
  const [error, setError] = useState<string | null>(null);

  function handleConfirm() {
    setError(null);
    startTransition(async () => {
      const result = await deleteResumeAction(resumeId);
      // deleteResumeAction redirects on success, so we only get here on failure.
      if (!result.success) {
        setError(result.error);
      }
    });
  }

  return (
    <>
      <Button
        type="button"
        variant="destructive"
        size="sm"
        onClick={() => setOpen(true)}
      >
        {t("delete.trigger")}
      </Button>
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

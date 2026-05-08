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
import { deleteResumeAction } from "@/lib/actions/resumes";

interface DeleteResumeDialogProps {
  resumeId: string;
  resumeName: string;
}

export function DeleteResumeDialog({
  resumeId,
  resumeName,
}: DeleteResumeDialogProps) {
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
        Radera CV
      </Button>
      <Dialog open={open} onOpenChange={(o) => { if (!isPending) setOpen(o); }}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Radera CV?</DialogTitle>
            <DialogDescription>
              Du är på väg att radera <strong>{resumeName}</strong> permanent.
              Det går inte att ångra.
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
              Avbryt
            </Button>
            <Button
              type="button"
              variant="destructive"
              size="sm"
              onClick={handleConfirm}
              disabled={isPending}
            >
              Bekräfta radering
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}

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
import { deleteSavedSearchAction } from "@/lib/actions/saved-searches";

interface DeleteSavedSearchDialogProps {
  savedSearchId: string;
  savedSearchName: string;
}

export function DeleteSavedSearchDialog({
  savedSearchId,
  savedSearchName,
}: DeleteSavedSearchDialogProps) {
  const [open, setOpen] = useState(false);
  const [isPending, startTransition] = useTransition();
  const [error, setError] = useState<string | null>(null);

  function handleConfirm() {
    setError(null);
    startTransition(async () => {
      const result = await deleteSavedSearchAction(savedSearchId);
      // deleteSavedSearchAction revaliderar /sokningar vid lyckad radering;
      // listan ritas om utan den här raden, så vi stänger dialogen.
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
        size="sm"
        onClick={() => setOpen(true)}
      >
        Radera
      </Button>
      <Dialog
        open={open}
        onOpenChange={(o) => {
          if (!isPending) setOpen(o);
        }}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Radera sparad sökning?</DialogTitle>
            <DialogDescription>
              Du är på väg att radera <strong>{savedSearchName}</strong>{" "}
              permanent. Det går inte att ångra.
            </DialogDescription>
          </DialogHeader>
          {error && <p className="text-body-sm text-danger-600">{error}</p>}
          <DialogFooter>
            <Button
              type="button"
              variant="ghost"
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
              {isPending ? "Raderar…" : "Bekräfta radering"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}

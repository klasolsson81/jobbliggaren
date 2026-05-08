"use client";

import { useActionState, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { renameResumeAction, type ActionResult } from "@/lib/actions/resumes";

interface RenameResumeFormProps {
  resumeId: string;
  currentName: string;
}

export function RenameResumeForm({
  resumeId,
  currentName,
}: RenameResumeFormProps) {
  const [open, setOpen] = useState(false);

  const action = renameResumeAction.bind(null, resumeId);
  const [state, formAction, isPending] = useActionState<
    ActionResult | null,
    FormData
  >(async (_prev, formData) => {
    const result = await action(formData);
    if (result.success) setOpen(false);
    return result;
  }, null);

  return (
    <>
      <Button
        type="button"
        variant="outline"
        size="sm"
        onClick={() => setOpen(true)}
      >
        Byt namn
      </Button>
      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Byt namn på CV</DialogTitle>
            <DialogDescription>
              Namnet visas i din CV-lista och i tailored versioner.
            </DialogDescription>
          </DialogHeader>
          <form action={formAction} className="flex flex-col gap-4">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="rename-name">Namn</Label>
              <Input
                id="rename-name"
                name="name"
                defaultValue={currentName}
                required
                maxLength={200}
                disabled={isPending}
              />
            </div>
            {state && !state.success && (
              <p className="text-body-sm text-danger-600">{state.error}</p>
            )}
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
              <Button type="submit" size="sm" disabled={isPending}>
                Spara
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>
    </>
  );
}

"use client";

import { useActionState, useEffect, useRef } from "react";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { Label } from "@/components/ui/label";
import { addNoteAction, type ActionResult } from "@/lib/actions/applications";

interface AddNoteFormProps {
  applicationId: string;
  /** Callas efter lyckad spar — driver disclosure-collapse i parent (Prompt 4). */
  onSuccess?: () => void;
  /** Renderar Avbryt-knapp jämte Submit; collapse-callback från parent. */
  onCancel?: () => void;
}

export function AddNoteForm({
  applicationId,
  onSuccess,
  onCancel,
}: AddNoteFormProps) {
  const tUi = useTranslations("applications.ui");
  const formRef = useRef<HTMLFormElement>(null);

  const action = addNoteAction.bind(null, applicationId);
  const [state, formAction, isPending] = useActionState<ActionResult | null, FormData>(
    async (_prev, formData) => {
      const result = await action(formData);
      if (result.success) formRef.current?.reset();
      return result;
    },
    null
  );

  useEffect(() => {
    if (state?.success) onSuccess?.();
  }, [state, onSuccess]);

  return (
    <form ref={formRef} action={formAction} className="flex flex-col gap-3">
      <div className="flex flex-col gap-1.5">
        <Label htmlFor="note-content">{tUi("addNote.label")}</Label>
        <Textarea
          id="note-content"
          name="content"
          rows={3}
          required
          disabled={isPending}
        />
      </div>
      {state && !state.success && (
        <p className="text-body-sm text-danger-600">{state.error}</p>
      )}
      <div className="flex flex-wrap gap-2">
        <Button type="submit" size="sm" disabled={isPending}>
          {tUi("addNote.submit")}
        </Button>
        {onCancel && (
          <Button
            type="button"
            size="sm"
            variant="ghost"
            disabled={isPending}
            onClick={onCancel}
          >
            {tUi("common.cancel")}
          </Button>
        )}
      </div>
    </form>
  );
}

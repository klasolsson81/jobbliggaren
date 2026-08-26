"use client";

import { useActionState, useEffect, useRef } from "react";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { Label } from "@/components/ui/label";
import {
  addNoteAction,
  type AddNoteActionState,
} from "@/lib/actions/applications";

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
  const errorRef = useRef<HTMLParagraphElement>(null);

  const action = addNoteAction.bind(null, applicationId);
  const [state, formAction, isPending] = useActionState<
    AddNoteActionState | null,
    FormData
  >(async (_prev, formData) => {
    const result = await action(formData);
    if (result.success) formRef.current?.reset();
    return result;
  }, null);

  const failed = state !== null && !state.success;

  useEffect(() => {
    if (state?.success) onSuccess?.();
  }, [state, onSuccess]);

  // The failure names no field — this form has one — and the submit button is disabled while the
  // action runs, so focus falls to <body> and the next Tab restarts at the top of the page. The
  // message is the only target here, so it takes focus itself.
  useEffect(() => {
    if (failed) errorRef.current?.focus();
  }, [failed, state]);

  return (
    <form ref={formRef} action={formAction} className="flex flex-col gap-3">
      <div className="flex flex-col gap-1.5">
        <Label htmlFor="note-content">{tUi("addNote.label")}</Label>
        {/* Re-seeded from the echo a failed save returns. React 19 resets this uncontrolled form
            after every action, and a note can run to several paragraphs — retyping all of it is
            the cost of one failed request otherwise. */}
        <Textarea
          id="note-content"
          name="content"
          rows={3}
          defaultValue={failed ? (state.values?.content ?? "") : ""}
          required
          disabled={isPending}
        />
      </div>
      {failed && (
        <p
          ref={errorRef}
          tabIndex={-1}
          role="alert"
          className="text-body-sm text-danger-600"
        >
          {state.error}
        </p>
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

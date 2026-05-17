"use client";

import { useActionState } from "react";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  recordFollowUpOutcomeAction,
  type ActionResult,
} from "@/lib/actions/applications";

interface RecordFollowUpOutcomeFormProps {
  applicationId: string;
  followUpId: string;
}

export function RecordFollowUpOutcomeForm({
  applicationId,
  followUpId,
}: RecordFollowUpOutcomeFormProps) {
  const action = recordFollowUpOutcomeAction.bind(null, applicationId, followUpId);
  const [state, formAction, isPending] = useActionState<ActionResult | null, FormData>(
    async (_prev, formData) => action(formData),
    null
  );

  const selectId = `outcome-${followUpId}`;
  const errorId = `outcome-error-${followUpId}`;
  const hasError = state ? !state.success : false;

  return (
    <form action={formAction} className="mt-2 flex flex-wrap items-end gap-2">
      <div className="flex flex-col gap-1.5">
        <Label htmlFor={selectId} className="text-body-sm">
          Utfall
        </Label>
        <Select name="outcome" required disabled={isPending}>
          <SelectTrigger
            id={selectId}
            className="w-44"
            aria-invalid={hasError}
            aria-describedby={hasError ? errorId : undefined}
          >
            <SelectValue placeholder="Välj utfall" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="Responded">Svar mottaget</SelectItem>
            <SelectItem value="NoResponse">Inget svar</SelectItem>
          </SelectContent>
        </Select>
      </div>
      <Button type="submit" size="sm" variant="secondary" disabled={isPending}>
        Spara utfall
      </Button>
      {hasError && (
        <p
          id={errorId}
          role="alert"
          className="w-full text-body-sm text-danger-700"
        >
          {state && !state.success ? state.error : null}
        </p>
      )}
    </form>
  );
}

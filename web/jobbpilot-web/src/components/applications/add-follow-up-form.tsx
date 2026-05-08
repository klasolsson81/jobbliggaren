"use client";

import { useActionState, useRef } from "react";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { addFollowUpAction, type ActionResult } from "@/lib/actions/applications";
import { CHANNEL_LABELS } from "@/lib/applications/status";

interface AddFollowUpFormProps {
  applicationId: string;
}

export function AddFollowUpForm({ applicationId }: AddFollowUpFormProps) {
  const formRef = useRef<HTMLFormElement>(null);

  const action = addFollowUpAction.bind(null, applicationId);
  const [state, formAction, isPending] = useActionState<ActionResult | null, FormData>(
    async (_prev, formData) => {
      const result = await action(formData);
      if (result.success) formRef.current?.reset();
      return result;
    },
    null
  );

  return (
    <form ref={formRef} action={formAction} className="flex flex-col gap-3">
      <div className="grid grid-cols-2 gap-3">
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="follow-up-channel">Kanal</Label>
          <Select name="channel" required>
            <SelectTrigger id="follow-up-channel">
              <SelectValue placeholder="Välj kanal" />
            </SelectTrigger>
            <SelectContent>
              {Object.entries(CHANNEL_LABELS).map(([value, label]) => (
                <SelectItem key={value} value={value}>
                  {label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="follow-up-date">Datum</Label>
          <input
            id="follow-up-date"
            name="scheduledAt"
            type="datetime-local"
            required
            disabled={isPending}
            className="flex h-8 w-full rounded-md border border-input bg-transparent px-2.5 py-2 text-sm outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 disabled:opacity-50"
          />
        </div>
      </div>
      <div className="flex flex-col gap-1.5">
        <Label htmlFor="follow-up-note">Anteckning (valfritt)</Label>
        <Textarea
          id="follow-up-note"
          name="note"
          placeholder="Vad diskuterades?"
          rows={2}
          disabled={isPending}
        />
      </div>
      {state && !state.success && (
        <p className="text-body-sm text-danger-600">{state.error}</p>
      )}
      <Button type="submit" size="sm" disabled={isPending}>
        Lägg till uppföljning
      </Button>
    </form>
  );
}

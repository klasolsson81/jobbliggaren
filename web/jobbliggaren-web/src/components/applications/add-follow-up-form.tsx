"use client";

import { useActionState, useEffect, useRef, useState } from "react";
import { useTranslations } from "next-intl";

/**
 * Lokal datetime-string i `datetime-local`-input-format (YYYY-MM-DDTHH:mm,
 * lokal tid, ingen Z). Beräknas EN gång på mount för att fylla "Datum"-
 * fältet med nu-tid som default (Klas-UX 2026-05-20: sparar tid eftersom
 * uppföljningar oftast schemaläggs nära skapandetidpunkten). Användaren
 * kan fritt ändra; värdet är ej kontrollerat (defaultValue), så reset
 * efter lyckad submit återställer till mount-tiden — lätt inaktuell vid
 * multi-add i samma session, acceptabelt YAGNI.
 */
function localDatetimeNow(): string {
  const d = new Date();
  const p = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}T${p(d.getHours())}:${p(d.getMinutes())}`;
}
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
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
import { CHANNEL_KEYS, channelLabel } from "@/lib/applications/status";

interface AddFollowUpFormProps {
  applicationId: string;
  /** Callas efter lyckad spar — driver disclosure-collapse i parent (Prompt 4). */
  onSuccess?: () => void;
  /** Renderar Avbryt-knapp jämte Submit; collapse-callback från parent. */
  onCancel?: () => void;
}

export function AddFollowUpForm({
  applicationId,
  onSuccess,
  onCancel,
}: AddFollowUpFormProps) {
  const t = useTranslations("applications.enums");
  const formRef = useRef<HTMLFormElement>(null);
  const [defaultScheduledAt] = useState(localDatetimeNow);

  const action = addFollowUpAction.bind(null, applicationId);
  const [state, formAction, isPending] = useActionState<ActionResult | null, FormData>(
    async (_prev, formData) => {
      const result = await action(formData);
      if (result.success) formRef.current?.reset();
      return result;
    },
    null
  );

  // Trigger:a onSuccess EFTER render (useActionState-state-ändring). useEffect
  // ger parent kontroll över collapse-timing utan att form-internals behöver
  // veta något om disclosure-mönstret.
  useEffect(() => {
    if (state?.success) {
      onSuccess?.();
    }
  }, [state, onSuccess]);

  return (
    <form ref={formRef} action={formAction} className="flex flex-col gap-3">
      <div className="grid grid-cols-2 gap-3">
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="follow-up-channel">Kanal</Label>
          <Select name="channel" required disabled={isPending}>
            <SelectTrigger id="follow-up-channel" className="w-full">
              <SelectValue placeholder="Välj kanal" />
            </SelectTrigger>
            <SelectContent>
              {CHANNEL_KEYS.map((value) => (
                <SelectItem key={value} value={value}>
                  {channelLabel(t, value)}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="follow-up-date">Datum</Label>
          <Input
            id="follow-up-date"
            name="scheduledAt"
            type="datetime-local"
            defaultValue={defaultScheduledAt}
            required
            disabled={isPending}
          />
        </div>
      </div>
      <div className="flex flex-col gap-1.5">
        <Label htmlFor="follow-up-note">Anteckning (valfritt)</Label>
        <Textarea
          id="follow-up-note"
          name="note"
          rows={2}
          aria-describedby="follow-up-note-hint"
          disabled={isPending}
        />
        <p
          id="follow-up-note-hint"
          className="text-body-sm text-text-secondary"
        >
          Till exempel vad som diskuterades.
        </p>
      </div>
      {state && !state.success && (
        <p role="alert" className="text-body-sm text-danger-700">
          {state.error}
        </p>
      )}
      <div className="flex flex-wrap gap-2">
        <Button type="submit" size="sm" disabled={isPending}>
          Lägg till uppföljning
        </Button>
        {onCancel && (
          <Button
            type="button"
            size="sm"
            variant="ghost"
            disabled={isPending}
            onClick={onCancel}
          >
            Avbryt
          </Button>
        )}
      </div>
    </form>
  );
}

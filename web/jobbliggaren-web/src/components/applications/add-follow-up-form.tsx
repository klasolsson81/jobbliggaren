"use client";

import { useEffect, useMemo, useRef, useState, useTransition } from "react";
import { Controller, useForm } from "react-hook-form";
import { useTranslations } from "next-intl";

/**
 * Lokal datetime-string i `datetime-local`-input-format (YYYY-MM-DDTHH:mm,
 * lokal tid, ingen Z). Fyller "Datum"-fältet med nu-tid som default
 * (Klas-UX 2026-05-20: sparar tid eftersom uppföljningar oftast schemaläggs
 * nära skapandetidpunkten). Användaren kan fritt ändra; värdet ägs av React
 * Hook Form och räknas om vid varje lyckad spar, så nästa uppföljning i samma
 * session öppnar på den nya nu-tiden.
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
import { addFollowUpAction } from "@/lib/actions/applications";
import { makeAddFollowUpSchema } from "@/lib/actions/application-schemas";
import { CHANNEL_KEYS, channelLabel } from "@/lib/applications/status";

interface AddFollowUpFormProps {
  applicationId: string;
  /** Callas efter lyckad spar — driver disclosure-collapse i parent (Prompt 4). */
  onSuccess?: () => void;
  /** Renderar Avbryt-knapp jämte Submit; collapse-callback från parent. */
  onCancel?: () => void;
}

type FormValues = {
  channel: string;
  scheduledAt: string;
  note: string;
};

/**
 * React Hook Form owns all three values, and that ownership is the point.
 *
 * As an uncontrolled `<form action={serverAction}>` this form lost every field to a failed save:
 * React 19 resets such a form after EVERY action, so the note went, the chosen date rewound to the
 * form's mount-time "now" (a wrong value, not an empty one) and the Radix Select dropped back to its
 * placeholder — measured in the browser, error-surface matrix RP-27 (2026-08-24).
 *
 * Submitting through `handleSubmit` instead of a form action removes that reset entirely: nothing
 * calls `form.reset()`, so nothing clears the DOM and nothing fires the reset event Radix listens
 * for. The values survive a failure by construction rather than by being handed back. The form is
 * cleared exactly once, deliberately, on a successful save.
 *
 * Client validation mirrors the server's own schema (`makeAddFollowUpSchema`, the same builder
 * `addFollowUpAction` runs) and the server stays authoritative — the shape `cv-gapfill-form` uses.
 */
export function AddFollowUpForm({
  applicationId,
  onSuccess,
  onCancel,
}: AddFollowUpFormProps) {
  const t = useTranslations("applications.enums");
  const tUi = useTranslations("applications.ui");
  const tValidation = useTranslations("validation");
  // The action's own schema, minus the one member that is not a field of this form: applicationId
  // arrives as a prop, so a client-side complaint about it would name something the user cannot
  // change. The action still parses the full object, applicationId included — it stays the
  // authority, and this is only the round trip saved.
  const schema = useMemo(
    () => makeAddFollowUpSchema(tValidation).omit({ applicationId: true }),
    [tValidation]
  );

  const errorRef = useRef<HTMLParagraphElement>(null);
  const [isPending, startTransition] = useTransition();
  // An object rather than a bare string so two identical failures in a row are two distinct states
  // — the focus effect below has to fire on the second one too.
  const [error, setError] = useState<{ message: string } | null>(null);

  const { register, control, handleSubmit, reset } = useForm<FormValues>({
    defaultValues: { channel: "", scheduledAt: localDatetimeNow(), note: "" },
  });

  // The failure names no field, and the submit button is disabled while the action runs, so focus
  // would otherwise fall to <body> and the next Tab restart at the top of the page. The message is
  // the only honest target here.
  useEffect(() => {
    if (error) errorRef.current?.focus();
  }, [error]);

  function onSubmit(values: FormValues) {
    setError(null);
    const parsed = schema.safeParse({
      channel: values.channel,
      scheduledAt: values.scheduledAt,
      note: values.note || undefined,
    });
    if (!parsed.success) {
      setError({
        message: parsed.error.issues[0]?.message ?? tUi("actions.invalidInput"),
      });
      return;
    }

    startTransition(async () => {
      const formData = new FormData();
      formData.set("channel", values.channel);
      formData.set("scheduledAt", values.scheduledAt);
      formData.set("note", values.note);
      const result = await addFollowUpAction(applicationId, formData);
      if (!result.success) {
        setError({ message: result.error });
        return;
      }
      // The only place this form is ever cleared. `localDatetimeNow()` is re-read here so the next
      // follow-up in the same session opens on the current time, not on the mount time.
      reset({ channel: "", scheduledAt: localDatetimeNow(), note: "" });
      onSuccess?.();
    });
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="flex flex-col gap-3">
      <div className="grid grid-cols-2 gap-3">
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="follow-up-channel">{tUi("addFollowUp.channelLabel")}</Label>
          {/* Controller, not `register`: Radix Select is not a native control and posts nothing on
              its own. With the value held here it also cannot be cleared behind the form's back. */}
          <Controller
            control={control}
            name="channel"
            render={({ field }) => (
              <Select
                name={field.name}
                value={field.value}
                onValueChange={field.onChange}
                required
                disabled={isPending}
              >
                <SelectTrigger
                  id="follow-up-channel"
                  className="w-full"
                  ref={field.ref}
                  onBlur={field.onBlur}
                >
                  <SelectValue placeholder={tUi("addFollowUp.channelPlaceholder")} />
                </SelectTrigger>
                <SelectContent>
                  {CHANNEL_KEYS.map((value) => (
                    <SelectItem key={value} value={value}>
                      {channelLabel(t, value)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )}
          />
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="follow-up-date">{tUi("addFollowUp.dateLabel")}</Label>
          <Input
            id="follow-up-date"
            type="datetime-local"
            required
            disabled={isPending}
            {...register("scheduledAt")}
          />
        </div>
      </div>
      <div className="flex flex-col gap-1.5">
        <Label htmlFor="follow-up-note">{tUi("addFollowUp.noteLabel")}</Label>
        <Textarea
          id="follow-up-note"
          rows={2}
          aria-describedby="follow-up-note-hint"
          disabled={isPending}
          {...register("note")}
        />
        <p
          id="follow-up-note-hint"
          className="text-body-sm text-text-primary"
        >
          {tUi("addFollowUp.noteHint")}
        </p>
      </div>
      {error && (
        <p
          ref={errorRef}
          tabIndex={-1}
          role="alert"
          className="text-body-sm text-danger-700"
        >
          {error.message}
        </p>
      )}
      <div className="flex flex-wrap gap-2">
        <Button type="submit" size="sm" disabled={isPending}>
          {tUi("addFollowUp.submit")}
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

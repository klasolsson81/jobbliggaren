"use client";

import {
  useEffect,
  useId,
  useMemo,
  useRef,
  useState,
  useTransition,
} from "react";
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

// Maps a zod issue path back to the control that owns it, so a refusal can mark and focus the
// input it names. Keyed by form field, so a renamed field breaks the build rather than the routing.
const FIELD_ELEMENT_IDS: Record<keyof FormValues, string> = {
  channel: "follow-up-channel",
  scheduledAt: "follow-up-date",
  note: "follow-up-note",
};

/**
 * React Hook Form owns all three values, and that ownership is the point.
 *
 * As an uncontrolled `<form action={serverAction}>` this form lost every field to a failed save:
 * React 19 resets such a form after EVERY action, so the note went, the chosen date rewound to the
 * form's mount-time "now" (a wrong value, not an empty one) and the Radix Select dropped back to its
 * placeholder — measured in the browser, RP-27, 2026-08-24, PR #1512.
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

  const errorId = useId();
  const errorRef = useRef<HTMLParagraphElement>(null);
  const [isPending, startTransition] = useTransition();
  // An object rather than a bare string so two identical failures in a row are two distinct states
  // — the focus effect below has to fire on the second one too. `field` carries #1117's
  // discriminator: the name of the ONE control a client-side refusal belongs to, absent for a
  // server failure that belongs to no field.
  const [error, setError] = useState<{
    message: string;
    field?: keyof FormValues;
  } | null>(null);

  const { register, control, handleSubmit, reset } = useForm<FormValues>({
    defaultValues: { channel: "", scheduledAt: localDatetimeNow(), note: "" },
  });

  function fieldA11y(name: keyof FormValues) {
    return error?.field === name
      ? ({ "aria-invalid": true, "aria-describedby": errorId } as const)
      : {};
  }

  // A refusal that names a control sends the caret there — it is what the user has to change. One
  // that names none has no field to go to, and the submit button is disabled while the action runs,
  // so focus would otherwise fall to <body> and the next Tab restart at the top of the page.
  useEffect(() => {
    if (!error) return;
    if (error.field) {
      document.getElementById(FIELD_ELEMENT_IDS[error.field])?.focus();
      return;
    }
    errorRef.current?.focus();
  }, [error]);

  function onSubmit(values: FormValues) {
    setError(null);
    const parsed = schema.safeParse({
      channel: values.channel,
      scheduledAt: values.scheduledAt,
      note: values.note || undefined,
    });
    if (!parsed.success) {
      const issue = parsed.error.issues[0];
      const path = issue?.path[0];
      setError({
        message: issue?.message ?? tUi("actions.invalidInput"),
        field: typeof path === "string" && path in FIELD_ELEMENT_IDS
          ? (path as keyof FormValues)
          : undefined,
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
              its own. With the value held here it also cannot be cleared behind the form's back.

              NO `required` here, deliberately. Radix puts it on the visually hidden native select
              it renders for form participation, and a browser probe of that arrangement
              (2026-08-24, PR #1512) measured what the user actually got: nativeRequired true,
              nativeValidity false, the message "Please select an item in the list." in the
              BROWSER's locale rather than Swedish, and focus moved to the hidden select. The zod
              refusal below never ran. Dropping it makes that refusal the reachable gate, in
              Swedish, bound to the visible trigger. The date and note inputs keep theirs — they
              are real, visible controls whose native bubbles anchor correctly. */}
          <Controller
            control={control}
            name="channel"
            render={({ field }) => (
              <Select
                name={field.name}
                value={field.value}
                onValueChange={field.onChange}
                disabled={isPending}
              >
                <SelectTrigger
                  id="follow-up-channel"
                  className="w-full"
                  ref={field.ref}
                  onBlur={field.onBlur}
                  {...fieldA11y("channel")}
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
            {...fieldA11y("scheduledAt")}
            {...register("scheduledAt")}
          />
        </div>
      </div>
      <div className="flex flex-col gap-1.5">
        <Label htmlFor="follow-up-note">{tUi("addFollowUp.noteLabel")}</Label>
        {/* The hint is kept alongside the error rather than replaced by it — the length cap it
            states is exactly what the refusal is about. */}
        <Textarea
          id="follow-up-note"
          rows={2}
          aria-invalid={error?.field === "note" ? true : undefined}
          aria-describedby={
            error?.field === "note"
              ? `follow-up-note-hint ${errorId}`
              : "follow-up-note-hint"
          }
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
          id={errorId}
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

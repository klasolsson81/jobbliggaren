"use client";

import { useEffect, useMemo, useRef, useState, useTransition } from "react";
import { Controller, useForm, type FieldErrors } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
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
import {
  makeAddFollowUpSchema,
  type ValidationTranslator,
} from "@/lib/actions/application-schemas";
import { CHANNEL_KEYS, channelLabel } from "@/lib/applications/status";

interface AddFollowUpFormProps {
  applicationId: string;
  /** Callas efter lyckad spar — driver disclosure-collapse i parent (Prompt 4). */
  onSuccess?: () => void;
  /** Renderar Avbryt-knapp jämte Submit; collapse-callback från parent. */
  onCancel?: () => void;
}

// The schema the client mirrors, minus the one member that is not a field of this form:
// applicationId arrives as a prop, so a client-side complaint about it would name something the
// user cannot change. The action still parses the full object, applicationId included — it stays
// the authority, and this is only the round trip saved.
function makeFormSchema(t: ValidationTranslator) {
  return makeAddFollowUpSchema(t).omit({ applicationId: true });
}

// The form's values are the schema's INPUT shape, derived rather than restated. `z.infer` would
// give the output shape; a form holds what the user picked and typed, which is the input side.
// `channel` is therefore the enum, and "nothing picked yet" is `undefined` rather than the empty
// string that used to stand in for it — the Select maps it back to "" at its own boundary below.
type FormValues = z.input<ReturnType<typeof makeFormSchema>>;

// Maps a form field to the control that owns it, so a refusal can mark the input it names and
// address that input's own message node. Keyed by form field, so a renamed field breaks the build
// rather than the routing.
const FIELD_ELEMENT_IDS: Record<keyof FormValues, string> = {
  channel: "follow-up-channel",
  scheduledAt: "follow-up-date",
  note: "follow-up-note",
};

// The note's hint states the length cap its refusal is about, so a refused note is described by the
// hint AND its own message rather than by the message alone.
const NOTE_HINT_ID = "follow-up-note-hint";

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
 * <b>React Hook Form owns the REFUSALS too, through the schema resolver.</b>
 *
 * Client validation used to run as a hand-rolled `schema.safeParse` whose result was kept in a
 * `useState` beside the form. That split ownership — values in RHF, errors next to it — produced
 * two defects at once (#1514): only `issues[0]` was ever surfaced, so an unpicked channel AND an
 * over-long note took two submits, and nothing cleared the refusal while the user typed, so a
 * corrected field kept `aria-invalid="true"`. Handing the same schema to `zodResolver` removes both
 * by construction rather than by wiring: every issue lands on the field it names, and
 * `reValidateMode: "onChange"` drops a field's error the moment it becomes valid again.
 *
 * The schema is `makeAddFollowUpSchema`, the same builder `addFollowUpAction` runs, and the server
 * stays authoritative — this is only the round trip saved.
 */
export function AddFollowUpForm({
  applicationId,
  onSuccess,
  onCancel,
}: AddFollowUpFormProps) {
  const t = useTranslations("applications.enums");
  const tUi = useTranslations("applications.ui");
  const tValidation = useTranslations("validation");
  const schema = useMemo(() => makeFormSchema(tValidation), [tValidation]);

  const errorRef = useRef<HTMLParagraphElement>(null);
  const [isPending, startTransition] = useTransition();
  // The fields the last submit refused. `errors` alone cannot gate the display: with a resolver
  // RHF re-validates on every keystroke once a submit has failed, and a field the user was never
  // refused on would start marking itself mid-word.
  const [refused, setRefused] = useState<ReadonlySet<keyof FormValues>>(
    new Set()
  );

  const {
    register,
    control,
    handleSubmit,
    reset,
    setError,
    formState: { errors },
  } = useForm<FormValues>({
    // `raw: true` keeps `handleSubmit`'s argument the schema's INPUT shape, which is what
    // `FormValues` above declares it to be. Without it the resolver hands back parsed OUTPUT at
    // runtime while the type still says input.
    resolver: zodResolver(schema, undefined, { raw: true }),
    // Refuse on submit, then re-check a refused field on every change. The second half is what
    // clears a corrected field's `aria-invalid` while the user is still typing (#1514).
    mode: "onSubmit",
    reValidateMode: "onChange",
    shouldFocusError: false,
    defaultValues: { scheduledAt: localDatetimeNow(), note: "" },
  });

  function isRefused(name: keyof FormValues) {
    return errors[name] !== undefined && refused.has(name);
  }

  function fieldA11y(name: keyof FormValues, hintId?: string) {
    const invalid = isRefused(name);
    const describedBy = [
      hintId,
      invalid ? `${FIELD_ELEMENT_IDS[name]}-error` : undefined,
    ].filter((id) => id !== undefined);
    return {
      "aria-invalid": invalid || undefined,
      "aria-describedby":
        describedBy.length > 0 ? describedBy.join(" ") : undefined,
    };
  }

  // Every refused field carries its OWN message node under its own stable id. One shared node would
  // make each invalid control's `aria-describedby` point at every other refused field's message as
  // well — the error would be identified but misattributed, which is not what WCAG 3.3.1 asks for.
  function fieldError(name: keyof FormValues) {
    const message = isRefused(name) ? errors[name]?.message : undefined;
    if (message === undefined) return null;
    return (
      <p
        id={`${FIELD_ELEMENT_IDS[name]}-error`}
        role="alert"
        className="text-body-sm text-danger-700"
      >
        {message}
      </p>
    );
  }

  // A server failure names no control, so it has no field to go to, and the submit button is
  // disabled while the action runs — focus would otherwise fall to <body> and the next Tab restart
  // at the top of the page.
  useEffect(() => {
    if (errors.root === undefined) return;
    errorRef.current?.focus();
  }, [errors.root]);

  // Runs on a refused submit, and nowhere else. It records which fields were refused and sends the
  // caret to the first of them IN THE ORDER THIS FORM DECLARES ITS FIELDS, which is the order they
  // appear on screen.
  //
  // RHF's own `shouldFocusError` is off rather than unused. It walks its internal registration
  // order, and with the channel and the note both refused it focused the NOTE (measured
  // 2026-08-27), dropping the user past the refusal above it.
  //
  // `focusVisible` is explicit because a programmatic `.focus()` after a MOUSE click leaves
  // `:focus-visible` false on a <button>, and the app's focus ring is drawn by that selector alone.
  // The Radix trigger IS a <button>.
  function onRefused(fieldErrors: FieldErrors<FormValues>) {
    const names = Object.keys(FIELD_ELEMENT_IDS) as (keyof FormValues)[];
    const refusedNow = names.filter((name) => fieldErrors[name] !== undefined);
    setRefused(new Set(refusedNow));
    const first = refusedNow[0];
    if (first === undefined) return;
    document
      .getElementById(FIELD_ELEMENT_IDS[first])
      ?.focus({ focusVisible: true });
  }

  function onSubmit(values: FormValues) {
    setRefused(new Set());
    startTransition(async () => {
      const formData = new FormData();
      formData.set("channel", values.channel);
      formData.set("scheduledAt", values.scheduledAt);
      // `note` is `string | undefined` on the schema's input side. RHF holds it as the empty string
      // `defaultValues` seeds, so `?? ""` never fires at runtime — it is there because
      // `formData.set(k, undefined)` would post the literal string "undefined".
      formData.set("note", values.note ?? "");
      const result = await addFollowUpAction(applicationId, formData);
      if (!result.success) {
        setError("root", { message: result.error });
        return;
      }
      // The only place this form is ever cleared. `localDatetimeNow()` is re-read here so the next
      // follow-up in the same session opens on the current time, not on the mount time, and the
      // channel goes back to "nothing picked".
      reset({ channel: undefined, scheduledAt: localDatetimeNow(), note: "" });
      onSuccess?.();
    });
  }

  return (
    <form
      onSubmit={handleSubmit(onSubmit, onRefused)}
      className="flex flex-col gap-3"
    >
      <div className="grid grid-cols-2 gap-3">
        <div className="flex flex-col gap-1.5">
          <Label htmlFor={FIELD_ELEMENT_IDS.channel}>
            {tUi("addFollowUp.channelLabel")}{" "}
            <span aria-hidden="true" className="text-danger-600">
              *
            </span>
          </Label>
          {/* Controller, not `register`: Radix Select is not a native control and posts nothing on
              its own. With the value held here it also cannot be cleared behind the form's back.

              NO `required` here, deliberately. Radix puts it on the visually hidden native select
              it renders for form participation, and a browser probe of that arrangement
              (2026-08-24, PR #1512) measured what the user actually got: nativeRequired true,
              nativeValidity false, the message "Please select an item in the list." in the
              BROWSER's locale rather than Swedish, and focus moved to the hidden select. The zod
              refusal below never ran. Dropping it makes that refusal the reachable gate, in
              Swedish, bound to the visible trigger. The date input keeps its required — it is
              a real, visible control whose native bubble anchors correctly. */}
          <Controller
            control={control}
            name="channel"
            render={({ field }) => (
              <Select
                name={field.name}
                // "Nothing picked yet" is `undefined` in the form's values, because that is what
                // the schema's input side calls it. Radix spells the same state as the empty
                // string — no item may carry it, so it renders the placeholder — and this is the
                // one boundary where the two spellings meet.
                value={field.value ?? ""}
                onValueChange={field.onChange}
                disabled={isPending}
              >
                <SelectTrigger
                  id={FIELD_ELEMENT_IDS.channel}
                  className="w-full"
                  ref={field.ref}
                  onBlur={field.onBlur}
                  aria-required="true"
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
          {fieldError("channel")}
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor={FIELD_ELEMENT_IDS.scheduledAt}>
            {tUi("addFollowUp.dateLabel")}{" "}
            <span aria-hidden="true" className="text-danger-600">
              *
            </span>
          </Label>
          <Input
            id={FIELD_ELEMENT_IDS.scheduledAt}
            type="datetime-local"
            required
            aria-required="true"
            disabled={isPending}
            {...fieldA11y("scheduledAt")}
            {...register("scheduledAt")}
          />
          {fieldError("scheduledAt")}
        </div>
      </div>
      <div className="flex flex-col gap-1.5">
        <Label htmlFor={FIELD_ELEMENT_IDS.note}>{tUi("addFollowUp.noteLabel")}</Label>
        {/* The hint is kept alongside the message rather than replaced by it — the length cap it
            states is exactly what the refusal is about. */}
        <Textarea
          id={FIELD_ELEMENT_IDS.note}
          rows={2}
          disabled={isPending}
          {...fieldA11y("note", NOTE_HINT_ID)}
          {...register("note")}
        />
        <p
          id={NOTE_HINT_ID}
          className="text-body-sm text-text-primary"
        >
          {tUi("addFollowUp.noteHint")}
        </p>
        {fieldError("note")}
      </div>
      {errors.root && (
        <p
          ref={errorRef}
          tabIndex={-1}
          role="alert"
          className="text-body-sm text-danger-700"
        >
          {errors.root.message ?? tUi("actions.invalidInput")}
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

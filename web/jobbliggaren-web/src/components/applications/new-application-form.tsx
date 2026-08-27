"use client";

import Link from "next/link";
import { useEffect, useMemo, useRef, useState, useTransition } from "react";
import { useForm, type FieldErrors } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Label } from "@/components/ui/label";
import { createApplicationAction } from "@/lib/actions/applications";
import { makeCreateApplicationSchema } from "@/lib/actions/application-schemas";

// The form's values are the schema's INPUT shape, derived rather than restated. `z.infer` would
// give the output shape, in which an empty optional field has already been transformed away to
// `undefined`; a form holds what the user typed, which is the input side. Deriving it also means a
// field added to the schema fails this file's build until it is named in `FIELD_ELEMENT_IDS`.
type FormValues = z.input<ReturnType<typeof makeCreateApplicationSchema>>;

// Maps a form field to the control that owns it, so a refusal can mark the input it names and
// address that input's own message node. Keyed by form field, so a renamed field breaks the build
// rather than the routing. The JSX derives every `id`/`htmlFor` from this map for the same reason.
const FIELD_ELEMENT_IDS: Record<keyof FormValues, string> = {
  title: "title",
  company: "company",
  url: "url",
  expiresAt: "expiresAt",
  coverLetter: "cover-letter",
};

// Hint paragraphs the three optional fields are described by. A field in error is described by its
// hint AND its own message, not the message alone: each hint states the constraint the refusal is
// about.
const URL_HINT_ID = "url-hint";
const EXPIRES_HINT_ID = "expires-hint";
const COVER_LETTER_HINT_ID = "cover-letter-hint";

/**
 * The manual create-application form, extracted from `/ny-ansokan/page.tsx`.
 *
 * It is `"use client"` because React Hook Form and `useTransition` are, and that is why it is a
 * component rather than the page: Next refuses `generateMetadata` from a client module, so a page
 * that IS this form cannot carry a document title (WCAG 2.4.2). The heading stays on the page,
 * which renders it server-side; only the form ships as client JS.
 *
 * <b>React Hook Form owns all five values, and that ownership is the point.</b>
 *
 * As an uncontrolled `<form action={formAction}>` driven by `useActionState`, one failed save took
 * every field with it: React 19 resets such a form after EVERY action, and this form had no
 * `defaultValue` and no echo-back to re-seed from. Job title, company, link, deadline and a cover
 * letter of up to 5000 characters all went, and focus fell to `<body>` — the worst
 * input-destruction surface in the app (error-surface matrix rank 1, RP-26).
 *
 * Submitting through `handleSubmit` instead of a form action removes that reset entirely: nothing
 * calls `form.reset()`, so nothing clears the DOM. The values survive a failure by construction
 * rather than by being handed back, which is why `createApplicationAction` grew no echo of the
 * submitted values — it would be dead weight (the same conclusion PR #1512 reached).
 *
 * <b>React Hook Form owns the REFUSALS too, through the schema resolver.</b>
 *
 * Client validation used to run as a hand-rolled `schema.safeParse` whose result was kept in a
 * `useState` beside the form. That split ownership — values in RHF, errors next to it — produced
 * two defects at once (#1514): only `issues[0]` was ever surfaced, so a bad link AND an over-long
 * cover letter took two submits, and nothing cleared the refusal while the user typed, so a
 * corrected field kept `aria-invalid="true"`. Handing the same schema to `zodResolver` removes
 * both by construction rather than by wiring: every issue lands on the field it names, and
 * `reValidateMode: "onChange"` drops a field's error the moment it becomes valid again.
 *
 * The schema is `makeCreateApplicationSchema`, the same builder `createApplicationAction` runs, and
 * the server stays authoritative — this is only the round trip saved.
 */
export function NewApplicationForm() {
  const t = useTranslations("pages");
  const tUi = useTranslations("applications.ui");
  const tValidation = useTranslations("validation");
  // The action's own schema, unmodified: every member of it is a field of this form.
  const schema = useMemo(
    () => makeCreateApplicationSchema(tValidation),
    [tValidation]
  );

  const errorRef = useRef<HTMLParagraphElement>(null);
  const [isPending, startTransition] = useTransition();
  // The fields the last submit refused. `errors` alone cannot gate the display: with a resolver
  // RHF re-validates EVERY field on each keystroke once a submit has failed, so a field the user
  // was never refused on would start marking itself mid-word.
  const [refused, setRefused] = useState<ReadonlySet<keyof FormValues>>(
    new Set()
  );

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<FormValues>({
    // `raw: true` keeps `handleSubmit`'s argument the schema's INPUT shape, which is what
    // `FormValues` above declares it to be. Without it the resolver hands back parsed OUTPUT at
    // runtime while the type still says input.
    resolver: zodResolver(schema, undefined, { raw: true }),
    // Refuse on submit, then re-check a refused field on every keystroke. The second half is what
    // clears a corrected field's `aria-invalid` while the user is still typing (#1514).
    mode: "onSubmit",
    reValidateMode: "onChange",
    shouldFocusError: false,
    defaultValues: {
      title: "",
      company: "",
      url: "",
      expiresAt: "",
      coverLetter: "",
    },
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
  // appear on screen. This form's registration order happens to agree, but `add-follow-up-form` —
  // the other half of #1514 — has a `Controller` whose registration lands out of document order,
  // and the two surfaces carry ONE shape deliberately.
  //
  // `focusVisible` is explicit because a programmatic `.focus()` after a MOUSE click leaves
  // `:focus-visible` false on a <button>, and the app's focus ring is drawn by that selector alone.
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
      formData.set("title", values.title);
      formData.set("company", values.company);
      // The three optional members are `string | undefined` on the schema's input side. RHF holds
      // them as the empty strings `defaultValues` seeds, so `?? ""` never fires at runtime — it is
      // there because `formData.set(k, undefined)` would post the literal string "undefined", and
      // the type is the only thing that can rule that out.
      formData.set("url", values.url ?? "");
      formData.set("expiresAt", values.expiresAt ?? "");
      formData.set("coverLetter", values.coverLetter ?? "");
      // A successful create never returns here. The action ends in `redirect()`, and Next REJECTS
      // the action promise with that redirect so its own router performs the navigation
      // (`server-action-reducer`, Next 16.3.0, measured 2026-08-25). That rejection is a success
      // signal, not a fault, and is left to propagate — the shape `cv-gapfill-form` already uses.
      // Only a RETURNED failure is an error, and it is the only thing handled below. Nothing clears
      // the form on the way out: the page navigates away, so there is no state left to reset.
      //
      // `null` is the retired `useActionState` previous-state argument. The action keeps its
      // two-parameter shape — it is also called that way by its own payload-contract test — and
      // this form is its only other caller.
      const result = await createApplicationAction(null, formData);
      if (!result.success) {
        setError("root", { message: result.error });
      }
    });
  }

  return (
    <form
      onSubmit={handleSubmit(onSubmit, onRefused)}
      className="flex max-w-lg flex-col gap-5"
    >
      <div className="flex flex-col gap-1.5">
        <Label htmlFor={FIELD_ELEMENT_IDS.title}>
          {t("ansokningar.new.titleLabel")}{" "}
          <span aria-hidden="true" className="text-danger-600">
            *
          </span>
        </Label>
        {/* `required` stays on this real, visible control: its native bubble anchors to the input
            the user must fix. That does make the schema's own "Jobbtitel krävs." unreachable in a
            browser, which is the same trade the form already made and is deliberate — the native
            gate is the earlier and better-placed one here. (Contrast PR #1512's Radix Select, whose
            `required` landed on a hidden element and had to go.) */}
        <Input
          id={FIELD_ELEMENT_IDS.title}
          required
          aria-required="true"
          disabled={isPending}
          {...fieldA11y("title")}
          {...register("title")}
        />
        {fieldError("title")}
      </div>

      <div className="flex flex-col gap-1.5">
        <Label htmlFor={FIELD_ELEMENT_IDS.company}>
          {t("ansokningar.new.companyLabel")}{" "}
          <span aria-hidden="true" className="text-danger-600">
            *
          </span>
        </Label>
        <Input
          id={FIELD_ELEMENT_IDS.company}
          required
          aria-required="true"
          disabled={isPending}
          {...fieldA11y("company")}
          {...register("company")}
        />
        {fieldError("company")}
      </div>

      <div className="flex flex-col gap-1.5">
        <Label htmlFor={FIELD_ELEMENT_IDS.url}>
          {t("ansokningar.new.urlLabel")}
        </Label>
        <Input
          id={FIELD_ELEMENT_IDS.url}
          type="url"
          inputMode="url"
          disabled={isPending}
          {...fieldA11y("url", URL_HINT_ID)}
          {...register("url")}
        />
        <p id={URL_HINT_ID} className="text-body-sm text-text-primary">
          {t("ansokningar.new.urlHint")}
        </p>
        {fieldError("url")}
      </div>

      <div className="flex flex-col gap-1.5">
        <Label htmlFor={FIELD_ELEMENT_IDS.expiresAt}>
          {t("ansokningar.new.expiresAtLabel")}
        </Label>
        <Input
          id={FIELD_ELEMENT_IDS.expiresAt}
          type="date"
          disabled={isPending}
          {...fieldA11y("expiresAt", EXPIRES_HINT_ID)}
          {...register("expiresAt")}
        />
        <p id={EXPIRES_HINT_ID} className="text-body-sm text-text-primary">
          {t("ansokningar.new.expiresAtHint")}
        </p>
        {fieldError("expiresAt")}
      </div>

      <div className="flex flex-col gap-1.5">
        <Label htmlFor={FIELD_ELEMENT_IDS.coverLetter}>
          {t("ansokningar.new.coverLetterLabel")}
        </Label>
        <Textarea
          id={FIELD_ELEMENT_IDS.coverLetter}
          rows={8}
          disabled={isPending}
          {...fieldA11y("coverLetter", COVER_LETTER_HINT_ID)}
          {...register("coverLetter")}
        />
        <p id={COVER_LETTER_HINT_ID} className="text-body-sm text-text-primary">
          {t("ansokningar.new.coverLetterHint")}
        </p>
        {fieldError("coverLetter")}
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

      <div className="flex items-center gap-3">
        <Button type="submit" disabled={isPending}>
          {isPending
            ? t("ansokningar.new.submitting")
            : t("ansokningar.new.submit")}
        </Button>
        <Button asChild variant="ghost">
          <Link href="/ansokningar">{t("ansokningar.new.cancel")}</Link>
        </Button>
      </div>
    </form>
  );
}

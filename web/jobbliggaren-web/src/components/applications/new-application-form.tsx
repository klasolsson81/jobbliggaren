"use client";

import Link from "next/link";
import { useEffect, useId, useMemo, useRef, useState, useTransition } from "react";
import { useForm } from "react-hook-form";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Label } from "@/components/ui/label";
import { createApplicationAction } from "@/lib/actions/applications";
import { makeCreateApplicationSchema } from "@/lib/actions/application-schemas";

type FormValues = {
  title: string;
  company: string;
  url: string;
  expiresAt: string;
  coverLetter: string;
};

// Maps a zod issue path back to the control that owns it, so a refusal can mark and focus the
// input it names. Keyed by form field, so a renamed field breaks the build rather than the routing.
// The JSX derives every `id`/`htmlFor` from this map for the same reason.
const FIELD_ELEMENT_IDS: Record<keyof FormValues, string> = {
  title: "title",
  company: "company",
  url: "url",
  expiresAt: "expiresAt",
  coverLetter: "cover-letter",
};

// Narrows a zod issue path segment to a field of this form. A path naming something the form does
// not render leaves the refusal fieldless, so it announces at the message row rather than marking
// a control that is not there.
function isFieldName(value: unknown): value is keyof FormValues {
  return typeof value === "string" && value in FIELD_ELEMENT_IDS;
}

// Hint paragraphs the three optional fields are described by. A field in error is described by its
// hint AND the message, not the message alone: each hint states the constraint the refusal is
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
 * Client validation mirrors the server's own schema (`makeCreateApplicationSchema`, the same
 * builder the action runs) and the server stays authoritative — the shape `cv-gapfill-form` and
 * `add-follow-up-form` use.
 */
export function NewApplicationForm() {
  const t = useTranslations("pages");
  const tUi = useTranslations("applications.ui");
  const tValidation = useTranslations("validation");
  // The action's own schema, unmodified: every member of it is a field of this form. The action
  // still parses the whole object server-side and stays the authority; this is only the round trip
  // saved.
  const schema = useMemo(
    () => makeCreateApplicationSchema(tValidation),
    [tValidation]
  );

  const errorId = useId();
  const errorRef = useRef<HTMLParagraphElement>(null);
  const [isPending, startTransition] = useTransition();
  // An object rather than a bare string so two identical failures in a row are two distinct states
  // — the focus effect below has to fire on the second one too. `field` names the ONE control a
  // client-side refusal belongs to, and is absent for a server failure that belongs to no field.
  const [error, setError] = useState<{
    message: string;
    field?: keyof FormValues;
  } | null>(null);

  const { register, handleSubmit } = useForm<FormValues>({
    defaultValues: {
      title: "",
      company: "",
      url: "",
      expiresAt: "",
      coverLetter: "",
    },
  });

  function fieldA11y(name: keyof FormValues, hintId?: string) {
    const invalid = error?.field === name;
    return {
      "aria-invalid": invalid || undefined,
      "aria-describedby": invalid
        ? [hintId, errorId].filter((id) => id !== undefined).join(" ")
        : hintId,
    };
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
      title: values.title,
      company: values.company,
      url: values.url,
      expiresAt: values.expiresAt,
      coverLetter: values.coverLetter || undefined,
    });
    if (!parsed.success) {
      const issue = parsed.error.issues[0];
      const path = issue?.path[0];
      setError({
        message: issue?.message ?? tUi("actions.invalidInput"),
        field: isFieldName(path) ? path : undefined,
      });
      return;
    }

    startTransition(async () => {
      const formData = new FormData();
      formData.set("title", values.title);
      formData.set("company", values.company);
      formData.set("url", values.url);
      formData.set("expiresAt", values.expiresAt);
      formData.set("coverLetter", values.coverLetter);
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
        setError({ message: result.error });
      }
    });
  }

  return (
    <form
      onSubmit={handleSubmit(onSubmit)}
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

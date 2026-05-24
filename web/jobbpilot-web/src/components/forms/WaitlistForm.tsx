"use client";

import { useState, useTransition } from "react";
import Link from "next/link";
import { useForm } from "react-hook-form";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Label } from "@/components/ui/label";
import {
  requestWaitlistAction,
  type WaitlistActionState,
} from "@/lib/waitlist/actions";
import { waitlistFormSchema, type WaitlistFormInput } from "@/lib/dto/waitlist";

const DEFAULT_VALUES: WaitlistFormInput = {
  name: "",
  email: "",
  motivation: "",
  marketingEmailAccepted: false,
};

type FieldKey = keyof WaitlistFormInput;

export function WaitlistForm() {
  const [state, setState] = useState<WaitlistActionState>({ status: "idle" });
  const [isPending, startTransition] = useTransition();

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<WaitlistFormInput>({
    defaultValues: DEFAULT_VALUES,
    shouldUnregister: false,
  });

  function onSubmit(values: WaitlistFormInput) {
    setState({ status: "idle" });
    const parsed = waitlistFormSchema.safeParse(values);
    if (!parsed.success) {
      const fieldErrors: Record<string, string> = {};
      for (const issue of parsed.error.issues) {
        const key = issue.path[0];
        if (typeof key === "string" && !fieldErrors[key]) {
          fieldErrors[key] = issue.message;
        }
      }
      setState({
        status: "error",
        error: "Kontrollera fälten och försök igen.",
        fieldErrors,
      });
      return;
    }

    const formData = new FormData();
    formData.set("name", parsed.data.name);
    formData.set("email", parsed.data.email);
    formData.set("motivation", parsed.data.motivation);
    formData.set(
      "marketingEmailAccepted",
      parsed.data.marketingEmailAccepted ? "true" : "false",
    );

    startTransition(async () => {
      const result = await requestWaitlistAction(state, formData);
      setState(result);
    });
  }

  if (state.status === "success") {
    return (
      <div
        role="status"
        aria-live="polite"
        className="flex flex-col gap-3 rounded-lg border border-border bg-surface-secondary p-6"
      >
        <p className="text-body font-medium text-text-primary">
          Tack för din anmälan.
        </p>
        <p className="text-body text-text-secondary">
          Vi har sparat <span className="font-medium">{state.email}</span> på
          väntelistan. Vi hör av oss när vi har kapacitet att släppa in fler
          användare — du behöver inte göra något mer just nu.
        </p>
      </div>
    );
  }

  const fieldError = (key: FieldKey): string | undefined =>
    state.status === "error" ? state.fieldErrors?.[key] : undefined;

  function fieldA11y(key: FieldKey) {
    const err = errors[key]?.message ?? fieldError(key);
    return err
      ? ({ "aria-invalid": true, "aria-describedby": `${key}-error` } as const)
      : {};
  }

  function renderFieldError(name: FieldKey) {
    const message = errors[name]?.message ?? fieldError(name);
    if (!message) return null;
    return (
      <p id={`${name}-error`} role="alert" className="text-body-sm text-danger-600">
        {message}
      </p>
    );
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} noValidate className="flex flex-col gap-6">
      <div className="flex flex-col gap-1.5">
        <Label htmlFor="name" className="text-label font-medium text-text-primary">
          Namn
        </Label>
        <Input
          id="name"
          type="text"
          autoComplete="name"
          maxLength={100}
          {...register("name", {
            required: "Namn krävs.",
            maxLength: { value: 100, message: "Namn får vara max 100 tecken." },
          })}
          {...fieldA11y("name")}
        />
        {renderFieldError("name")}
      </div>

      <div className="flex flex-col gap-1.5">
        <Label htmlFor="email" className="text-label font-medium text-text-primary">
          E-postadress
        </Label>
        <Input
          id="email"
          type="email"
          autoComplete="email"
          maxLength={254}
          {...register("email", {
            required: "E-postadress krävs.",
            maxLength: { value: 254, message: "E-postadress får vara max 254 tecken." },
          })}
          {...fieldA11y("email")}
        />
        {renderFieldError("email")}
      </div>

      <div className="flex flex-col gap-1.5">
        <Label htmlFor="motivation" className="text-label font-medium text-text-primary">
          Varför vill du använda JobbPilot?
        </Label>
        <Textarea
          id="motivation"
          rows={5}
          maxLength={1000}
          {...register("motivation", {
            required: "Motivering krävs.",
            minLength: { value: 10, message: "Motiveringen ska vara minst 10 tecken." },
            maxLength: { value: 1000, message: "Motiveringen får vara max 1000 tecken." },
          })}
          {...fieldA11y("motivation")}
          aria-describedby={
            errors.motivation?.message || fieldError("motivation")
              ? "motivation-error"
              : "motivation-hint"
          }
        />
        <p id="motivation-hint" className="text-body-sm text-text-secondary">
          Skriv kort om hur du tänker använda tjänsten. 10–1000 tecken.
        </p>
        {renderFieldError("motivation")}
      </div>

      <div className="flex flex-col gap-2 border-t border-border pt-5">
        <label className="flex items-start gap-3 text-body text-text-primary">
          <input
            id="marketingEmailAccepted"
            type="checkbox"
            className="mt-1 size-4 rounded-sm border-border accent-brand-600 focus-visible:outline-2 focus-visible:outline-brand-600 focus-visible:outline-offset-2"
            {...register("marketingEmailAccepted")}
          />
          <span>
            Jag vill få e-post med information om hur JobbPilot utvecklas
            (valfritt).
          </span>
        </label>
      </div>

      {state.status === "error" && !state.fieldErrors && (
        <p role="alert" className="text-body-sm text-danger-600">
          {state.error}
        </p>
      )}

      <Button
        type="submit"
        disabled={isPending}
        aria-busy={isPending}
        className="w-full"
      >
        {isPending ? "Skickar anmälan…" : "Anmäl till väntelista"}
      </Button>

      <p className="text-body-sm text-text-secondary">
        Genom att skicka in godkänner du JobbPilots{" "}
        <Link
          href="/villkor"
          className="text-brand-600 underline underline-offset-2 hover:text-brand-700"
        >
          användarvillkor
        </Link>{" "}
        och att vi använder{" "}
        <Link
          href="/cookies"
          className="text-brand-600 underline underline-offset-2 hover:text-brand-700"
        >
          nödvändiga cookies
        </Link>{" "}
        för att hantera din anmälan.
      </p>
    </form>
  );
}

"use client";

import { useActionState, useEffect, useId, useRef } from "react";
import { useTranslations } from "next-intl";
import { ChangeEmailButton } from "@/components/auth/change-email-button";
import { LoginFormMessage } from "@/components/auth/login-form-message";
import { AcceptTermsCheckbox } from "@/components/forms/AcceptTermsCheckbox";
import { Button } from "@/components/ui/button";
import type { ConsentStepState } from "@/lib/auth/challenge-action-state";
import { completeRegistration } from "@/lib/auth/challenge-actions";

// Client because it holds the action's state (`useActionState`) and moves focus when it arrives.
//
// Step three, for a new address only: one decision, accept the terms or not.
//
// The step shows NO address. The account is created on the address the grant proved, whatever this
// browser's cookie once held, so any address rendered here would be a claim the page cannot stand
// behind. What it says instead is unconditionally true: the account is created on the address the
// code was just confirmed for.
//
// The exit is part of the form. Until the acceptance nothing durable has been written, so walking
// away is the right thing for someone who sees the address was wrong, and without it the only
// ways out are accepting or closing the tab.
export function ConsentForm() {
  const t = useTranslations("pages");
  const [state, formAction, isPending] = useActionState<ConsentStepState, FormData>(
    completeRegistration,
    null
  );
  const checkboxRef = useRef<HTMLInputElement>(null);
  const statusRef = useRef<HTMLParagraphElement>(null);
  const errorId = useId();
  const fieldInvalid = state?.channel === "field";

  useEffect(() => {
    if (!state) return;
    (state.channel === "field" ? checkboxRef : statusRef).current?.focus();
  }, [state]);

  return (
    <div className="flex flex-col gap-5">
      <form action={formAction} noValidate className="flex flex-col gap-5">
        {/* Never re-ticked after a refusal: the only refusal is of an UNTICKED box. */}
        <AcceptTermsCheckbox
          ref={checkboxRef}
          aria-invalid={fieldInvalid ? true : undefined}
          aria-describedby={fieldInvalid ? errorId : undefined}
        />

        {state && (
          <LoginFormMessage
            id={errorId}
            message={state.error}
            channel={state.channel}
            statusRef={statusRef}
          />
        )}

        {/* This step creates a session too, so the disclosure sits above its primary as well (D4). */}
        <p className="text-body-sm text-text-primary">{t("auth.passwordless.persistence")}</p>

        <Button type="submit" disabled={isPending} className="w-full max-md:h-11">
          {isPending
            ? t("auth.passwordless.consent.submitting")
            : t("auth.passwordless.consent.submit")}
        </Button>
      </form>

      <ChangeEmailButton label={t("auth.passwordless.consent.startOver")} />
    </div>
  );
}

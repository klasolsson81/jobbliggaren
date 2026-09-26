"use client";

import { useActionState, useEffect, useId, useRef } from "react";
import { useTranslations } from "next-intl";
import { LoginFormMessage } from "@/components/auth/login-form-message";
import { CodeField } from "@/components/forms/code-field";
import { Button } from "@/components/ui/button";
import type { CodeStepState } from "@/lib/auth/challenge-action-state";
import { verifyCode } from "@/lib/auth/challenge-actions";

/** The field's id: "Skicka ny kod" sends focus here after a resend. */
export const CODE_INPUT_ID = "code";

// Client because it holds the action's state (`useActionState`) and moves focus when it arrives.
//
// Step two: the six-digit code, in the field shape every code step shares (`CodeField`).
//
// The page cannot tell an existing account from a new address, and must not, so the primary says
// what the press does on both paths ("Bekräfta koden"), and the persistence disclosure sits
// directly above it.
//
// A wrong code is action state, not cookie state, on purpose: it must not survive a reload. The
// code is never echoed back, so the field is retyped after a miss.
export function CodeForm() {
  const t = useTranslations("pages");
  const [state, formAction, isPending] = useActionState<CodeStepState, FormData>(verifyCode, null);
  const inputRef = useRef<HTMLInputElement>(null);
  const statusRef = useRef<HTMLParagraphElement>(null);
  const errorId = useId();
  const persistenceId = useId();
  const fieldInvalid = state?.channel === "field";

  useEffect(() => {
    if (!state) return;
    (state.channel === "field" ? inputRef : statusRef).current?.focus();
  }, [state]);

  // One slot, one announcement: the last-attempt warning rides in the same alert as the miss.
  const message = state?.lastAttempt
    ? `${state.error} ${t("auth.passwordless.code.lastAttempt")}`
    : state?.error;

  return (
    <form action={formAction} noValidate className="flex flex-col gap-5">
      <CodeField
        id={CODE_INPUT_ID}
        hintId="code-hint"
        label={t("auth.passwordless.code.codeLabel")}
        hint={t("auth.passwordless.code.codeHint")}
        invalid={fieldInvalid}
        errorId={errorId}
        inputRef={inputRef}
        name="code"
      />

      {state && message && (
        <LoginFormMessage
          id={errorId}
          message={message}
          channel={state.channel}
          statusRef={statusRef}
        />
      )}

      <p id={persistenceId} className="text-body-sm text-text-primary">
        {t("auth.passwordless.persistence")}
      </p>

      <Button
        type="submit"
        disabled={isPending}
        aria-describedby={persistenceId}
        className="w-full max-md:h-11"
      >
        {isPending ? t("auth.passwordless.code.submitting") : t("auth.passwordless.code.submit")}
      </Button>
    </form>
  );
}

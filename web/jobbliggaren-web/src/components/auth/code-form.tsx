"use client";

import { useActionState, useEffect, useId, useRef } from "react";
import { useTranslations } from "next-intl";
import { LoginFormMessage } from "@/components/auth/login-form-message";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import type { CodeStepState } from "@/lib/auth/challenge-action-state";
import { verifyCode } from "@/lib/auth/challenge-actions";

/** The field's id: "Skicka ny kod" sends focus here after a resend. */
export const CODE_INPUT_ID = "code";

// Client because it holds the action's state (`useActionState`) and moves focus when it arrives.
//
// Step two: the six-digit code. ONE input, never six boxes: a single `one-time-code` field is what
// the platform's autofill fills, and what a screen reader reads as one thing (ADR 0142, design M3).
//
// The page cannot tell an existing account from a new address, and must not, so the primary says
// what the press does on both paths ("Bekräfta koden"), and the persistence disclosure sits
// directly above it: this is the only step an existing account sees before a session exists (D4).
//
// A wrong code is action state, not cookie state, on purpose: it must not survive a reload. The
// code is never echoed back, so the field is retyped after a miss.
export function CodeForm() {
  const t = useTranslations("pages");
  const [state, formAction, isPending] = useActionState<CodeStepState, FormData>(verifyCode, null);
  const inputRef = useRef<HTMLInputElement>(null);
  const statusRef = useRef<HTMLParagraphElement>(null);
  const errorId = useId();
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
      <div className="flex flex-col gap-1.5">
        <label htmlFor={CODE_INPUT_ID} className="text-label font-medium text-text-primary">
          {t("auth.passwordless.code.codeLabel")}
        </label>
        <Input
          ref={inputRef}
          id={CODE_INPUT_ID}
          name="code"
          type="text"
          inputMode="numeric"
          autoComplete="one-time-code"
          pattern="[0-9]*"
          maxLength={6}
          required
          aria-required="true"
          aria-invalid={fieldInvalid ? true : undefined}
          aria-describedby={fieldInvalid ? `code-hint ${errorId}` : "code-hint"}
        />
        <p id="code-hint" className="text-body-sm text-text-primary">
          {t("auth.passwordless.code.codeHint")}
        </p>
      </div>

      {state && message && (
        <LoginFormMessage
          id={errorId}
          message={message}
          channel={state.channel}
          statusRef={statusRef}
        />
      )}

      <p className="text-body-sm text-text-primary">{t("auth.passwordless.persistence")}</p>

      <Button type="submit" disabled={isPending} className="w-full max-md:h-11">
        {isPending ? t("auth.passwordless.code.submitting") : t("auth.passwordless.code.submit")}
      </Button>
    </form>
  );
}

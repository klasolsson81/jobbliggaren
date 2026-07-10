"use client";

import { useActionState, useEffect, useRef } from "react";
import { useSearchParams } from "next/navigation";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { PasswordInput } from "@/components/forms/PasswordInput";
import { RememberMeCheckbox } from "@/components/forms/RememberMeCheckbox";
import { ResendConfirmationButton } from "@/components/auth/ResendConfirmationButton";
import { loginAction, type AuthActionState } from "@/lib/auth/actions";

export function LoginForm() {
  const t = useTranslations("pages");
  const searchParams = useSearchParams();
  const [state, formAction, isPending] = useActionState<AuthActionState, FormData>(
    loginAction,
    null
  );
  const emailInputRef = useRef<HTMLInputElement>(null);

  // TD-45 a11y: vid generic server-error (medvetet vag av säkerhetsskäl, inte
  // path-baserad som TD-15) flytta fokus till email-fältet. Screen reader läser
  // role="alert" automatiskt; focus-flytt ger keyboard-användare visuell anchor
  // + nästa recovery-action (skriva om credentials).
  useEffect(() => {
    if (state?.error) emailInputRef.current?.focus();
  }, [state?.error]);

  return (
    <form action={formAction} className="flex flex-col gap-5">
      <input type="hidden" name="next" value={searchParams.get("next") ?? "/jobb"} />

      <div className="flex flex-col gap-1.5">
        <label htmlFor="email" className="text-label font-medium text-text-primary">
          {t("auth.login.emailLabel")}
        </label>
        <Input
          ref={emailInputRef}
          id="email"
          name="email"
          type="email"
          autoComplete="email"
          required
          aria-required="true"
          aria-describedby="email-hint"
        />
        <p id="email-hint" className="text-body-sm text-text-primary">
          {t("auth.login.emailHint")}
        </p>
      </div>

      <div className="flex flex-col gap-1.5">
        <label htmlFor="password" className="text-label font-medium text-text-primary">
          {t("auth.login.passwordLabel")}
        </label>
        <PasswordInput
          id="password"
          name="password"
          autoComplete="current-password"
          required
          aria-required="true"
        />
      </div>

      <RememberMeCheckbox
        label={t("auth.login.rememberMeLabel")}
        hint={t("auth.login.rememberMeHint")}
      />

      {state?.error && (
        <p role="alert" className="text-body-sm leading-5 text-danger-600">
          {state.error}
        </p>
      )}

      {/* #733: the email input stays mounted here, so read it live at click time. */}
      {state?.emailNotConfirmed && (
        <ResendConfirmationButton
          getEmail={() => emailInputRef.current?.value ?? ""}
        />
      )}

      <Button type="submit" disabled={isPending} className="w-full">
        {isPending ? t("auth.login.submitting") : t("auth.login.submit")}
      </Button>
    </form>
  );
}

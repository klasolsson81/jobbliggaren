"use client";

import { useActionState, useEffect, useRef } from "react";
import Link from "next/link";
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
  // role="alert" automatiskt; focus-flytt ger keyboard-användare en visuell anchor
  // vid toppen av formuläret de ska gå igenom igen.
  // Fältet är nu förifyllt med den inskickade adressen, så fokus landar på ifylld
  // text. `select()` skulle inte markera den: markerings-API:t gäller inte för
  // `type="email"` (selectionStart är null), så anropet vore verkningslöst.
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
        {/* Re-seeded from the echo the action returns on failure: React 19 resets this uncontrolled
            form after every action, so a wrong password otherwise costs the address too. The
            password is deliberately not echoed and is the one field retyped on a retry. */}
        <Input
          ref={emailInputRef}
          id="email"
          name="email"
          type="email"
          autoComplete="email"
          defaultValue={state?.values?.email ?? ""}
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
        {/* #1171 — under the field it recovers, which is where a user looks after a failed attempt. */}
        <p className="text-body-sm">
          <Link
            href="/glomt-losenord"
            className="text-brand-600 hover:text-brand-700 underline underline-offset-2"
          >
            {t("auth.login.forgotPassword")}
          </Link>
        </p>
      </div>

      <RememberMeCheckbox
        label={t("auth.login.rememberMeLabel")}
        defaultChecked={state?.values?.rememberMe ?? false}
      />

      {state?.error && (
        <p role="alert" className="text-body-sm leading-5 text-danger-600">
          {state.error}
        </p>
      )}

      {/* #733/#791: read the submitted email from the action state, not the live input — the
          address the server actually received, independent of what the field holds at click
          time. loginAction echoes it on the 403 for exactly this. */}
      {state?.emailNotConfirmed && (
        <ResendConfirmationButton getEmail={() => state.email ?? ""} />
      )}

      <Button type="submit" disabled={isPending} className="w-full">
        {isPending ? t("auth.login.submitting") : t("auth.login.submit")}
      </Button>
    </form>
  );
}

"use client";

import { useActionState, useEffect, useRef } from "react";
import { useSearchParams } from "next/navigation";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { PasswordInput } from "@/components/forms/PasswordInput";
import { RememberMeCheckbox } from "@/components/forms/RememberMeCheckbox";
import { ResendConfirmationButton } from "@/components/auth/ResendConfirmationButton";
import { registerAction, type AuthActionState } from "@/lib/auth/actions";

export function RegisterForm() {
  const t = useTranslations("pages");
  const searchParams = useSearchParams();
  const [state, formAction, isPending] = useActionState<AuthActionState, FormData>(
    registerAction,
    null
  );
  const pendingRef = useRef<HTMLDivElement>(null);

  // Focus management (not data fetching): when registration flips to the pending-confirmation state,
  // move focus to the status panel so keyboard users land on it and screen readers announce it.
  useEffect(() => {
    if (state?.pendingConfirmation) pendingRef.current?.focus();
  }, [state?.pendingConfirmation]);

  // #714: email-confirmation-first — the backend returned 202. Show a "check your inbox" panel in
  // place of the form. Byte-identical for a fresh or a taken address (the account-enumeration status
  // oracle is closed; the only differentiator is the out-of-band email), so the FE never distinguishes
  // them. role="status" + aria-live announces the state change without a second page-level h1.
  if (state?.pendingConfirmation) {
    return (
      <div className="flex flex-col gap-4">
        <div
          ref={pendingRef}
          tabIndex={-1}
          role="status"
          aria-live="polite"
          className="flex flex-col gap-1 focus:outline-none"
        >
          {/* h2 (not a second h1 — the page already owns the h1): keeps the panel in the heading
              outline / reachable via heading navigation, while role=status + aria-live announce it. */}
          <h2 className="text-body font-bold text-heading-1">
            {t("auth.register.pendingTitle")}
          </h2>
          <p className="text-body text-text-primary">
            {t("auth.register.pendingBody")}
          </p>
        </div>
        {/* #733: sibling of the panel (not nested) so the resend button's own role=status live
            region is not wrapped inside this one — nested live regions double-announce. Email is
            echoed from the action state because the form (and its input) is unmounted here. */}
        <ResendConfirmationButton getEmail={() => state.email ?? ""} />
      </div>
    );
  }

  return (
    <form action={formAction} className="flex flex-col gap-5">
      <input type="hidden" name="next" value={searchParams.get("next") ?? "/jobb"} />

      <div className="flex flex-col gap-1.5">
        <label htmlFor="displayName" className="text-label font-medium text-text-primary">
          {t("auth.register.nameLabel")}
        </label>
        <Input
          id="displayName"
          name="displayName"
          type="text"
          autoComplete="name"
          required
          aria-required="true"
          aria-describedby="name-hint"
        />
        <p id="name-hint" className="text-body-sm text-text-primary">
          {t("auth.register.nameHint")}
        </p>
      </div>

      <div className="flex flex-col gap-1.5">
        <label htmlFor="email" className="text-label font-medium text-text-primary">
          {t("auth.register.emailLabel")}
        </label>
        <Input
          id="email"
          name="email"
          type="email"
          autoComplete="email"
          required
          aria-required="true"
          aria-describedby="email-hint"
        />
        <p id="email-hint" className="text-body-sm text-text-primary">
          {t("auth.register.emailHint")}
        </p>
      </div>

      <div className="flex flex-col gap-1.5">
        <label htmlFor="password" className="text-label font-medium text-text-primary">
          {t("auth.register.passwordLabel")}
        </label>
        <PasswordInput
          id="password"
          name="password"
          autoComplete="new-password"
          required
          aria-required="true"
          aria-describedby="password-hint"
        />
        <p id="password-hint" className="text-body-sm text-text-primary">
          {t("auth.register.passwordHint")}
        </p>
      </div>

      <RememberMeCheckbox
        label={t("auth.register.rememberMeLabel")}
        hint={t("auth.register.rememberMeHint")}
      />

      {state?.error && (
        <p role="alert" className="text-body-sm leading-5 text-danger-600">
          {state.error}
        </p>
      )}

      <Button type="submit" disabled={isPending} className="w-full">
        {isPending ? t("auth.register.submitting") : t("auth.register.submit")}
      </Button>
    </form>
  );
}

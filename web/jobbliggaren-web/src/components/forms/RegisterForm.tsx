"use client";

import { useActionState, useEffect, useId, useRef } from "react";
import { useSearchParams } from "next/navigation";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { PasswordInput } from "@/components/forms/PasswordInput";
import { AcceptTermsCheckbox } from "@/components/forms/AcceptTermsCheckbox";
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
  const displayNameRef = useRef<HTMLInputElement>(null);
  const acceptTermsRef = useRef<HTMLInputElement>(null);
  const passwordRef = useRef<HTMLInputElement>(null);
  const errorRef = useRef<HTMLParagraphElement>(null);
  const errorId = useId();
  // #1117: the error belongs to a named field only when the action says so. Absent `field`
  // means a non-field failure (network, kill-switch), which must not mark an input invalid.
  const displayNameInvalid = state?.error !== undefined && state.field === "displayName";
  // #1479: the server-side half of the terms gate. `required` already blocks the submit in a
  // browser with constraint validation, so this state is what a bypassed client produces.
  const acceptTermsInvalid = state?.error !== undefined && state.field === "acceptTerms";
  // A breached password is a refusal about ONE field, fixed by changing that field — the same
  // wiring `reset-password` gives the identical refusal on its own form.
  const passwordInvalid = state?.error !== undefined && state.field === "password";
  // A failure that names no field — an unreachable server, a validator
  // message from the backend. There is no input to send the caret to, and the submit button the
  // user pressed is disabled during the action, so focus lands on <body> and the next Tab starts
  // over at the skip link. The message itself is the only honest target.
  const genericError = state?.error !== undefined && state.field === undefined;

  // Focus goes to the field the user has to correct, the same move ForgotPasswordForm makes.
  // Without it the message is announced but the caret is nowhere near the input it names.
  useEffect(() => {
    if (displayNameInvalid) displayNameRef.current?.focus();
  }, [displayNameInvalid, state]);

  useEffect(() => {
    if (acceptTermsInvalid) acceptTermsRef.current?.focus();
  }, [acceptTermsInvalid, state]);

  useEffect(() => {
    if (passwordInvalid) passwordRef.current?.focus();
  }, [passwordInvalid, state]);

  useEffect(() => {
    if (genericError) errorRef.current?.focus();
  }, [genericError, state]);

  // Focus management (not data fetching): when registration flips to the pending-confirmation state,
  // move focus to the status panel so keyboard users land on it and screen readers announce it.
  useEffect(() => {
    if (state?.pendingConfirmation) pendingRef.current?.focus();
  }, [state?.pendingConfirmation]);

  const closedRef = useRef<HTMLDivElement>(null);

  // Same reason as the panel above, and it is not optional here either. Submitting unmounts the form,
  // so the focused element leaves the DOM and focus falls to <body> — the next Tab restarts from the
  // skip link. And role="status" announces CHANGES to a live region that already exists; this one
  // mounts already filled, which NVDA and JAWS routinely miss (WCAG 4.1.3). The focus move is what
  // actually delivers the outcome, which is why all four sibling auth panels do it.
  useEffect(() => {
    if (state?.registrationsClosed) closedRef.current?.focus();
  }, [state?.registrationsClosed]);

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
          className="flex flex-col gap-1"
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

  // ADR 0083 Amendment 2026-08-03 — public registration is held closed. Rendered in place of the
  // form, in the SAME channel as the 202 panel above and deliberately not in the error channel:
  // red text-danger-600 + role="alert" + a live submit button would read as "you typed something
  // wrong" and invite a retry that cannot succeed until a config flag is flipped and the process
  // restarts. role="status" + aria-live announces the state without claiming a fault.
  if (state?.registrationsClosed) {
    return (
      <div
        ref={closedRef}
        tabIndex={-1}
        role="status"
        aria-live="polite"
      >
        <p className="text-body text-text-primary">
          {t("auth.actions.registrationsClosed")}
        </p>
      </div>
    );
  }

  return (
    <form action={formAction} className="flex flex-col gap-5">
      <input type="hidden" name="next" value={searchParams.get("next") ?? "/jobb"} />

      {/* Every non-secret input below re-seeds from the echo the action returns on failure. React 19
          resets this uncontrolled form after every action, so without it a single wrong character
          costs the user the whole form. The password is deliberately absent from the echo and is
          therefore the one field that is retyped. */}
      <div className="flex flex-col gap-1.5">
        <label htmlFor="displayName" className="text-label font-medium text-text-primary">
          {t("auth.register.nameLabel")}
        </label>
        <Input
          ref={displayNameRef}
          id="displayName"
          name="displayName"
          type="text"
          autoComplete="name"
          defaultValue={state?.values?.displayName ?? ""}
          required
          aria-required="true"
          aria-invalid={displayNameInvalid ? true : undefined}
          aria-describedby={
            displayNameInvalid ? `name-hint ${errorId}` : "name-hint"
          }
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
          defaultValue={state?.values?.email ?? ""}
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
          ref={passwordRef}
          id="password"
          name="password"
          autoComplete="new-password"
          required
          aria-required="true"
          aria-invalid={passwordInvalid ? true : undefined}
          aria-describedby={
            passwordInvalid ? `password-hint ${errorId}` : "password-hint"
          }
        />
        <p id="password-hint" className="text-body-sm text-text-primary">
          {t("auth.register.passwordHint")}
        </p>
      </div>

      <AcceptTermsCheckbox
        ref={acceptTermsRef}
        defaultChecked={state?.values?.acceptTerms ?? false}
        aria-invalid={acceptTermsInvalid ? true : undefined}
        aria-describedby={acceptTermsInvalid ? errorId : undefined}
      />

      {state?.error && (
        <p
          ref={errorRef}
          tabIndex={-1}
          id={errorId}
          role="alert"
          className="text-body-sm leading-5 text-danger-600"
        >
          {state.error}
        </p>
      )}

      <Button type="submit" disabled={isPending} className="w-full">
        {isPending ? t("auth.register.submitting") : t("auth.register.submit")}
      </Button>
    </form>
  );
}

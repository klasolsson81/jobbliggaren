"use client";

import { useActionState, useEffect, useId, useRef } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  requestPasswordResetAction,
  type ForgotPasswordActionState,
} from "@/lib/actions/forgot-password";

/**
 * #1171 — the forgot-password request form.
 *
 * Three outcome channels, and keeping them apart is the point:
 *
 *  1. SENT — a `role="status"` panel replacing the form. Its copy is deliberately CONDITIONAL ("Om
 *     adressen hör till ett konto…"): the backend answers an identical 202 for a known and an unknown
 *     address, so a panel saying "we sent a link to your address" would confirm existence in the UI
 *     after the API refused to. The frontend must not invent the signal the backend withheld.
 *  2. REFUSED — its own `role="status"` panel, ALSO replacing the form, so the submit affordance is
 *     removed rather than merely relabelled. No configured sender can deliver, so no retry can succeed
 *     until an operator changes something; a live red error with a live button would read as "you typed
 *     something wrong" and invite exactly the retry that cannot work. Structure copied from
 *     `RegisterForm`'s registrationsClosed panel, which is the same class of state.
 *  3. ERROR — the ordinary `role="alert"` line, form intact, because those failures ARE retryable.
 *
 * Panels 1 and 2 move focus in an effect. Not optional: submitting unmounts the form, so the focused
 * element leaves the DOM and focus falls to <body>; and `role="status"` announces CHANGES to a live
 * region, while one that mounts already filled is routinely missed by NVDA and JAWS (WCAG 4.1.3). The
 * focus move is what actually delivers the outcome.
 */
export function ForgotPasswordForm() {
  const t = useTranslations("pages");
  const [state, formAction, isPending] = useActionState<
    ForgotPasswordActionState | null,
    FormData
  >(requestPasswordResetAction, null);

  const sentRef = useRef<HTMLDivElement>(null);
  const refusedRef = useRef<HTMLDivElement>(null);
  const emailInputRef = useRef<HTMLInputElement>(null);
  const errorId = useId();

  const refused = state?.success === false && state.refused === true;
  const sent = state?.success === true;

  useEffect(() => {
    if (sent) sentRef.current?.focus();
  }, [sent]);

  useEffect(() => {
    if (refused) refusedRef.current?.focus();
  }, [refused]);

  // An ordinary failure keeps the form, so focus goes back to the field the user would correct —
  // the same move LoginForm makes on its error.
  useEffect(() => {
    if (state?.success === false && !state.refused) emailInputRef.current?.focus();
  }, [state]);

  if (sent) {
    return (
      <div className="flex flex-col gap-4">
        <div
          ref={sentRef}
          tabIndex={-1}
          role="status"
          aria-live="polite"
          className="flex flex-col gap-1"
        >
          {/* h2, not a second h1 — the page owns the h1. Keeps the panel in the heading outline
              while role=status announces the change. */}
          <h2 className="text-body font-bold text-heading-1">
            {t("auth.forgotPassword.sentTitle")}
          </h2>
          <p className="text-body text-text-primary">
            {t("auth.forgotPassword.sentBody")}
          </p>
        </div>
        {/* Sibling of the live region, not nested — the exemplar (RegisterForm) keeps its affordance
            outside for the same reason: a live region should announce the state, not the navigation. */}
        <p className="text-body-sm">
          <Link
            href="/logga-in"
            className="text-brand-600 hover:text-brand-700 underline underline-offset-2"
          >
            {t("auth.forgotPassword.backToLogin")}
          </Link>
        </p>
      </div>
    );
  }

  if (refused) {
    return (
      <div className="flex flex-col gap-4">
        <div
          ref={refusedRef}
          tabIndex={-1}
          role="status"
          aria-live="polite"
        >
          <p className="text-body text-text-primary">{state.error}</p>
        </div>
        {/* A way out. The refusal removes the form, the auth layout renders no login link
            (showLogin={false}), so without this the page body offers no navigation at all. */}
        <p className="text-body-sm">
          <Link
            href="/logga-in"
            className="text-brand-600 hover:text-brand-700 underline underline-offset-2"
          >
            {t("auth.forgotPassword.backToLogin")}
          </Link>
        </p>
      </div>
    );
  }

  return (
    <form action={formAction} className="flex flex-col gap-5">
      {/* The instruction belongs to the FIELD, so it unmounts with it. Left on the page it would tell
          the user to fill in something that is gone, and in the refused state it would contradict the
          panel outright ("we will send you a link" above "we cannot send a link"). */}
      <p className="text-body text-text-secondary">{t("auth.forgotPassword.intro")}</p>

      <div className="flex flex-col gap-1.5">
        <label htmlFor="email" className="text-label font-medium text-text-primary">
          {t("auth.forgotPassword.emailLabel")}
        </label>
        {/* Re-seeded from the echo an ordinary failure returns. React 19 resets this uncontrolled
            form after every action, so the address the user must resend was emptied by the very
            failure telling them to try again. */}
        <Input
          ref={emailInputRef}
          id="email"
          name="email"
          type="email"
          autoComplete="email"
          defaultValue={
            state?.success === false ? (state.values?.email ?? "") : ""
          }
          required
          aria-required="true"
          aria-invalid={
            state?.success === false && state.field === "email" ? true : undefined
          }
          aria-describedby={
            state?.success === false && !state.refused
              ? `email-hint ${errorId}`
              : "email-hint"
          }
        />
        <p id="email-hint" className="text-body-sm text-text-primary">
          {t("auth.forgotPassword.emailHint")}
        </p>
      </div>

      {state?.success === false && !state.refused && (
        <p id={errorId} role="alert" className="text-body-sm leading-5 text-danger-600">
          {state.error}
        </p>
      )}

      <Button type="submit" disabled={isPending} className="w-full">
        {isPending
          ? t("auth.forgotPassword.submitting")
          : t("auth.forgotPassword.submit")}
      </Button>

      <p className="text-body-sm text-text-primary">
        <Link
          href="/logga-in"
          className="text-brand-600 hover:text-brand-700 underline underline-offset-2"
        >
          {t("auth.forgotPassword.backToLogin")}
        </Link>
      </p>
    </form>
  );
}

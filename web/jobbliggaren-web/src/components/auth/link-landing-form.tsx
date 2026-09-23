"use client";

import { useActionState, useEffect, useRef } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { LoginOutcomePanel } from "@/components/auth/login-outcome-panel";
import { Button } from "@/components/ui/button";
import type { LinkStepState } from "@/lib/auth/challenge-action-state";
import { consumeLink } from "@/lib/auth/challenge-actions";

// Client because it holds the action's state (`useActionState`) and moves focus when it arrives.
//
// The landing of the login link in the mail. The token is consumed by a PRESS, never by the GET
// and never on mount: mail scanners and link previews fetch the URL, and each would burn a
// single-use link before its owner saw it. A `<form action>` with the token in a hidden input, so
// it works without JavaScript.
//
// This browser may hold a session already. Pressing would replace it with a session for the
// address the link proves, and everything done afterwards would land on that account; a link
// minted for someone else's address and mailed to a logged-in user is a login CSRF that needs one
// press. So that arm says what continuing does, and asks for a choice between two buttons (Klas,
// 2026-09-21). It never names the address: the GET reads no record, and naming it would tell
// whoever found the URL whose it is.
//
// `consumeLink` is what decides that arm. `alreadyLoggedIn` is the GET's early signal and is
// absent on a click in webmail, where the Strict session cookie is not sent; the first press then
// answers `confirm` and consumes nothing. The arm's own press carries `replaceSession`.
//
// The h1 is rendered here because it names the arm, and the arm can change after a press.
//
// The outcomes are action state here, not cookie state, and that is safe on this page only: it
// never reads the flow cookie, so it has no phase check to lose a panel to.
export function LinkLandingForm({
  token,
  alreadyLoggedIn,
}: {
  token: string;
  alreadyLoggedIn: boolean;
}) {
  const t = useTranslations("pages");
  const [state, formAction, isPending] = useActionState<LinkStepState, FormData>(
    consumeLink,
    null
  );
  const messageRef = useRef<HTMLParagraphElement>(null);
  const headingRef = useRef<HTMLHeadingElement>(null);

  const asksForChoice =
    alreadyLoggedIn || state?.kind === "confirm" || (state?.kind !== undefined && state.confirmed);

  useEffect(() => {
    if (state?.kind === "unusable" || state?.kind === "error") messageRef.current?.focus();
    if (state?.kind === "confirm") headingRef.current?.focus();
  }, [state]);

  const heading = (
    <h1 ref={headingRef} tabIndex={-1} className="text-h1 font-bold text-heading-1">
      {asksForChoice
        ? t("auth.passwordless.link.alreadyLoggedIn.title")
        : t("auth.passwordless.link.title")}
    </h1>
  );

  if (state?.kind === "outcome") {
    return (
      <>
        {heading}
        <LoginOutcomePanel result={state.result} />
      </>
    );
  }

  // A dead link has no retry, so the form, and with it the token, is not rendered again.
  if (state?.kind === "unusable") {
    return (
      <>
        {heading}
        <UnusableLink messageRef={messageRef} />
      </>
    );
  }

  return (
    <>
      {heading}
      <form action={formAction} className="flex flex-col gap-5">
        <input type="hidden" name="token" value={token} />
        {asksForChoice && <input type="hidden" name="replaceSession" value="on" />}

        <p className="text-body text-text-primary">
          {asksForChoice
            ? t("auth.passwordless.link.alreadyLoggedIn.body")
            : t("auth.passwordless.link.body")}
        </p>

        {state?.kind === "error" && (
          <p
            ref={messageRef}
            tabIndex={-1}
            role="status"
            aria-live="polite"
            className="text-body-sm leading-5 text-text-primary"
          >
            {state.error}
          </p>
        )}

        <div className="flex flex-col gap-3">
          <Button type="submit" disabled={isPending} className="w-full max-md:h-11">
            {isPending
              ? t("auth.passwordless.link.submitting")
              : asksForChoice
                ? t("auth.passwordless.link.alreadyLoggedIn.continue")
                : t("auth.passwordless.link.submit")}
          </Button>
          {asksForChoice && (
            <Button asChild variant="outline" className="w-full max-md:h-11">
              <Link href="/oversikt">{t("auth.passwordless.link.alreadyLoggedIn.stay")}</Link>
            </Button>
          )}
        </div>
      </form>
    </>
  );
}

/** One sentence for every link that cannot be used: expired, used, malformed or missing. */
export function UnusableLink({
  messageRef,
}: {
  messageRef?: React.Ref<HTMLParagraphElement>;
}) {
  const t = useTranslations("pages");

  return (
    <div className="flex flex-col gap-4">
      <p
        ref={messageRef}
        tabIndex={-1}
        role="status"
        aria-live="polite"
        className="text-body text-text-primary"
      >
        {t("auth.passwordless.link.unusable")}
      </p>
      <p className="text-body-sm text-text-primary">
        <Link href="/logga-in" className="text-brand-700 underline underline-offset-2 max-md:py-3">
          {t("auth.passwordless.link.toLogin")}
        </Link>
      </p>
    </div>
  );
}

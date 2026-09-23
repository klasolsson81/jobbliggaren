"use client";

import { useActionState, useEffect } from "react";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { CODE_INPUT_ID } from "@/components/auth/code-form";
import type { ResendState } from "@/lib/auth/challenge-action-state";
import { resendCode } from "@/lib/auth/challenge-actions";
import { useCountdown } from "@/lib/hooks/use-countdown";

// Client because it holds the action's state (`useActionState`) and counts the cooldown down.
//
// "Skicka ny kod". The form of `ResendConfirmationButton` (disabled while cooling, the countdown
// OUTSIDE the live region so a screen reader is not read a number every second, the message in
// `role="status"`), with two differences that both come from what a resend costs here.
//
// It STARTS in cooldown. A new code replaces the one already mailed, and inside the server's
// per-address window a request returns a challenge with no record at all, which would make the
// mailed code unverifiable. The window opens at the step-one submit, so the seconds left are
// counted by the server from the cookie's mint time and handed in; a reload does not restart them.
//
// And it says what pressing costs, in static text beside the button, before the press.
//
// A `<form action>`, so it works without JavaScript. `resendCode` rewrites the cookie and the page
// re-renders around this component, which therefore keeps one position in the tree: that is what
// lets its receipt survive the re-render, including the one that brings the field back after a
// dead code.
export function ResendCodeButton({
  sentAt,
  initialCooldownSeconds,
  primary,
}: {
  /** The mint time of the challenge on screen. A new value means a new challenge: count again. */
  sentAt: number;
  initialCooldownSeconds: number;
  /** True once the code is dead and this is the way forward. */
  primary: boolean;
}) {
  const t = useTranslations("pages");
  const [state, formAction, isPending] = useActionState<ResendState, FormData>(resendCode, null);
  // A new `sentAt` is a new challenge, so the count starts again in the same paint.
  const cooldown = useCountdown(initialCooldownSeconds, sentAt);

  // The button stays disabled for the whole cooldown, so focus cannot return to it. The field is
  // the user's next act, on both arms: a resend after a dead code brings it back.
  useEffect(() => {
    if (state?.status === "sent") document.getElementById(CODE_INPUT_ID)?.focus();
  }, [state]);

  const isCoolingDown = cooldown > 0;
  const message =
    state?.status === "sent"
      ? t("auth.passwordless.code.resend.receipt")
      : state?.status === "error"
        ? state.error
        : null;

  return (
    <form action={formAction} className="flex flex-col gap-2">
      <div>
        <Button
          type="submit"
          variant={primary && !isCoolingDown ? "default" : "outline"}
          disabled={isPending || isCoolingDown}
          className="max-md:h-11"
        >
          {isPending
            ? t("auth.passwordless.code.resend.sending")
            : t("auth.passwordless.code.resend.button")}
        </Button>
      </div>

      {isCoolingDown && (
        <p className="text-body-sm text-text-primary">
          {t("auth.passwordless.code.resend.cooldownHint", { seconds: cooldown })}
        </p>
      )}

      {/* A throttle or an outage on a resend is a status here too: never danger colour. */}
      <div role="status" aria-live="polite">
        {message && <p className="text-body-sm text-text-primary">{message}</p>}
      </div>

      <p className="text-body-sm text-text-primary">
        {t("auth.passwordless.code.resend.consequence")}
      </p>
    </form>
  );
}

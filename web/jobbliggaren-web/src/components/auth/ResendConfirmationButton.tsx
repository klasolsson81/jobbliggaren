"use client";

// #733 — shared resend-confirmation-link island. Client because it owns an explicit click interaction,
// a local sending/sent/error state machine, and a client-side cooldown timer (setInterval). Consumed by
// LoginForm (the 403 "email not confirmed" state) and RegisterForm (the 202 check-inbox panel), each of
// which passes the current email through `getEmail` — a function, not a string, so the value is read at
// click time (the login email input can still change; the register panel echoes the submitted address).
//
// SECURITY (§5): the email is user PII. It is read from `getEmail()` only to hand to the server action
// and is NEVER logged (no `console.*`). The success message is the SAME uniform anti-enumeration copy
// regardless of whether an unconfirmed account exists — the FE never confirms the address.

import { useEffect, useState } from "react";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { resendConfirmationAction } from "@/lib/actions/resend-confirmation";

const COOLDOWN_SECONDS = 60;

type Phase =
  | { status: "idle" }
  | { status: "sending" }
  | { status: "sent"; message: string }
  | { status: "error"; message: string };

interface ResendConfirmationButtonProps {
  getEmail: () => string;
}

export function ResendConfirmationButton({
  getEmail,
}: ResendConfirmationButtonProps) {
  const t = useTranslations("pages");
  const [phase, setPhase] = useState<Phase>({ status: "idle" });
  const [cooldown, setCooldown] = useState(0);

  // Client-side cooldown countdown (not data fetching): after a successful resend the button is
  // disabled for COOLDOWN_SECONDS to throttle repeat clicks in the UI (the backend rate-limits with
  // 429 as the real defence). The interval decrements once a second and is cleared on unmount and when
  // it reaches 0. Re-running on each `cooldown` change keeps exhaustive-deps satisfied and self-clears.
  useEffect(() => {
    if (cooldown <= 0) return;
    const id = setInterval(() => {
      setCooldown((seconds) => (seconds <= 1 ? 0 : seconds - 1));
    }, 1000);
    return () => clearInterval(id);
  }, [cooldown]);

  async function onResend() {
    const email = getEmail().trim();
    if (email.length === 0) return;

    setPhase({ status: "sending" });
    const result = await resendConfirmationAction(email);
    if (result.success) {
      setPhase({ status: "sent", message: t("auth.resendConfirmation.sent") });
      setCooldown(COOLDOWN_SECONDS);
    } else {
      setPhase({ status: "error", message: result.error });
    }
  }

  const isSending = phase.status === "sending";
  const isCoolingDown = cooldown > 0;
  const message =
    phase.status === "sent" || phase.status === "error" ? phase.message : null;

  return (
    <div className="flex flex-col gap-2">
      <div>
        <Button
          type="button"
          variant="outline"
          onClick={onResend}
          disabled={isSending || isCoolingDown}
        >
          {isSending
            ? t("auth.resendConfirmation.sending")
            : t("auth.resendConfirmation.button")}
        </Button>
      </div>

      {/* Announced once (polite): the uniform sent message or the retryable error. */}
      <div role="status" aria-live="polite">
        {message && (
          <p
            className={
              phase.status === "error"
                ? "text-body-sm text-danger-700"
                : "text-body-sm text-text-primary"
            }
          >
            {message}
          </p>
        )}
      </div>

      {/* Visible countdown kept OUT of the live region so screen readers are not spammed once a
          second. The disabled button already communicates the throttled state to all users. */}
      {isCoolingDown && (
        <p className="text-body-sm text-text-primary">
          {t("auth.resendConfirmation.cooldownHint", { seconds: cooldown })}
        </p>
      )}
    </div>
  );
}

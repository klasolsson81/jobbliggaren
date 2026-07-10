"use client";

// #714 — PUBLIC registration-confirmation island. Client because it owns the explicit confirm
// interaction: the confirm POST fires ONLY on this button's click, NEVER on page load. Mail scanners
// and link prefetchers issue GET requests against the link in the email; if we POSTed in an effect on
// mount they would silently auto-consume the confirmation. A human button click is the gate.
//
// It drives a small three-state machine (idle -> done | error) around the public `confirmAccountAction`.
// On success EmailConfirmed is set and the next step is a fresh login. On failure the message is shown
// and the button stays so a transient network error can be retried. uid/token are never logged (§5).

import { useEffect, useRef, useState, useTransition } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { confirmAccountAction } from "@/lib/actions/confirm-account";

interface ConfirmAccountProps {
  uid: string;
  token: string;
}

type Phase =
  | { status: "idle" }
  | { status: "done" }
  | { status: "error"; message: string };

export function ConfirmAccount({ uid, token }: ConfirmAccountProps) {
  const t = useTranslations("pages");
  const [phase, setPhase] = useState<Phase>({ status: "idle" });
  const [isPending, startTransition] = useTransition();
  const doneHeadingRef = useRef<HTMLHeadingElement>(null);

  // Focus management (not data fetching): when the confirmation resolves, move focus to the success
  // heading so keyboard users land on the result and screen readers announce the state change.
  useEffect(() => {
    if (phase.status === "done") doneHeadingRef.current?.focus();
  }, [phase.status]);

  function onConfirm() {
    startTransition(async () => {
      const result = await confirmAccountAction(uid, token);
      if (result.success) {
        setPhase({ status: "done" });
      } else {
        setPhase({ status: "error", message: result.error });
      }
    });
  }

  if (phase.status === "done") {
    return (
      <div className="flex flex-col gap-6">
        <div className="flex flex-col gap-1">
          <h1
            ref={doneHeadingRef}
            tabIndex={-1}
            className="text-h1 font-bold text-heading-1 focus:outline-none"
          >
            {t("auth.confirmAccount.successTitle")}
          </h1>
          <p className="text-body text-text-primary">
            {t("auth.confirmAccount.successBody")}
          </p>
        </div>
        <div>
          <Button asChild>
            <Link href="/logga-in">{t("auth.confirmAccount.loginLink")}</Link>
          </Button>
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-h1 font-bold text-heading-1">
          {t("auth.confirmAccount.title")}
        </h1>
        <p className="text-body text-text-primary">
          {t("auth.confirmAccount.intro")}
        </p>
      </div>

      {phase.status === "error" && (
        <p role="alert" className="text-body-sm text-danger-700">
          {phase.message}
        </p>
      )}

      <div className="flex flex-col gap-4">
        <Button type="button" onClick={onConfirm} disabled={isPending}>
          {isPending
            ? t("auth.confirmAccount.pending")
            : t("auth.confirmAccount.confirm")}
        </Button>
        {phase.status === "error" && (
          <p className="text-body-sm text-text-primary">
            <Link
              href="/logga-in"
              className="text-brand-700 underline underline-offset-2"
            >
              {t("auth.confirmAccount.loginLink")}
            </Link>
          </p>
        )}
      </div>
    </div>
  );
}

"use client";

import { useEffect, useRef, useState } from "react";
import { useTranslations } from "next-intl";
import { mailLink } from "@/components/auth/mail-link";
import type { ReauthHandOff } from "@/components/forms/reauth-code-dialog";
import { DeleteAccountDialog } from "./delete-account-dialog";

type Outcome = Exclude<ReauthHandOff<never>, { kind: "verified" }>;

/**
 * Deleting the account, inside the Sekretess och data card on /mina-sidor (#1740). Client because it
 * owns what came of a press the dialog could not finish: the dialog closes on those and hands them
 * here, where they are rendered and focused (design-reviewer, #1740 D2).
 *
 * - Mail cannot be delivered: the section becomes a panel naming the one route left, kontakt@.
 * - The backend refused after the code was spent: a status under the trigger, which stays.
 * - Nothing readable came back: the deletion may have happened, so the trigger goes and the panel says
 *   how to find out. A success never lands here; it redirects to the login page with its notice.
 */
export function DeleteAccountSection({ currentEmail }: { currentEmail: string }) {
  const t = useTranslations("settings");
  const [outcome, setOutcome] = useState<Outcome | null>(null);
  const targetRef = useRef<HTMLDivElement>(null);

  // Focus management (not data fetching): a panel that replaces the section mounts already filled,
  // which screen readers routinely miss, and the dialog it replaces is gone with its trigger. The move
  // is what delivers the message.
  useEffect(() => {
    if (outcome) targetRef.current?.focus();
  }, [outcome]);

  const heading = (
    <h3 id="delete-account-heading" className="text-h3 font-medium text-text-primary">
      {t("account.danger.title")}
    </h3>
  );

  function onHandOff(handOff: ReauthHandOff<never>) {
    if (handOff.kind !== "verified") setOutcome(handOff);
  }

  if (outcome?.kind === "refused" || outcome?.kind === "outcomeUnknown") {
    return (
      <section
        aria-labelledby="delete-account-heading"
        className="flex flex-col gap-3 border-t border-border pt-6"
      >
        {/* Focus lands on the wrapper so the heading is read with the message; role="status" sits on
            the message alone, since nested live regions announce twice. */}
        <div ref={targetRef} tabIndex={-1} className="flex flex-col gap-3">
          {heading}
          <p role="status" className="text-body text-text-primary [overflow-wrap:anywhere]">
            {outcome.kind === "refused"
              ? t.rich("account.delete.mailOff", { mail: mailLink })
              : outcome.error}
          </p>
          {outcome.kind === "outcomeUnknown" && (
            <p className="text-body-sm">
              <a href="/mina-sidor" className="text-brand-700 underline underline-offset-2">
                {t("account.reload")}
              </a>
            </p>
          )}
        </div>
      </section>
    );
  }

  return (
    <section
      aria-labelledby="delete-account-heading"
      className="flex flex-col gap-3 border-t border-border pt-6"
    >
      {heading}
      <p className="text-body text-text-primary">{t("account.danger.description")}</p>
      <div>
        <DeleteAccountDialog
          currentEmail={currentEmail}
          onHandOff={onHandOff}
          handOffTarget={targetRef}
        />
      </div>
      {/* Persistent live region under the trigger: the message is the only thing that changes. */}
      <div ref={targetRef} tabIndex={-1} role="status" aria-live="polite">
        {outcome?.kind === "operationRefused" && (
          <p className="text-body-sm text-text-primary">{outcome.error}</p>
        )}
      </div>
    </section>
  );
}

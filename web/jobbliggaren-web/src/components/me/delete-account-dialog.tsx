"use client";

// Delete-account on the shared re-authentication dialog (#1740, ADR 0142 D5). The dialog carries the
// code; this owns only what deletion adds: the typed address in the request step, and the action.
//
// The typed address is friction against the user's own mistake, never proof of identity (the code is).
// It is checked when "Skicka kod" is pressed, never by a disabled button, which would hide its reason,
// and the action compares it again with the SESSION's address before anything is spent (#822). The
// dialog holds what was confirmed together with the code, so a close and a re-open on the code step
// cannot reach the action without it.

import { type RefObject, useId, useRef, useState } from "react";
import { useTranslations } from "next-intl";
import { mailLink, mailText } from "@/components/auth/mail-link";
import { ReAuthCodeDialog, type ReauthHandOff } from "@/components/forms/reauth-code-dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { deleteAccountAction } from "@/lib/actions/me";
import { comparableAddress } from "@/lib/auth/comparable-address";

export function DeleteAccountDialog({
  currentEmail,
  onHandOff,
  handOffTarget,
}: {
  currentEmail: string;
  /** The section renders what came of a press the dialog could not finish itself. */
  onHandOff: (handOff: ReauthHandOff<never>) => void;
  /** Where focus goes once the dialog has closed on a hand-off. */
  handOffTarget: RefObject<HTMLElement | null>;
}) {
  const ts = useTranslations("settings");
  const fieldId = useId();
  const expectedId = useId();
  const errorId = useId();
  const fieldRef = useRef<HTMLInputElement>(null);
  const [confirmEmail, setConfirmEmail] = useState("");
  const [mismatch, setMismatch] = useState(false);

  function confirm(): string | null {
    const expected = comparableAddress(currentEmail);
    // Fail closed: an absent expected address must never let "" === "" arm an irreversible action.
    if (expected.length === 0 || comparableAddress(confirmEmail) !== expected) {
      setMismatch(true);
      fieldRef.current?.focus();
      return null;
    }
    setMismatch(false);
    return confirmEmail;
  }

  return (
    <ReAuthCodeDialog<never, string>
      trigger={
        <Button type="button" variant="destructive" className="max-md:h-11">
          {ts("account.delete.trigger")}
        </Button>
      }
      title={ts("account.delete.title")}
      description={ts("account.delete.description")}
      currentEmail={currentEmail}
      confirmLabel={ts("account.delete.submit")}
      pendingLabel={ts("account.delete.deleting")}
      cancelLabel={ts("account.delete.cancel")}
      variant="destructive"
      requestFields={
        <div className="flex flex-col gap-1.5">
          <Label htmlFor={fieldId}>{ts("account.delete.confirmEmailLabel")}</Label>
          <Input
            ref={fieldRef}
            id={fieldId}
            type="text"
            inputMode="email"
            autoComplete="off"
            spellCheck={false}
            aria-invalid={mismatch ? true : undefined}
            aria-describedby={mismatch ? `${expectedId} ${errorId}` : expectedId}
            value={confirmEmail}
            onChange={(event) => setConfirmEmail(event.target.value)}
          />
          <p id={expectedId} className="text-body-sm text-text-primary [overflow-wrap:anywhere]">
            {ts("account.delete.expected", { email: currentEmail })}
          </p>
          {mismatch && (
            <p id={errorId} role="alert" className="text-body-sm text-danger-600">
              {ts("account.delete.confirmMismatch")}
            </p>
          )}
        </div>
      }
      onBeforeRequest={confirm}
      codeHint={ts.rich("account.delete.contactRoute", { mail: mailText })}
      terminalExtra={ts.rich("account.delete.contactRoute", { mail: mailLink })}
      action={(proof, confirmed) => deleteAccountAction(confirmed, proof)}
      onHandOff={onHandOff}
      focusAfterHandOff={() => handOffTarget.current?.focus()}
      onOpenChange={(open) => {
        if (!open) {
          setConfirmEmail("");
          setMismatch(false);
        }
      }}
    />
  );
}

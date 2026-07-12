"use client";

// Thin wrapper over the generic <ReAuthDialog> (PR2c-1). It owns only the
// delete-specific bits: the typed confirm-email field (injected via `children`),
// the email-match gate (`canSubmit`), and the action binding. The password field,
// the Dialog shell, RHF/useTransition, the server-error line and reset-on-close
// all live in ReAuthDialog. Behaviour and copy are unchanged from #595.

import { useId, useState } from "react";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ReAuthDialog } from "@/components/forms/reauth-dialog";
import { deleteAccountAction } from "@/lib/actions/me";

interface DeleteAccountDialogProps {
  currentEmail: string;
}

export function DeleteAccountDialog({ currentEmail }: DeleteAccountDialogProps) {
  const ts = useTranslations("settings");
  const confirmEmailId = useId();
  const [confirmEmail, setConfirmEmail] = useState("");

  // Case-insensitive, trimmed match against the signed-in account. This is
  // client-side friction only (GitHub/Stripe typed-confirmation pattern); the
  // Server Action re-checks it against the server-trusted email.
  //
  // #822 — the expected address MUST be non-empty for the gate to arm. While /me
  // returned an empty email, this comparison degenerated to "" === "" and the gate
  // INVERTED: it blocked the user who typed their address correctly and armed on an
  // empty field. The backend fix restores the address; this guard makes the safeguard
  // fail closed rather than open if the expected value is ever absent again — an
  // irreversible action must never lose its confirmation (ASVS V6.2.5).
  const expectedEmail = currentEmail.trim().toLowerCase();
  const emailMatches =
    expectedEmail.length > 0 &&
    confirmEmail.trim().toLowerCase() === expectedEmail;

  return (
    <ReAuthDialog
      trigger={
        <Button type="button" variant="destructive">
          {ts("account.delete.trigger")}
        </Button>
      }
      title={ts("account.delete.title")}
      description={ts("account.delete.description")}
      confirmLabel={ts("account.delete.submit")}
      pendingLabel={ts("account.delete.deleting")}
      cancelLabel={ts("account.delete.cancel")}
      variant="destructive"
      // The password travels with the delete; the server re-authenticates it.
      action={(password) =>
        deleteAccountAction({ confirmEmail, password }, currentEmail)
      }
      canSubmit={() => emailMatches}
      onOpenChange={(open) => {
        if (!open) setConfirmEmail("");
      }}
    >
      <div className="flex flex-col gap-1.5">
        <Label htmlFor={confirmEmailId}>
          {ts("account.delete.confirmEmailLabel")}
        </Label>
        <Input
          id={confirmEmailId}
          type="email"
          autoComplete="off"
          spellCheck={false}
          value={confirmEmail}
          onChange={(event) => setConfirmEmail(event.target.value)}
        />
        <p className="text-body-sm text-text-primary">
          {ts("account.delete.expected", { email: currentEmail })}
        </p>
      </div>
    </ReAuthDialog>
  );
}

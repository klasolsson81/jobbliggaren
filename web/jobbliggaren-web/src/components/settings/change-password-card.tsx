"use client";

// #678 — change-password card. Reuses the generic <ReAuthDialog> (PR2c-1): the
// dialog owns the CURRENT password (its re-auth field, rendered first via
// childrenPosition="after"), the shell, RHF/useTransition, the server-error line
// and reset-on-close. This card owns only the two NEW-password fields (injected via
// `children`), the new === confirm gate (`canSubmit`, client friction only), the
// action binding, and the stay-on-page success confirmation (`onSuccess`).

import { useId, useState } from "react";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { PasswordInput } from "@/components/forms/PasswordInput";
import { ReAuthDialog } from "@/components/forms/reauth-dialog";
import { changePasswordAction } from "@/lib/actions/me";

// Mirrors the backend PasswordRules.MinimumLength / Identity RequiredLength (12).
const NEW_PASSWORD_MIN_LENGTH = 12;

export function ChangePasswordCard() {
  const ts = useTranslations("settings");
  const newPasswordId = useId();
  const newPasswordHintId = useId();
  const confirmPasswordId = useId();
  const confirmFeedbackId = useId();
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [changed, setChanged] = useState(false);

  // Surface WHY submit is gated (a11y): the new password must meet the floor (via the
  // aria-linked hint) and match confirm (the inline message below). Only complain once
  // BOTH fields have content and differ, so we don't nag mid-typing.
  const passwordsMismatch =
    newPassword.length > 0 &&
    confirmPassword.length > 0 &&
    newPassword !== confirmPassword;

  function resetFields() {
    setNewPassword("");
    setConfirmPassword("");
  }

  return (
    <section className="jp-card">
      <h2 className="jp-card__title">{ts("account.changePassword.title")}</h2>
      <p className="text-body-sm text-text-primary">
        {ts("account.changePassword.description")}
      </p>
      {/* Persistent live region: the container is always in the DOM and the text is
          toggled, so a screen reader announces the confirmation reliably (an element
          inserted together with its text can be missed). Empty => zero height. */}
      <p role="status" aria-live="polite" className="text-body-sm text-text-primary">
        {changed ? ts("account.changePassword.success") : ""}
      </p>
      <div className="mt-3">
        <ReAuthDialog
          trigger={
            <Button type="button" variant="secondary">
              {ts("account.changePassword.trigger")}
            </Button>
          }
          title={ts("account.changePassword.title")}
          description={ts("account.changePassword.dialogDescription")}
          confirmLabel={ts("account.changePassword.submit")}
          pendingLabel={ts("account.changePassword.pending")}
          cancelLabel={ts("account.changePassword.cancel")}
          // The re-auth field is the CURRENT password: render it first, label it clearly,
          // and disambiguate its show/hide toggle from the two new-password toggles.
          childrenPosition="after"
          passwordLabel={ts("account.changePassword.currentPasswordLabel")}
          passwordFieldName={ts("account.changePassword.currentPasswordFieldName")}
          // The current password travels with the operation; the server re-authenticates it.
          action={(currentPassword) => changePasswordAction(currentPassword, newPassword)}
          // Client friction only (server authoritative): new meets the floor and matches confirm.
          canSubmit={() =>
            newPassword.length >= NEW_PASSWORD_MIN_LENGTH &&
            newPassword === confirmPassword
          }
          onOpenChange={(open) => {
            if (open) setChanged(false);
            else resetFields();
          }}
          onSuccess={() => {
            resetFields();
            setChanged(true);
          }}
        >
          <div className="flex flex-col gap-1.5">
            <Label htmlFor={newPasswordId}>
              {ts("account.changePassword.newPasswordLabel")}
            </Label>
            <PasswordInput
              id={newPasswordId}
              autoComplete="new-password"
              fieldName={ts("account.changePassword.newPasswordFieldName")}
              aria-describedby={newPasswordHintId}
              value={newPassword}
              onChange={(event) => setNewPassword(event.target.value)}
            />
            <p id={newPasswordHintId} className="text-body-sm text-text-primary">
              {ts("account.changePassword.newPasswordHint")}
            </p>
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor={confirmPasswordId}>
              {ts("account.changePassword.confirmPasswordLabel")}
            </Label>
            <PasswordInput
              id={confirmPasswordId}
              autoComplete="new-password"
              fieldName={ts("account.changePassword.confirmPasswordFieldName")}
              aria-invalid={passwordsMismatch ? true : undefined}
              aria-describedby={confirmFeedbackId}
              value={confirmPassword}
              onChange={(event) => setConfirmPassword(event.target.value)}
            />
            {/* Persistent live region: explains the otherwise-silent submit gate when
                the two new-password entries differ. Empty (zero height) until mismatch. */}
            <p
              id={confirmFeedbackId}
              role="status"
              aria-live="polite"
              className="text-body-sm text-danger-600"
            >
              {passwordsMismatch ? ts("account.changePassword.passwordsDontMatch") : ""}
            </p>
          </div>
        </ReAuthDialog>
      </div>
    </section>
  );
}

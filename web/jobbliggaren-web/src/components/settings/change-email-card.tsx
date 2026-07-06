"use client";

// #679 — change-email card. Mirrors <ChangePasswordCard> (#678): the generic
// <ReAuthDialog> owns the CURRENT password (its re-auth field, rendered first via
// childrenPosition="after"), the shell, RHF/useTransition, the server-error line and
// reset-on-close. This card owns only the single NEW-email field (injected via
// `children`), the valid-and-different submit gate (`canSubmit`, client friction
// only), the action binding, and the stay-on-page confirmation (`onSuccess`).
//
// Unlike change-password there is NO done-state: the address swaps only after the
// emailed link is opened, so the confirmation says a link was SENT, not that the
// email was changed.

import { useId, useState } from "react";
import { useTranslations } from "next-intl";
import { z } from "zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ReAuthDialog } from "@/components/forms/reauth-dialog";
import { changeEmailAction } from "@/lib/actions/me";

// Structural client-side email check (server authoritative). Mirrors the action
// schema's `newEmail` rule (z.email) so the client gate and the server validation
// agree; used only to gate submit, never to render a message.
const emailShape = z.email();

interface ChangeEmailCardProps {
  currentEmail: string;
}

export function ChangeEmailCard({ currentEmail }: ChangeEmailCardProps) {
  const ts = useTranslations("settings");
  const newEmailId = useId();
  const newEmailHintId = useId();
  const sameEmailFeedbackId = useId();
  const [newEmail, setNewEmail] = useState("");
  const [sent, setSent] = useState(false);

  function resetFields() {
    setNewEmail("");
  }

  // Surface WHY submit is gated (a11y): when the entered address matches the current
  // one it is a valid email but the same account, so the same-different gate below
  // silently blocks submit. Only complain once the field has content, so we don't nag
  // mid-typing. Mirrors ChangePasswordCard's confirm-mismatch region.
  const isSameEmail =
    newEmail.trim().length > 0 &&
    newEmail.trim().toLowerCase() === currentEmail.trim().toLowerCase();

  // Client friction only (server authoritative): the new address is a valid email
  // AND differs from the current one (case-insensitive, trimmed). The same-address
  // guard keeps the backend's 409 backstop from being the first line of defense.
  function isValidDifferentEmail() {
    const trimmed = newEmail.trim();
    if (!emailShape.safeParse(trimmed).success) return false;
    return trimmed.toLowerCase() !== currentEmail.trim().toLowerCase();
  }

  return (
    <section className="jp-card">
      <h2 className="jp-card__title">{ts("account.changeEmail.title")}</h2>
      <p className="text-body-sm text-text-primary">
        {ts("account.changeEmail.description")}
      </p>
      {/* Persistent live region: the container is always in the DOM and the text is
          toggled, so a screen reader announces the "link sent" confirmation reliably
          (an element inserted together with its text can be missed). Empty => zero
          height. */}
      <p role="status" aria-live="polite" className="text-body-sm text-text-primary">
        {sent ? ts("account.changeEmail.success") : ""}
      </p>
      <div className="mt-3">
        <ReAuthDialog
          trigger={
            <Button type="button" variant="secondary">
              {ts("account.changeEmail.trigger")}
            </Button>
          }
          title={ts("account.changeEmail.title")}
          description={ts("account.changeEmail.dialogDescription")}
          confirmLabel={ts("account.changeEmail.submit")}
          pendingLabel={ts("account.changeEmail.pending")}
          cancelLabel={ts("account.changeEmail.cancel")}
          // The re-auth field is the CURRENT password: render it first, label it
          // clearly, and name its show/hide toggle (the injected field is an email,
          // not a password, so its toggle needs no disambiguation).
          childrenPosition="after"
          passwordLabel={ts("account.changeEmail.currentPasswordLabel")}
          passwordFieldName={ts("account.changeEmail.currentPasswordFieldName")}
          // The current password travels with the operation; the server re-authenticates it.
          action={(currentPassword) => changeEmailAction(currentPassword, newEmail)}
          // Client friction only (server authoritative): valid new email AND different.
          canSubmit={() => isValidDifferentEmail()}
          onOpenChange={(open) => {
            if (open) setSent(false);
            else resetFields();
          }}
          onSuccess={() => {
            resetFields();
            setSent(true);
          }}
        >
          <div className="flex flex-col gap-1.5">
            <Label htmlFor={newEmailId}>
              {ts("account.changeEmail.newEmailLabel")}
            </Label>
            <Input
              id={newEmailId}
              type="email"
              autoComplete="email"
              aria-required="true"
              aria-invalid={isSameEmail ? true : undefined}
              aria-describedby={`${newEmailHintId} ${sameEmailFeedbackId}`}
              value={newEmail}
              onChange={(event) => setNewEmail(event.target.value)}
            />
            <p id={newEmailHintId} className="text-body-sm text-text-primary">
              {ts("account.changeEmail.newEmailHint")}
            </p>
            {/* Persistent live region: explains the otherwise-silent submit gate when
                the new address equals the current one. Empty (zero height) otherwise. */}
            <p
              id={sameEmailFeedbackId}
              role="status"
              aria-live="polite"
              className="text-body-sm text-danger-600"
            >
              {isSameEmail ? ts("account.changeEmail.sameEmail") : ""}
            </p>
          </div>
        </ReAuthDialog>
      </div>
    </section>
  );
}

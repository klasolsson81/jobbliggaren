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

import { useEffect, useId, useRef, useState } from "react";
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
  // #734 B-ii: the backend refused up front because no configured sender can deliver
  // (503 + Auth.EmailDeliveryUnavailable). Its own state rather than the dialog's error
  // line, because no retry can succeed until an operator sets a real Email:Provider.
  const [refusedMessage, setRefusedMessage] = useState<string | null>(null);
  const refusedRef = useRef<HTMLDivElement>(null);

  // Focus management (not data fetching): submitting closes the dialog, so the focused
  // element leaves the DOM and focus falls to <body>. And role="status" announces CHANGES
  // to a live region that already exists; this one mounts already filled, which NVDA and
  // JAWS routinely miss (WCAG 4.1.3). The focus move is what actually delivers the message
  // — the same reason every sibling auth panel does it (RegisterForm).
  useEffect(() => {
    if (refusedMessage) refusedRef.current?.focus();
  }, [refusedMessage]);

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

  // Delivery is refused for the whole deployment, so the change-email flow is rendered in
  // place of itself: no trigger, no dialog, nothing to submit. Mirrors RegisterForm's
  // registrations-closed panel, and for the same reason — a live control that cannot
  // succeed invites a retry and reads as a fault the user could fix.
  //
  // The promise copy (`description`) is deliberately NOT rendered here. Its string is
  // untouched in messages/, so release-checklist.md §2.6 point 5.5 condition (a) is
  // unaffected — this is one conditional state, not a softening of the published claim,
  // and publishing "Vi skickar en bekräftelselänk" directly above its own denial would
  // contradict the panel.
  if (refusedMessage) {
    return (
      <section className="jp-card">
        {/* Focus lands on the wrapper, not the message, so the heading is read with it:
            /installningar renders nine cards and the message alone gives a screen-reader
            user no anchor to which one it belongs to. role="status" stays on the <p>
            alone — nested live regions double-announce. No focus:outline-none here: the
            global *:focus-visible rule paints a token-borne ring, which a keyboard user
            needs in order to see where focus went. */}
        <div ref={refusedRef} tabIndex={-1}>
          <h2 className="jp-card__title">{ts("account.changeEmail.title")}</h2>
          <p
            role="status"
            aria-live="polite"
            className="text-body-sm text-text-primary"
          >
            {refusedMessage}
          </p>
        </div>
      </section>
    );
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
          // The dialog closes and hands the message over; the card then renders itself as
          // the panel above. The action already resolved the copy from messages/ — the
          // backend `detail` is never rendered (see changeEmailAction's 503 arm).
          onRefused={(message) => {
            resetFields();
            setSent(false);
            setRefusedMessage(message);
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

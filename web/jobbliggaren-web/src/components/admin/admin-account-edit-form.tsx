"use client";

// "use client": React Hook Form owns the new address, and the press that opens the step-up dialog is checked here.
import { type MouseEvent, useEffect, useRef, useState } from "react";
import { useForm } from "react-hook-form";
import { useTranslations } from "next-intl";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ReAuthCodeDialog, type ReauthHandOff } from "@/components/forms/reauth-code-dialog";
import type { AdminEmailChangeRequestOutcome, AdminPendingEmailChange } from "@/lib/admin/account-email-change";
import type { AdminAddressedAccount } from "@/lib/admin/view-models";
import { checkNewAddress } from "@/lib/auth/new-address";
import type { CodeProof, ReauthRequestResult } from "@/lib/auth/reauth-action-state";
import { AdminRolePill } from "./admin-account-status";

export const ADMIN_NEW_EMAIL_FIELD_ID = "admin-account-new-email";

/**
 * How a request ended when the panel, not the form, shows what came of it. The pending change itself reaches the
 * panel from its caller, which keeps the server's answer.
 */
export type AdminEditExit =
  | {
      readonly kind: "requested";
      readonly newEmail: string;
      readonly completableFrom: AdminPendingEmailChange["completableFrom"];
    }
  /** A message about the account rather than the address typed: the panel shows it beside the account's facts. */
  | { readonly kind: "notice"; readonly message: string }
  | { readonly kind: "gone" };

interface AdminAccountEditFormProps {
  readonly account: AdminAddressedAccount;
  /** The administrator's own address: the step-up code goes there, never to the account's. */
  readonly selfEmail: string;
  /** Asks for the administrator's step-up code. */
  readonly requestCode: () => Promise<ReauthRequestResult>;
  /** Verifies the administrator's code and requests the change, in one Server Action. */
  readonly request: (newEmail: string, proof: CodeProof) => Promise<AdminEmailChangeRequestOutcome>;
  /** The page a lapsed session returns to after logging in again. */
  readonly returnPath: string;
  /** A request that ended where the panel shows it: the form is left. */
  readonly onExit: (exit: AdminEditExit) => void;
  /** Moves focus to what the panel shows once the dialog has closed on an exit. */
  readonly focusAfterExit: () => void;
  readonly onCancel: () => void;
}

type FocusTarget = "field" | "status" | "exit";

/**
 * The panel's edit mode (#1975, ADR 0153): a request to change the account's address, never the change itself, which
 * happens when the owner uses the code on the public page. The role is shown, not edited (ADR 0150 D8).
 *
 * "Fortsätt" checks the address before the step-up dialog opens, in the order of the self-service setting, so a
 * refusal keeps the dialog closed and spends no code. A refusal about the address typed stays here with what was
 * typed; a request that went through, an unknown outcome and a refusal about the account itself leave the form.
 */
export function AdminAccountEditForm({
  account,
  selfEmail,
  requestCode,
  request,
  returnPath,
  onExit,
  focusAfterExit,
  onCancel,
}: AdminAccountEditFormProps) {
  const t = useTranslations("admin.users");
  const tp = useTranslations("pages");
  const {
    register,
    getValues,
    setError,
    clearErrors,
    formState: { errors },
  } = useForm<{ newEmail: string }>({ defaultValues: { newEmail: "" } });

  // The address "Fortsätt" last admitted: the dialog names it and the request sends it.
  const [pendingAddress, setPendingAddress] = useState("");
  const [status, setStatus] = useState<string | null>(null);
  const pendingFocus = useRef<FocusTarget | null>(null);
  const handOffFocus = useRef<FocusTarget | null>(null);
  const after = useRef<AdminEmailChangeRequestOutcome["after"]>(undefined);
  const statusRef = useRef<HTMLParagraphElement>(null);

  const hintId = `${ADMIN_NEW_EMAIL_FIELD_ID}-hint`;
  const errorId = `${ADMIN_NEW_EMAIL_FIELD_ID}-error`;
  const fieldError = errors.newEmail?.message;

  function focus(target: FocusTarget) {
    if (target === "exit") focusAfterExit();
    else if (target === "field") document.getElementById(ADMIN_NEW_EMAIL_FIELD_ID)?.focus({ focusVisible: true });
    else statusRef.current?.focus();
  }

  // Focus follows the message it belongs to, once that message is on screen. A hand-off from the dialog is
  // focused from the dialog's close instead, after its focus trap has let go.
  useEffect(() => {
    const target = pendingFocus.current;
    if (target === null) return;
    pendingFocus.current = null;
    focus(target);
  });

  function onContinue(event: MouseEvent<HTMLButtonElement>) {
    setStatus(null);
    const verdict = checkNewAddress(getValues("newEmail"), account.email);
    if (!verdict.ok) {
      event.preventDefault(); // the dialog stays closed, and no code is asked for
      setError("newEmail", { type: "manual", message: t(`edit.refusal.${verdict.reason}`) });
      pendingFocus.current = "field";
      return;
    }
    clearErrors("newEmail");
    setPendingAddress(verdict.address);
  }

  async function send(proof: CodeProof) {
    const outcome = await request(pendingAddress, proof);
    after.current = outcome.after;
    return outcome;
  }

  function onHandOff(handOff: ReauthHandOff<AdminPendingEmailChange>) {
    const next = after.current;
    after.current = undefined;
    switch (handOff.kind) {
      case "verified":
        handOffFocus.current = "exit";
        onExit({ kind: "requested", newEmail: pendingAddress, completableFrom: handOff.value.completableFrom });
        return;
      case "outcomeUnknown":
        handOffFocus.current = "exit";
        onExit({ kind: "notice", message: handOff.error });
        return;
      case "operationRefused":
        if (next === "gone" || next === "changed") {
          handOffFocus.current = "exit";
          onExit(next === "gone" ? { kind: "gone" } : { kind: "notice", message: handOff.error });
          return;
        }
        if (handOff.channel === "field") {
          setError("newEmail", { type: "manual", message: handOff.error });
          handOffFocus.current = "field";
          return;
        }
        setStatus(handOff.error);
        handOffFocus.current = "status";
        return;
      case "refused":
        // No mail can be sent: before any code, the dialog hands this over without words of its own.
        setStatus(handOff.error ?? t("emailChange.deliveryUnavailable"));
        handOffFocus.current = "status";
        return;
    }
  }

  function focusAfterHandOff() {
    const target = handOffFocus.current;
    handOffFocus.current = null;
    if (target !== null) focus(target);
  }

  return (
    // Enter in the field presses "Fortsätt", the form's one submit; the form itself sends nothing.
    <form className="jp-admineditform" onSubmit={(event) => event.preventDefault()} noValidate>
      <h3 className="jp-admineditform__heading">{t("edit.heading")}</h3>
      <dl className="jp-admindl">
        <dt>{t("edit.current")}</dt>
        <dd>{account.email}</dd>
        <dt>{t("edit.role")}</dt>
        <dd>
          <AdminRolePill role={account.role} />
        </dd>
      </dl>
      <div className="jp-admineditform__field">
        <Label htmlFor={ADMIN_NEW_EMAIL_FIELD_ID}>{t("edit.newEmail")}</Label>
        <Input
          id={ADMIN_NEW_EMAIL_FIELD_ID}
          type="email"
          autoComplete="off"
          spellCheck={false}
          aria-required="true"
          aria-invalid={fieldError === undefined ? undefined : true}
          aria-describedby={fieldError === undefined ? hintId : `${hintId} ${errorId}`}
          {...register("newEmail")}
        />
        <p id={hintId} className="jp-admineditform__hint">
          {t("edit.hint")}
        </p>
        {fieldError === undefined ? null : (
          <p id={errorId} role="alert" className="jp-admineditform__refusal">
            {fieldError}
          </p>
        )}
      </div>
      {status === null ? null : (
        <p ref={statusRef} tabIndex={-1} role="status" className="jp-admineditform__status">
          {status}
        </p>
      )}
      <div className="jp-admineditform__actions">
        <ReAuthCodeDialog<AdminPendingEmailChange>
          trigger={
            <button type="submit" className="jp-btn jp-btn--primary" onClick={onContinue}>
              {t("edit.submit")}
            </button>
          }
          // Layered above the panel by the admin stylesheet, as the confirmation is.
          className="jp-adminstepup"
          title={t("edit.heading")}
          description={t("edit.dialogDescription", { newEmail: pendingAddress, currentEmail: account.email })}
          currentEmail={selfEmail}
          confirmLabel={tp("auth.passwordless.code.submit")}
          pendingLabel={tp("auth.passwordless.code.submitting")}
          cancelLabel={t("edit.cancel")}
          returnPath={returnPath}
          requestCode={requestCode}
          action={send}
          onHandOff={onHandOff}
          focusAfterHandOff={focusAfterHandOff}
        />
        <button type="button" className="jp-btn jp-btn--ghost" onClick={onCancel}>
          {t("edit.cancel")}
        </button>
      </div>
    </form>
  );
}

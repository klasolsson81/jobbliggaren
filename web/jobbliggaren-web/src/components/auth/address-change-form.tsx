"use client";

import { type FormEvent, type ReactNode, useActionState, useEffect, useId, useRef, useState } from "react";
import Link from "next/link";
import { useFormatter, useTranslations } from "next-intl";
import { LoginFormMessage } from "@/components/auth/login-form-message";
import { mailLink, STANDALONE_LINK } from "@/components/auth/mail-link";
import { CodeField } from "@/components/forms/code-field";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  checkAddressChange,
  type AddressChangeField,
  type AddressChangeFieldErrors,
  type AddressChangeFieldRefusal,
  type AddressChangeState,
} from "@/lib/auth/address-change";
import { completeAddressChange } from "@/lib/auth/address-change-actions";
import { CODE_MAX_ATTEMPTS } from "@/lib/auth/code-format";
import { LOGIN_ENTRY_PATH } from "@/lib/auth/login-paths";
import { formatLedgerDate, formatTime } from "@/lib/i18n/format";

// Client because it holds the action's state (`useActionState`), checks the fields before a send and moves focus when
// an answer arrives. Without JavaScript the same `<form action>` posts to the same Server Action, which runs the same
// check and answers the same states (design-reviewer, #1975 R1).
//
// The page cannot tell which input a refusal was about, and must not (ADR 0153): the refusal is one message for the
// form and marks no field invalid. The addresses are re-seeded from the action's echo, because React resets an
// uncontrolled form after an action; the code is never echoed, so it is typed again.

const FIELDS: ReadonlyArray<AddressChangeField> = ["currentEmail", "newEmail", "code"];

const INPUT_IDS = {
  currentEmail: "address-change-current",
  newEmail: "address-change-new",
  code: "address-change-code",
} as const satisfies Record<AddressChangeField, string>;

const HINT_IDS = {
  currentEmail: "address-change-current-hint",
  newEmail: "address-change-new-hint",
  code: "address-change-code-hint",
} as const satisfies Record<AddressChangeField, string>;

const ERROR_IDS = {
  currentEmail: "address-change-current-error",
  newEmail: "address-change-new-error",
  code: "address-change-code-error",
} as const satisfies Record<AddressChangeField, string>;

const NO_ERRORS: AddressChangeFieldErrors = {};

/** Moves focus to the first field, in the order they stand, that carries an error. */
function focusFirstInvalid(fieldErrors: AddressChangeFieldErrors) {
  const first = FIELDS.find((field) => fieldErrors[field] !== undefined);
  if (first !== undefined) document.getElementById(INPUT_IDS[first])?.focus();
}

/** A field's own error, under it and wired to it: something the user can correct. */
function FieldError({ field, children }: { readonly field: AddressChangeField; readonly children: ReactNode }) {
  return (
    <p id={ERROR_IDS[field]} role="alert" className="text-body-sm leading-5 text-danger-600 [overflow-wrap:anywhere]">
      {children}
    </p>
  );
}

export function AddressChangeForm() {
  const t = useTranslations("pages");
  const format = useFormatter();
  const [state, formAction, isPending] = useActionState<AddressChangeState, FormData>(completeAddressChange, null);
  // The browser's own check: a refusal here keeps the form unsent, so the action never sees it.
  const [checked, setChecked] = useState<AddressChangeFieldErrors | null>(null);
  const messageRef = useRef<HTMLParagraphElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  const messageId = useId();
  const loggedOutId = useId();

  // What the last answer said stays until the next press: the browser's own refusal or a send replaces it.
  const answer = checked === null && !isPending ? state : null;
  const errors = checked ?? (answer?.kind === "invalid" ? answer.errors : NO_ERRORS);
  const values = state !== null && state.kind !== "done" ? state.values : null;

  // Focus follows the answer once it is on screen.
  useEffect(() => {
    if (state === null) return;
    if (state.kind === "done") panelRef.current?.focus();
    else if (state.kind === "invalid") focusFirstInvalid(state.errors);
    else messageRef.current?.focus();
  }, [state]);

  useEffect(() => {
    if (checked !== null) focusFirstInvalid(checked);
  }, [checked]);

  function onSubmit(event: FormEvent<HTMLFormElement>) {
    const data = new FormData(event.currentTarget);
    const read = (field: AddressChangeField) => {
      const value = data.get(field);
      return typeof value === "string" ? value : "";
    };
    const verdict = checkAddressChange({
      currentEmail: read("currentEmail"),
      newEmail: read("newEmail"),
      code: read("code"),
    });
    if (!verdict.ok) {
      event.preventDefault(); // React runs no action for a submit that was prevented
      setChecked(verdict.errors);
      return;
    }
    setChecked(null);
  }

  function errorText(field: AddressChangeField, refusal: AddressChangeFieldRefusal): string {
    if (field === "code") {
      return refusal === "required"
        ? t("auth.addressChange.errors.codeRequired")
        : t("auth.passwordless.code.malformedCode");
    }
    switch (refusal) {
      case "required":
        return field === "currentEmail"
          ? t("auth.addressChange.errors.currentRequired")
          : t("auth.addressChange.errors.newRequired");
      case "same":
        return t("auth.addressChange.errors.same");
      default:
        return t("auth.passwordless.entry.emailInvalid");
    }
  }

  function describedBy(field: AddressChangeField): string {
    return errors[field] === undefined ? HINT_IDS[field] : `${HINT_IDS[field]} ${ERROR_IDS[field]}`;
  }

  function message(): ReactNode {
    switch (answer?.kind) {
      case "refused":
        // One message, whatever the cause: no field is marked, and focus moves here.
        return (
          <p
            ref={messageRef}
            id={messageId}
            tabIndex={-1}
            role="alert"
            className="text-body-sm leading-5 text-danger-600 [overflow-wrap:anywhere]"
          >
            {t.rich("auth.addressChange.refused", { attempts: CODE_MAX_ATTEMPTS, mail: mailLink })}
          </p>
        );
      case "notYet":
        return (
          <LoginFormMessage
            id={messageId}
            channel="status"
            statusRef={messageRef}
            message={t("auth.addressChange.notYet", {
              date: formatLedgerDate(format, answer.completableFrom) ?? answer.completableFrom,
              time: formatTime(format, new Date(answer.completableFrom)),
            })}
          />
        );
      case "tooManyAttempts":
        return (
          <LoginFormMessage
            id={messageId}
            channel="status"
            statusRef={messageRef}
            message={t("auth.passwordless.errors.tooManyAttempts")}
          />
        );
      case "unavailable":
        return (
          <LoginFormMessage
            id={messageId}
            channel="status"
            statusRef={messageRef}
            message={t("auth.addressChange.unavailable")}
          />
        );
      case "unknown":
        // It may have happened, so it claims nothing and advises no retry: a retry over a change that went through
        // meets the refusal.
        return (
          <p
            ref={messageRef}
            id={messageId}
            tabIndex={-1}
            role="status"
            aria-live="polite"
            className="text-body-sm leading-5 text-text-primary [overflow-wrap:anywhere]"
          >
            {t.rich("auth.addressChange.unknown", { mail: mailLink })}
          </p>
        );
      default:
        return null;
    }
  }

  if (state?.kind === "done") {
    // The panel replaces the form; it mounts filled, so the focus move is what announces it.
    return (
      <div className="flex flex-col gap-4">
        <div ref={panelRef} tabIndex={-1} role="status" aria-live="polite" className="flex flex-col gap-1">
          <h2 className="text-body font-bold text-heading-1">{t("auth.addressChange.done.title")}</h2>
          <p className="text-body text-text-primary">{t("auth.addressChange.done.body")}</p>
        </div>
        {/* A sibling of the live region, never inside it. No address travels in the link. */}
        <p className="text-body-sm text-text-primary">
          <Link href={LOGIN_ENTRY_PATH} className={STANDALONE_LINK}>
            {t("auth.addressChange.done.login")}
          </Link>
        </p>
      </div>
    );
  }

  return (
    <form action={formAction} onSubmit={onSubmit} noValidate className="flex flex-col gap-5">
      <div className="flex flex-col gap-1.5">
        <label htmlFor={INPUT_IDS.currentEmail} className="text-label font-medium text-text-primary">
          {t("auth.addressChange.currentEmailLabel")}
        </label>
        <Input
          id={INPUT_IDS.currentEmail}
          name="currentEmail"
          type="email"
          autoComplete="username"
          spellCheck={false}
          autoCapitalize="none"
          required
          aria-required="true"
          aria-invalid={errors.currentEmail === undefined ? undefined : true}
          aria-describedby={describedBy("currentEmail")}
          defaultValue={values?.currentEmail ?? ""}
        />
        <p id={HINT_IDS.currentEmail} className="text-body-sm text-text-primary">
          {t("auth.addressChange.currentEmailHint")}
        </p>
        {errors.currentEmail === undefined ? null : (
          <FieldError field="currentEmail">{errorText("currentEmail", errors.currentEmail)}</FieldError>
        )}
      </div>

      <div className="flex flex-col gap-1.5">
        <label htmlFor={INPUT_IDS.newEmail} className="text-label font-medium text-text-primary">
          {t("auth.addressChange.newEmailLabel")}
        </label>
        <Input
          id={INPUT_IDS.newEmail}
          name="newEmail"
          type="email"
          autoComplete="email"
          spellCheck={false}
          autoCapitalize="none"
          required
          aria-required="true"
          aria-invalid={errors.newEmail === undefined ? undefined : true}
          aria-describedby={describedBy("newEmail")}
          defaultValue={values?.newEmail ?? ""}
        />
        <p id={HINT_IDS.newEmail} className="text-body-sm text-text-primary">
          {t("auth.addressChange.newEmailHint")}
        </p>
        {errors.newEmail === undefined ? null : (
          <FieldError field="newEmail">{errorText("newEmail", errors.newEmail)}</FieldError>
        )}
      </div>

      <div className="flex flex-col gap-1.5">
        <CodeField
          id={INPUT_IDS.code}
          hintId={HINT_IDS.code}
          label={t("auth.passwordless.code.codeLabel")}
          hint={t("auth.passwordless.code.codeHint")}
          invalid={errors.code !== undefined}
          errorId={ERROR_IDS.code}
          name="code"
        />
        {errors.code === undefined ? null : <FieldError field="code">{errorText("code", errors.code)}</FieldError>}
      </div>

      {message()}

      <p id={loggedOutId} className="text-body-sm text-text-primary">
        {t("auth.addressChange.loggedOut")}
      </p>

      <Button type="submit" disabled={isPending} aria-describedby={loggedOutId} className="w-full max-md:h-11">
        {isPending ? t("auth.addressChange.submitting") : t("auth.addressChange.submit")}
      </Button>
    </form>
  );
}

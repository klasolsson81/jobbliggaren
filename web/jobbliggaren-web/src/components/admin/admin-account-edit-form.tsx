"use client";

import { useEffect, useMemo, useRef, useState, useTransition } from "react";
import { useForm, type FieldErrors } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useTranslations } from "next-intl";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { checkNewAddress, type NewAddressRefusal } from "@/lib/auth/new-address";
import type { AdminAccountDetail } from "@/lib/admin/view-models";
import { AdminRolePill } from "./admin-account-status";

export const ADMIN_NEW_EMAIL_FIELD_ID = "admin-account-new-email";

// The address rule is the one self-service change-email runs before a code is spent: it refuses at
// least what the backend refuses and never an address the backend would take.
function makeSchema(currentEmail: string, refusal: (reason: NewAddressRefusal) => string) {
  return z.object({
    newEmail: z.string().superRefine((value, context) => {
      const verdict = checkNewAddress(value, currentEmail);
      if (!verdict.ok) context.addIssue({ code: "custom", message: refusal(verdict.reason) });
    }),
  });
}

type FormValues = z.input<ReturnType<typeof makeSchema>>;

interface AdminAccountEditFormProps {
  readonly account: AdminAccountDetail;
  /** Resolves to a refusal to show under the form, or null when the request went through. */
  readonly onSubmit: (newEmail: string) => Promise<string | null>;
  readonly onCancel: () => void;
}

/**
 * The panel's edit mode: a request to change the account's address (#1975), never the change
 * itself, which happens when the owner confirms. The role is shown, not edited (ADR 0150 D8).
 * React Hook Form owns the value and the refusal; a failed request keeps what was typed.
 */
export function AdminAccountEditForm({ account, onSubmit, onCancel }: AdminAccountEditFormProps) {
  const t = useTranslations("admin.users.edit");
  const schema = useMemo(
    () => makeSchema(account.email, (reason) => t(`refusal.${reason}`)),
    [account.email, t],
  );
  const rootRef = useRef<HTMLParagraphElement>(null);
  const [pending, startTransition] = useTransition();
  const [refused, setRefused] = useState(false);

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema, undefined, { raw: true }),
    mode: "onSubmit",
    reValidateMode: "onChange",
    shouldFocusError: false,
    defaultValues: { newEmail: "" },
  });

  useEffect(() => {
    if (errors.root !== undefined) rootRef.current?.focus();
  }, [errors.root]);

  const invalid = refused && errors.newEmail !== undefined;
  const hintId = `${ADMIN_NEW_EMAIL_FIELD_ID}-hint`;
  const errorId = `${ADMIN_NEW_EMAIL_FIELD_ID}-error`;

  function submit(values: FormValues) {
    setRefused(false);
    const verdict = checkNewAddress(values.newEmail, account.email);
    if (!verdict.ok) return;
    startTransition(async () => {
      const refusal = await onSubmit(verdict.address);
      if (refusal !== null) setError("root", { message: refusal });
    });
  }

  function refuse(fieldErrors: FieldErrors<FormValues>) {
    setRefused(fieldErrors.newEmail !== undefined);
    document.getElementById(ADMIN_NEW_EMAIL_FIELD_ID)?.focus({ focusVisible: true });
  }

  return (
    <form className="jp-admineditform" onSubmit={handleSubmit(submit, refuse)} noValidate>
      <h3 className="jp-admineditform__heading">{t("heading")}</h3>
      <dl className="jp-admindl">
        <dt>{t("current")}</dt>
        <dd>{account.email}</dd>
        <dt>{t("role")}</dt>
        <dd>
          <AdminRolePill role={account.role} />
        </dd>
      </dl>
      <div className="jp-admineditform__field">
        <Label htmlFor={ADMIN_NEW_EMAIL_FIELD_ID}>{t("newEmail")}</Label>
        <Input
          id={ADMIN_NEW_EMAIL_FIELD_ID}
          type="email"
          autoComplete="off"
          disabled={pending}
          aria-invalid={invalid || undefined}
          aria-describedby={invalid ? `${hintId} ${errorId}` : hintId}
          {...register("newEmail")}
        />
        <p id={hintId} className="jp-admineditform__hint">
          {t("hint")}
        </p>
        {invalid ? (
          <p id={errorId} role="alert" className="jp-admineditform__refusal">
            {errors.newEmail?.message}
          </p>
        ) : null}
      </div>
      {errors.root === undefined ? null : (
        <p ref={rootRef} tabIndex={-1} role="alert" className="jp-admineditform__refusal">
          {errors.root.message}
        </p>
      )}
      <div className="jp-admineditform__actions">
        <button type="submit" className="jp-btn jp-btn--primary" disabled={pending}>
          {t("submit")}
        </button>
        <button type="button" className="jp-btn jp-btn--ghost" disabled={pending} onClick={onCancel}>
          {t("cancel")}
        </button>
      </div>
    </form>
  );
}

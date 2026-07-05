"use client";

// PR2c-1 (persistent-login re-auth, epic #481) — reusable server-enforced
// re-auth dialog. Extracted from the inline pattern in
// `me/delete-account-dialog.tsx` so the upcoming change-email / change-password /
// data-export flows reuse one shell. It owns a Dialog, exactly ONE password
// field (the shared <PasswordInput>), RHF with a manual `schema.safeParse`,
// `useTransition` (the pending state locks every control), a `role="alert"`
// server-error line wired via `aria-describedby`, and reset-on-close.
//
// Re-auth is enforced server-side: the operation carries the password and the
// backend verifies it (no separate `/auth/verify` pre-call). The consumer
// supplies the operation-specific copy + the Server Action; the password is
// handed to that action on submit. Operation-specific extra fields (delete's
// confirm-email) are injected via `children` and gated via `canSubmit`, so this
// component stays generic.

import {
  type ReactNode,
  useId,
  useMemo,
  useState,
  useTransition,
} from "react";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import { Label } from "@/components/ui/label";
import { PasswordInput } from "@/components/forms/PasswordInput";
import type { ActionResult } from "@/lib/actions/_action-result";

export interface ReAuthDialogProps {
  /** The element that opens the dialog (rendered via `DialogTrigger asChild`). */
  trigger: ReactNode;
  title: string;
  description: string;
  /** Submit-button label — verb + object, e.g. "Radera mitt konto". */
  confirmLabel: string;
  /** Submit-button label while the action runs, e.g. "Raderar…". */
  pendingLabel: string;
  cancelLabel: string;
  /** Visual weight of the submit button. `destructive` for delete-style ops. */
  variant?: "default" | "destructive";
  /**
   * The Server Action the password travels with. Re-auth is server-side: the
   * operation carries the password and the backend verifies it. Returns
   * `{ success: false, error }` on failure; on success the action redirects, so
   * control never resolves back here.
   */
  action: (password: string) => Promise<ActionResult>;
  /** Extra fields rendered next to the password (e.g. delete's confirm-email). */
  children?: ReactNode;
  /**
   * Where `children` render relative to the password field. `"before"` (default)
   * keeps delete's confirm-email-then-password order; `"after"` puts the password
   * first so change-password reads current -> new -> confirm.
   */
  childrenPosition?: "before" | "after";
  /**
   * Overrides the password field label. Defaults to the shared "Lösenord"; a
   * consumer whose re-auth field is the *current* password (change-password)
   * passes "Nuvarande lösenord" for clarity.
   */
  passwordLabel?: string;
  /**
   * Disambiguates the password field's show/hide toggle accessible name when the
   * form has multiple password fields (change-password injects new + confirm via
   * `children`). Passed to <PasswordInput fieldName>. Omitted → the bare toggle
   * label (unchanged for delete, which has one password field).
   */
  passwordFieldName?: string;
  /**
   * Extra client-side submit gating on top of "password is non-empty" (e.g.
   * delete's email-match). Receives the current password. Server-side
   * validation stays authoritative — this is UX friction only.
   */
  canSubmit?: (password: string) => boolean;
  /**
   * Notified when the dialog opens (`true`) / closes (`false`). Lets a consumer
   * reset its own injected-field state on close; the password field and the
   * server error are reset here automatically.
   */
  onOpenChange?: (open: boolean) => void;
  /**
   * Invoked when the action resolves `{ success: true }` — the STAY-ON-PAGE path
   * (change-password). ReAuthDialog resets its password field, clears the error,
   * and closes before calling this, so the consumer just clears its own injected
   * state and surfaces a confirmation. When omitted, the legacy contract holds:
   * the action is expected to redirect on success (delete), so control never
   * returns here and nothing is called.
   */
  onSuccess?: () => void;
}

interface ReAuthFormValues {
  password: string;
}

export function ReAuthDialog({
  trigger,
  title,
  description,
  confirmLabel,
  pendingLabel,
  cancelLabel,
  variant = "default",
  action,
  children,
  childrenPosition = "before",
  passwordLabel,
  passwordFieldName,
  canSubmit,
  onOpenChange,
  onSuccess,
}: ReAuthDialogProps) {
  const ts = useTranslations("settings");
  const tv = useTranslations("validation");
  // Manual schema (parsed in onSubmit, not via a resolver) — mirrors the
  // delete-dialog / auth-form convention in this codebase.
  const schema = useMemo(
    () => z.object({ password: z.string().min(1, tv("profile.passwordRequired")) }),
    [tv],
  );

  const [open, setOpen] = useState(false);
  const [isPending, startTransition] = useTransition();
  const [serverError, setServerError] = useState<string | null>(null);

  const errorId = useId();
  const passwordId = useId();

  const { register, handleSubmit, watch, reset } = useForm<ReAuthFormValues>({
    defaultValues: { password: "" },
    shouldUnregister: false,
  });

  const password = watch("password");
  // Local activation guard (UX only — the server re-auth is authoritative):
  // a non-empty password plus any consumer gate (delete's email-match).
  const isSubmittable =
    !isPending && !!password && (canSubmit ? canSubmit(password) : true);

  function handleOpenChange(next: boolean) {
    if (isPending) return; // never close mid-operation
    setOpen(next);
    if (!next) {
      reset();
      setServerError(null);
    }
    onOpenChange?.(next);
  }

  function onSubmit(values: ReAuthFormValues) {
    const parsed = schema.safeParse(values);
    if (!parsed.success) {
      setServerError(
        parsed.error.issues[0]?.message ?? ts("account.errors.invalidInput"),
      );
      return;
    }

    setServerError(null);
    startTransition(async () => {
      const result = await action(parsed.data.password);
      if (!result.success) {
        setServerError(result.error);
        return;
      }
      // Stay-on-page success (e.g. change-password): reset, close, and notify the
      // consumer so it can clear its injected fields and show a confirmation. Without
      // onSuccess the legacy contract holds — the action redirected and control never
      // reaches here (delete).
      if (onSuccess) {
        reset();
        setServerError(null);
        setOpen(false);
        onSuccess();
      }
    });
  }

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogTrigger asChild>{trigger}</DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription>{description}</DialogDescription>
        </DialogHeader>
        <form
          onSubmit={handleSubmit(onSubmit)}
          className="flex flex-col gap-4"
          noValidate
        >
          {/* A disabled fieldset locks every control inside (the injected extra
              fields, the password input, and its show/hide toggle) in one place
              while the action runs. */}
          <fieldset
            disabled={isPending}
            className="m-0 flex min-w-0 flex-col gap-4 border-0 p-0"
          >
            {childrenPosition === "before" && children}
            <div className="flex flex-col gap-1.5">
              <Label htmlFor={passwordId}>
                {passwordLabel ?? ts("account.reauth.passwordLabel")}
              </Label>
              <PasswordInput
                id={passwordId}
                autoComplete="current-password"
                fieldName={passwordFieldName}
                aria-invalid={serverError ? true : undefined}
                aria-describedby={serverError ? errorId : undefined}
                {...register("password")}
              />
            </div>
            {childrenPosition === "after" && children}
          </fieldset>
          {serverError && (
            <p
              id={errorId}
              role="alert"
              className="text-body-sm text-danger-600"
            >
              {serverError}
            </p>
          )}
          <DialogFooter>
            <Button
              type="button"
              variant="ghost"
              disabled={isPending}
              onClick={() => handleOpenChange(false)}
            >
              {cancelLabel}
            </Button>
            <Button type="submit" variant={variant} disabled={!isSubmittable}>
              {isPending ? pendingLabel : confirmLabel}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

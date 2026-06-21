"use client";

import { useMemo, useState, useTransition } from "react";
import { useForm } from "react-hook-form";
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
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { deleteAccountAction } from "@/lib/actions/me";
import {
  makeDeleteMyAccountSchema,
  type DeleteMyAccountInput,
} from "@/lib/actions/me-schemas";

interface DeleteAccountDialogProps {
  currentEmail: string;
}

const FORM_ERROR_ID = "delete-account-error";

export function DeleteAccountDialog({ currentEmail }: DeleteAccountDialogProps) {
  const t = useTranslations("validation");
  const ts = useTranslations("settings");
  const schema = useMemo(() => makeDeleteMyAccountSchema(t), [t]);
  const [open, setOpen] = useState(false);
  const [isPending, startTransition] = useTransition();
  const [serverError, setServerError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    watch,
    reset,
    formState: { errors },
  } = useForm<DeleteMyAccountInput>({
    defaultValues: { confirmEmail: "", password: "" },
    shouldUnregister: false,
  });

  const confirmEmail = watch("confirmEmail");
  const password = watch("password");
  // Lokal aktivering: båda fält ifyllda + e-postmatchning. Server-side
  // validering är auktoritativ — detta är bara klient-UX-skydd.
  const canSubmit =
    !isPending &&
    !!password &&
    confirmEmail.trim().toLowerCase() === currentEmail.trim().toLowerCase();

  function handleOpenChange(next: boolean) {
    if (isPending) return;
    setOpen(next);
    if (!next) {
      reset();
      setServerError(null);
    }
  }

  function onSubmit(values: DeleteMyAccountInput) {
    const parsed = schema.safeParse(values);
    if (!parsed.success) {
      setServerError(
        parsed.error.issues[0]?.message ?? ts("account.delete.invalidInput")
      );
      return;
    }

    setServerError(null);
    startTransition(async () => {
      const result = await deleteAccountAction(parsed.data, currentEmail);
      if (!result.success) {
        setServerError(result.error);
      }
      // Vid success kastar deleteAccountAction redirect — vi når aldrig hit.
    });
  }

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogTrigger asChild>
        <Button type="button" variant="destructive">
          {ts("account.delete.trigger")}
        </Button>
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{ts("account.delete.title")}</DialogTitle>
          <DialogDescription>
            {ts("account.delete.description")}
          </DialogDescription>
        </DialogHeader>
        <form
          onSubmit={handleSubmit(onSubmit)}
          className="flex flex-col gap-4"
          noValidate
        >
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="delete-confirm-email">
              {ts("account.delete.confirmEmailLabel")}
            </Label>
            <Input
              id="delete-confirm-email"
              type="email"
              autoComplete="off"
              spellCheck={false}
              disabled={isPending}
              aria-invalid={errors.confirmEmail ? true : undefined}
              {...register("confirmEmail")}
            />
            <p className="text-body-sm text-text-secondary">
              {ts("account.delete.expected", { email: currentEmail })}
            </p>
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="delete-password">
              {ts("account.delete.passwordLabel")}
            </Label>
            <Input
              id="delete-password"
              type="password"
              autoComplete="current-password"
              disabled={isPending}
              aria-invalid={errors.password ? true : undefined}
              {...register("password")}
            />
          </div>
          {serverError && (
            <p
              id={FORM_ERROR_ID}
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
              {ts("account.delete.cancel")}
            </Button>
            <Button
              type="submit"
              variant="destructive"
              disabled={!canSubmit}
              aria-describedby={serverError ? FORM_ERROR_ID : undefined}
            >
              {isPending ? ts("account.delete.deleting") : ts("account.delete.submit")}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

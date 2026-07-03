"use client";

import { useActionState } from "react";
import { useSearchParams } from "next/navigation";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { registerAction, type AuthActionState } from "@/lib/auth/actions";

export function RegisterForm() {
  const t = useTranslations("pages");
  const searchParams = useSearchParams();
  const [state, formAction, isPending] = useActionState<AuthActionState, FormData>(
    registerAction,
    null
  );

  return (
    <form action={formAction} className="flex flex-col gap-5">
      <input type="hidden" name="next" value={searchParams.get("next") ?? "/jobb"} />

      <div className="flex flex-col gap-1.5">
        <label htmlFor="email" className="text-label font-medium text-text-primary">
          {t("auth.register.emailLabel")}
        </label>
        <Input
          id="email"
          name="email"
          type="email"
          autoComplete="email"
          required
          aria-required="true"
          aria-describedby="email-hint"
        />
        <p id="email-hint" className="text-body-sm text-text-secondary">
          {t("auth.register.emailHint")}
        </p>
      </div>

      <div className="flex flex-col gap-1.5">
        <label htmlFor="password" className="text-label font-medium text-text-primary">
          {t("auth.register.passwordLabel")}
        </label>
        <Input
          id="password"
          name="password"
          type="password"
          autoComplete="new-password"
          required
          aria-required="true"
          aria-describedby="password-hint"
        />
        <p id="password-hint" className="text-body-sm text-text-secondary">
          {t("auth.register.passwordHint")}
        </p>
      </div>

      {state?.error && (
        <p role="alert" className="text-body-sm leading-5 text-danger-600">
          {state.error}
        </p>
      )}

      <Button type="submit" disabled={isPending} className="w-full">
        {isPending ? t("auth.register.submitting") : t("auth.register.submit")}
      </Button>
    </form>
  );
}

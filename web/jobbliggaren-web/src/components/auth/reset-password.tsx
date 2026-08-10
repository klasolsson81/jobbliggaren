"use client";

import { useActionState, useEffect, useRef } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { Button } from "@/components/ui/button";
import { PasswordInput } from "@/components/forms/PasswordInput";
import {
  resetPasswordAction,
  type ResetPasswordActionState,
} from "@/lib/actions/reset-password";

/**
 * #1171 — the client island for /aterstall-losenord.
 *
 * Two properties here are load-bearing and easy to break:
 *
 *  1. **The POST fires only on an explicit submit, never on mount.** Mail scanners and link
 *     prefetchers GET this URL; a reset that ran on load would spend the single-use token before the
 *     user ever saw the form, and they would meet "invalid link" on their own link. Same reason
 *     `confirm-account` requires a click.
 *  2. **On error the form STAYS MOUNTED.** Identity verifies the token before it validates the
 *     password, so a rejected password does not rotate the security stamp and the SAME link is still
 *     usable. Replacing the form on a rejected-password error would strand a user whose only fault was
 *     picking a breached password, on a link that still works.
 *
 * uid and token ride in hidden inputs. They are already in the URL, so the DOM adds no exposure, and
 * neither is ever logged.
 */
export function ResetPassword({ uid, token }: { uid: string; token: string }) {
  const t = useTranslations("pages");
  const [state, formAction, isPending] = useActionState<
    ResetPasswordActionState,
    FormData
  >(resetPasswordAction, null);

  const doneRef = useRef<HTMLHeadingElement>(null);
  const passwordRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (state?.done) doneRef.current?.focus();
  }, [state?.done]);

  // On an ordinary failure focus returns to the field the user would correct — the form is still here
  // precisely so they can.
  useEffect(() => {
    if (state?.error) passwordRef.current?.focus();
  }, [state?.error]);

  if (state?.done) {
    return (
      <div className="flex flex-col gap-6">
        <div className="flex flex-col gap-1">
          <h1
            ref={doneRef}
            tabIndex={-1}
            className="text-h1 font-bold text-heading-1 focus:outline-none"
          >
            {t("auth.resetPassword.successTitle")}
          </h1>
          <p className="text-body text-text-primary">
            {t("auth.resetPassword.successBody")}
          </p>
        </div>
        <div>
          <Button asChild>
            <Link href="/logga-in">{t("auth.resetPassword.loginLink")}</Link>
          </Button>
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-8">
      <div className="flex flex-col gap-1">
        <h1 className="text-h1 font-bold text-heading-1">
          {t("auth.resetPassword.title")}
        </h1>
        <p className="text-body text-text-secondary">
          {t("auth.resetPassword.intro")}
        </p>
      </div>

      <form action={formAction} className="flex flex-col gap-5">
        <input type="hidden" name="uid" value={uid} />
        <input type="hidden" name="token" value={token} />

        <div className="flex flex-col gap-1.5">
          <label
            htmlFor="newPassword"
            className="text-label font-medium text-text-primary"
          >
            {t("auth.resetPassword.passwordLabel")}
          </label>
          <PasswordInput
            ref={passwordRef}
            id="newPassword"
            name="newPassword"
            autoComplete="new-password"
            required
            aria-required="true"
            aria-describedby="newPassword-hint"
          />
          <p id="newPassword-hint" className="text-body-sm text-text-primary">
            {t("auth.resetPassword.passwordHint")}
          </p>
        </div>

        {state?.error && (
          <p role="alert" className="text-body-sm leading-5 text-danger-600">
            {state.error}
          </p>
        )}

        <Button type="submit" disabled={isPending} className="w-full">
          {isPending
            ? t("auth.resetPassword.submitting")
            : t("auth.resetPassword.submit")}
        </Button>
      </form>
    </div>
  );
}

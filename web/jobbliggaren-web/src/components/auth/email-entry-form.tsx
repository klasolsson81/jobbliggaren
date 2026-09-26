"use client";

import { useActionState, useEffect, useId, useRef } from "react";
import Link from "next/link";
import { useTranslations } from "next-intl";
import { LoginFormMessage } from "@/components/auth/login-form-message";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import type { EmailStepState } from "@/lib/auth/challenge-action-state";
import { requestCode } from "@/lib/auth/challenge-actions";

// Client because it holds the action's state (`useActionState`) and moves focus when it arrives.
//
// Step one of the login flow: one address, for an existing account and a new one alike.
//
// `noValidate` with `required` kept, the form design-reviewer ruled for the settings name field
// (#1782). Native constraint validation answers with a browser bubble outside the DOM, in the
// browser's language, that no `role="alert"` ever announces. And for THIS field it is wrong as
// well as inaccessible: the HTML email production is ASCII-only in the local part, so it refuses
// `björn@…`, which the backend admits (#1781). The Server Action validates instead.
export function EmailEntryForm({ next }: { next: string }) {
  const t = useTranslations("pages");
  const [state, formAction, isPending] = useActionState<EmailStepState, FormData>(
    requestCode,
    null
  );
  const inputRef = useRef<HTMLInputElement>(null);
  const statusRef = useRef<HTMLParagraphElement>(null);
  const errorId = useId();
  const fieldInvalid = state?.channel === "field";

  useEffect(() => {
    if (!state) return;
    (state.channel === "field" ? inputRef : statusRef).current?.focus();
  }, [state]);

  return (
    <form action={formAction} noValidate className="flex flex-col gap-5">
      <input type="hidden" name="next" value={next} />

      <div className="flex flex-col gap-1.5">
        <label htmlFor="email" className="text-label font-medium text-text-primary">
          {t("auth.passwordless.entry.emailLabel")}
        </label>
        {/* Re-seeded from the action's echo: React 19 resets an uncontrolled form after an action. */}
        <Input
          ref={inputRef}
          id="email"
          name="email"
          type="email"
          autoComplete="email"
          defaultValue={state?.values.email ?? ""}
          required
          aria-required="true"
          aria-invalid={fieldInvalid ? true : undefined}
          aria-describedby={fieldInvalid ? `email-privacy ${errorId}` : "email-privacy"}
        />
        {/* The Art. 13 pointer sits where the address is collected (ADR 0142 D6). */}
        <p id="email-privacy" className="text-body-sm text-text-primary">
          {t.rich("auth.passwordless.entry.privacyHint", {
            privacy: (chunks) => (
              <Link href="/integritet" className="text-brand-700 underline underline-offset-2">
                {chunks}
              </Link>
            ),
          })}
        </p>
      </div>

      {state && (
        <LoginFormMessage
          id={errorId}
          message={state.error}
          channel={state.channel}
          statusRef={statusRef}
        />
      )}

      <Button type="submit" disabled={isPending} className="w-full max-md:h-11">
        {isPending
          ? t("auth.passwordless.entry.submitting")
          : t("auth.passwordless.entry.submit")}
      </Button>
    </form>
  );
}

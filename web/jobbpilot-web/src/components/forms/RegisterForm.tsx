"use client";

import { useActionState } from "react";
import { useSearchParams } from "next/navigation";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { registerAction, type AuthActionState } from "@/lib/auth/actions";

export function RegisterForm() {
  const searchParams = useSearchParams();
  const [state, formAction, isPending] = useActionState<AuthActionState, FormData>(
    registerAction,
    null
  );

  return (
    <form action={formAction} className="flex flex-col gap-5">
      <input type="hidden" name="next" value={searchParams.get("next") ?? "/mig"} />

      <div className="flex flex-col gap-1.5">
        <label htmlFor="email" className="text-label font-medium text-text-primary">
          E-postadress
        </label>
        <Input
          id="email"
          name="email"
          type="email"
          autoComplete="email"
          required
          placeholder="din.email@exempel.se"
        />
      </div>

      <div className="flex flex-col gap-1.5">
        <label htmlFor="password" className="text-label font-medium text-text-primary">
          Lösenord
        </label>
        <Input
          id="password"
          name="password"
          type="password"
          autoComplete="new-password"
          required
        />
      </div>

      {state?.error && (
        <p role="alert" className="text-sm text-danger-600">
          {state.error}
        </p>
      )}

      <Button type="submit" disabled={isPending} className="w-full">
        {isPending ? "Skapar konto..." : "Skapa konto"}
      </Button>
    </form>
  );
}

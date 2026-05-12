"use client";

import { useActionState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  requestWaitlistAction,
  type WaitlistActionState,
} from "@/lib/waitlist/actions";

const initialState: WaitlistActionState = { status: "idle" };

export function WaitlistForm() {
  const [state, formAction, isPending] = useActionState<
    WaitlistActionState,
    FormData
  >(requestWaitlistAction, initialState);

  if (state.status === "success") {
    return (
      <div
        role="status"
        aria-live="polite"
        className="flex flex-col gap-2 rounded border border-border bg-surface-2 p-5"
      >
        <p className="text-body font-medium text-text-primary">
          Anmälan registrerad.
        </p>
        <p className="text-body text-text-secondary">
          Vi har sparat <span className="font-medium">{state.email}</span> på
          väntelistan. När din plats är godkänd får du ett mejl med en länk
          för att skapa kontot.
        </p>
      </div>
    );
  }

  return (
    <form action={formAction} className="flex flex-col gap-5">
      <div className="flex flex-col gap-1.5">
        <label
          htmlFor="email"
          className="text-label font-medium text-text-primary"
        >
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

      {state.status === "error" && (
        <p role="alert" className="text-sm text-danger-600">
          {state.error}
        </p>
      )}

      <Button type="submit" disabled={isPending} className="w-full">
        {isPending ? "Skickar anmälan…" : "Anmäl till väntelista"}
      </Button>
    </form>
  );
}

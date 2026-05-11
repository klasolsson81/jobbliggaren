// "use client" required for useActionState (React 19 form state hook)
"use client";

import Link from "next/link";
import { useActionState } from "react";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { Label } from "@/components/ui/label";
import { createApplicationAction } from "@/lib/actions/applications";

export default function NyAnsokningPage() {
  const [state, formAction, isPending] = useActionState(
    createApplicationAction,
    null
  );

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center gap-4">
        <h1 className="text-h1 font-medium text-text-primary">Ny ansökan</h1>
      </div>

      <form action={formAction} className="flex flex-col gap-5 max-w-lg">
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="cover-letter">Personligt brev</Label>
          <p className="text-body-sm text-text-secondary">
            Valfritt. Du kan lägga till eller redigera det senare.
          </p>
          <Textarea
            id="cover-letter"
            name="coverLetter"
            placeholder="Skriv ett personligt brev..."
            rows={8}
            disabled={isPending}
          />
        </div>

        {state && !state.success && (
          <p className="text-body-sm text-danger-600">{state.error}</p>
        )}

        <div className="flex items-center gap-3">
          <Button type="submit" disabled={isPending}>
            Skapa ansökan
          </Button>
          <Button asChild variant="ghost">
            <Link href="/ansokningar">Avbryt</Link>
          </Button>
        </div>
      </form>
    </div>
  );
}

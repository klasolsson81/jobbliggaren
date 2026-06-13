// "use client" required for useActionState (React 19 form state hook)
"use client";

import Link from "next/link";
import { useActionState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { createResumeAction } from "@/lib/actions/resumes";

export default function NyCvPage() {
  const [state, formAction, isPending] = useActionState(
    createResumeAction,
    null
  );

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center gap-4">
        <h1 className="text-h1 font-medium text-text-primary">Nytt CV</h1>
      </div>

      <form action={formAction} className="flex flex-col gap-5 max-w-lg">
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="cv-name">Namn på CV</Label>
          <p className="text-body-sm text-text-secondary">
            Till exempel Master-CV eller Backend-utvecklare 2026.
          </p>
          <Input
            id="cv-name"
            name="name"
            required
            maxLength={200}
            disabled={isPending}
          />
        </div>

        <div className="flex flex-col gap-1.5">
          <Label htmlFor="cv-fullname">Fullständigt namn</Label>
          <p className="text-body-sm text-text-secondary">
            Visas överst på ditt CV. Du kan ändra det senare.
          </p>
          <Input
            id="cv-fullname"
            name="fullName"
            required
            maxLength={200}
            disabled={isPending}
          />
        </div>

        {state && !state.success && (
          <p className="text-body-sm text-danger-600">{state.error}</p>
        )}

        <div className="flex items-center gap-3">
          <Button type="submit" disabled={isPending}>
            Skapa CV
          </Button>
          <Button asChild variant="ghost">
            <Link href="/cv">Avbryt</Link>
          </Button>
        </div>
      </form>
    </div>
  );
}

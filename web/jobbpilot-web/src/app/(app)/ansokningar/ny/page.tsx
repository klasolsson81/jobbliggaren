// "use client" required for useActionState (React 19 form state hook)
"use client";

import Link from "next/link";
import { useActionState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Label } from "@/components/ui/label";
import { createApplicationAction } from "@/lib/actions/applications";

export default function NyAnsokningPage() {
  const [state, formAction, isPending] = useActionState(
    createApplicationAction,
    null
  );

  return (
    // /ansokningar/ny ärver V3_NATIVE_ROUTES-opt-out (prefix-match på
    // /ansokningar) → ingen transitionell bredd-container från app-shell.
    // Sidan måste därför äga egen jp-container/jp-page (design-reviewer
    // F5 Major #1 2026-05-20).
    <div className="jp-container jp-page flex flex-col gap-6">
      <header className="flex flex-col gap-1">
        <h1 className="jp-h1">Ny ansökan</h1>
        <p className="jp-lede">
          Lägg in jobbet du söker. Du kan komplettera uppgifterna senare.
        </p>
      </header>

      <form action={formAction} className="flex max-w-lg flex-col gap-5">
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="title">
            Jobbtitel{" "}
            <span aria-hidden="true" className="text-danger-600">
              *
            </span>
          </Label>
          <Input
            id="title"
            name="title"
            required
            aria-required="true"
            disabled={isPending}
          />
        </div>

        <div className="flex flex-col gap-1.5">
          <Label htmlFor="company">
            Företag{" "}
            <span aria-hidden="true" className="text-danger-600">
              *
            </span>
          </Label>
          <Input
            id="company"
            name="company"
            required
            aria-required="true"
            disabled={isPending}
          />
        </div>

        <div className="flex flex-col gap-1.5">
          <Label htmlFor="url">Annonslänk</Label>
          <Input
            id="url"
            name="url"
            type="url"
            inputMode="url"
            aria-describedby="url-hint"
            disabled={isPending}
          />
          <p id="url-hint" className="text-body-sm text-text-secondary">
            Frivilligt. Länken måste börja med http:// eller https://.
          </p>
        </div>

        <div className="flex flex-col gap-1.5">
          <Label htmlFor="expiresAt">Sista ansökningsdag</Label>
          <Input
            id="expiresAt"
            name="expiresAt"
            type="date"
            aria-describedby="expires-hint"
            disabled={isPending}
          />
          <p id="expires-hint" className="text-body-sm text-text-secondary">
            Frivilligt. Datumet visas som påminnelse i ansökningslistan.
          </p>
        </div>

        <div className="flex flex-col gap-1.5">
          <Label htmlFor="cover-letter">Personligt brev</Label>
          <Textarea
            id="cover-letter"
            name="coverLetter"
            rows={8}
            aria-describedby="cover-letter-hint"
            disabled={isPending}
          />
          <p
            id="cover-letter-hint"
            className="text-body-sm text-text-secondary"
          >
            Frivilligt. Du kan lägga till eller redigera det senare.
          </p>
        </div>

        {state && !state.success && (
          <p role="alert" className="text-body-sm text-danger-700">
            {state.error}
          </p>
        )}

        <div className="flex items-center gap-3">
          <Button type="submit" disabled={isPending}>
            {isPending ? "Sparar…" : "Skapa ansökan"}
          </Button>
          <Button asChild variant="ghost">
            <Link href="/ansokningar">Avbryt</Link>
          </Button>
        </div>
      </form>
    </div>
  );
}

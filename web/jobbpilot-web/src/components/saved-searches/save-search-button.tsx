"use client";

import { useActionState, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  createSavedSearchAction,
  type ActionResult,
} from "@/lib/actions/saved-searches";
import type { JobAdSortBy } from "@/lib/dto/job-ads";

interface SaveSearchButtonProps {
  // ADR 0042 Beslut B — ssyk/region är multi (arrays).
  ssyk: ReadonlyArray<string>;
  region: ReadonlyArray<string>;
  q: string;
  sortBy: JobAdSortBy;
}

/**
 * "Spara sökning" på /jobb. Fångar nuvarande filterläge (ssyk/region/q/sort)
 * och sparar det som en namngiven sökning via Server Action. Disablad om
 * inget kriterium är satt — en tom sökning matchar allt och har inget värde
 * (speglar backend SearchCriteria-invarianten, snabb feedback).
 *
 * Notiser (notification_enabled) exponeras inte i Fas 2 — leverans av notiser
 * är Fas 5 (ADR 0039 Beslut 4). Knappen sparar bara filtret.
 */
export function SaveSearchButton({
  ssyk,
  region,
  q,
  sortBy,
}: SaveSearchButtonProps) {
  const [open, setOpen] = useState(false);
  const formRef = useRef<HTMLFormElement>(null);

  const [state, formAction, isPending] = useActionState<
    ActionResult | null,
    FormData
  >(
    async (_prev, formData) => {
      const result = await createSavedSearchAction(_prev, formData);
      if (result.success) {
        formRef.current?.reset();
        setOpen(false);
      }
      return result;
    },
    null
  );

  const hasCriteria = ssyk.length > 0 || region.length > 0 || q !== "";

  if (!open) {
    return (
      <div className="flex flex-col gap-1.5">
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={!hasCriteria}
          onClick={() => setOpen(true)}
        >
          Spara sökning
        </Button>
        {!hasCriteria && (
          <p className="text-body-sm text-text-secondary">
            Lägg till minst ett filter (sökord, SSYK-kod eller region) för att
            kunna spara sökningen.
          </p>
        )}
        {state?.success && (
          <p role="status" className="text-body-sm text-success-700">
            Sökningen sparades. Du hittar den under Sparade sökningar.
          </p>
        )}
      </div>
    );
  }

  return (
    <form
      ref={formRef}
      action={formAction}
      className="flex flex-col gap-2 border border-border-default rounded-md p-4"
      aria-label="Spara nuvarande sökning"
    >
      {/* Ett dolt fält per element — Server Action läser via getAll() */}
      {ssyk.map((v) => (
        <input key={`ssyk-${v}`} type="hidden" name="ssyk" value={v} />
      ))}
      {region.map((v) => (
        <input key={`region-${v}`} type="hidden" name="region" value={v} />
      ))}
      <input type="hidden" name="q" value={q} />
      <input type="hidden" name="sortBy" value={sortBy} />

      <div className="flex flex-col gap-1.5">
        <Label htmlFor="saved-search-name">Namn på sökningen</Label>
        <Input
          id="saved-search-name"
          name="name"
          type="text"
          maxLength={120}
          required
          disabled={isPending}
          aria-describedby={
            state && !state.success ? "saved-search-error" : undefined
          }
        />
      </div>

      {state && !state.success && (
        <p
          id="saved-search-error"
          role="alert"
          className="text-body-sm text-danger-700"
        >
          {state.error}
        </p>
      )}

      <div className="flex flex-wrap items-center gap-2">
        <Button type="submit" size="sm" disabled={isPending}>
          {isPending ? "Sparar…" : "Spara"}
        </Button>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          disabled={isPending}
          onClick={() => setOpen(false)}
        >
          Avbryt
        </Button>
      </div>
    </form>
  );
}

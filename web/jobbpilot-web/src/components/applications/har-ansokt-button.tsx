"use client";

import Link from "next/link";
import { useState, useTransition } from "react";
import { CheckCircle2 } from "lucide-react";
import { createApplicationFromJobAdAction } from "@/lib/actions/applications";

interface HarAnsoktButtonProps {
  jobAdId: string;
}

/**
 * F6 P5 Punkt 2 Del B — "Har ansökt"-knapp i ADR 0053 jobbmodal-footer.
 * CTO Val 4 Variant A: modal-footer + toast med länk efter framgång.
 *
 * Flow:
 * 1. Klick → optimistic disabled-state + server-action
 * 2. Backend `POST /api/v1/applications/from-job-ad/{jobAdId}` skapar
 *    Application kopplad till JobAd (CreateApplicationFromJobAdCommand)
 * 3. Vid framgång: knappen byts mot inline-toast "Sparad i Mina ansökningar"
 *    med länk till `/ansokningar/{id}`. Modalen stängs inte automatiskt —
 *    användaren stänger när hen vill (civic-utility-disciplin).
 * 4. Vid fel: knappen återställs + felmeddelande visas inline.
 *
 * Idempotens: backend tillåter dubbel-create (varje POST skapar en ny
 * Application). FE förebygger dubbla klick via `isPending`-disabled-state +
 * post-success-state-switch.
 */
export function HarAnsoktButton({ jobAdId }: HarAnsoktButtonProps) {
  const [state, setState] = useState<
    | { kind: "idle" }
    | { kind: "success"; applicationId: string }
    | { kind: "error"; message: string }
  >({ kind: "idle" });
  const [isPending, startTransition] = useTransition();

  function handleClick() {
    setState({ kind: "idle" });
    startTransition(async () => {
      const result = await createApplicationFromJobAdAction(jobAdId);
      if (result.success) {
        setState({ kind: "success", applicationId: result.applicationId });
      } else {
        setState({ kind: "error", message: result.error });
      }
    });
  }

  if (state.kind === "success") {
    return (
      <div
        role="status"
        style={{
          display: "inline-flex",
          alignItems: "center",
          gap: 8,
          color: "var(--jp-ink-2)",
          fontSize: 14,
        }}
      >
        <CheckCircle2
          size={16}
          aria-hidden="true"
          style={{ color: "var(--jp-success, #2e7d32)" }}
        />
        <span>Sparad som ansökan.</span>
        <Link
          href={`/ansokningar/${state.applicationId}`}
          className="jp-btn jp-btn--secondary jp-btn--sm"
        >
          Öppna ansökan
        </Link>
      </div>
    );
  }

  return (
    <div style={{ display: "inline-flex", flexDirection: "column", gap: 4 }}>
      <button
        type="button"
        className="jp-btn jp-btn--primary"
        onClick={handleClick}
        disabled={isPending}
      >
        <CheckCircle2 size={14} aria-hidden="true" /> Har ansökt
      </button>
      {state.kind === "error" && (
        <span role="alert" className="text-danger-700" style={{ fontSize: 12 }}>
          {state.message}
        </span>
      )}
    </div>
  );
}

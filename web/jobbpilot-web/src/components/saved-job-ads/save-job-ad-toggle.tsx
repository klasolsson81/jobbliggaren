"use client";

import { useState, useTransition } from "react";
import { Bookmark, BookmarkCheck } from "lucide-react";
import {
  saveJobAdAction,
  unsaveJobAdAction,
} from "@/lib/actions/saved-job-ads";

interface SaveJobAdToggleProps {
  jobAdId: string;
  /**
   * Initialt sparat-tillstånd från server (RSC läser via `isJobAdSaved`).
   * Optimistic toggle uppdaterar lokalt utan re-fetch.
   */
  initialSaved: boolean;
  /**
   * Variant — `default` är knapp med ikon + text för modal-footer.
   * `compact` är bara ikon (för framtida in-listan-placement).
   */
  variant?: "default" | "compact";
}

/**
 * F6 P5 Punkt 2 Del A — Spara/Ta bort bokmärke-toggle.
 * ADR 0053 modal-footer (CTO Val 4 Variant A) — placeras bredvid
 * "Öppna annonsen" i `JobAdDetail` modal-footer.
 *
 * Optimistic UI: knappens ikon/text byts omedelbart vid klick, server-action
 * körs i transition. Vid fel rullas tillstånd tillbaka + felmeddelande visas
 * inline (rolealert). Idempotent backend → dubbelklick är säkert.
 */
export function SaveJobAdToggle({
  jobAdId,
  initialSaved,
  variant = "default",
}: SaveJobAdToggleProps) {
  const [saved, setSaved] = useState(initialSaved);
  const [error, setError] = useState<string | null>(null);
  const [isPending, startTransition] = useTransition();

  function handleClick() {
    const next = !saved;
    setSaved(next); // Optimistic
    setError(null);

    startTransition(async () => {
      const result = next
        ? await saveJobAdAction(jobAdId)
        : await unsaveJobAdAction(jobAdId);

      if (!result.success) {
        setSaved(!next); // Rollback
        setError(result.error);
      }
    });
  }

  const label = saved ? "Sparad" : "Spara";
  const ariaLabel = saved
    ? "Ta bort bokmärke för annonsen"
    : "Spara annonsen som bokmärke";
  const Icon = saved ? BookmarkCheck : Bookmark;

  if (variant === "compact") {
    return (
      <button
        type="button"
        className="jp-icon-btn"
        aria-label={ariaLabel}
        aria-pressed={saved}
        onClick={handleClick}
        disabled={isPending}
      >
        <Icon size={18} aria-hidden="true" />
      </button>
    );
  }

  return (
    <div style={{ display: "inline-flex", flexDirection: "column", gap: 4 }}>
      <button
        type="button"
        className={
          saved ? "jp-btn jp-btn--secondary" : "jp-btn jp-btn--secondary"
        }
        aria-label={ariaLabel}
        aria-pressed={saved}
        onClick={handleClick}
        disabled={isPending}
      >
        <Icon size={14} aria-hidden="true" /> {label}
      </button>
      {error && (
        <span role="alert" className="text-danger-700" style={{ fontSize: 12 }}>
          {error}
        </span>
      )}
    </div>
  );
}

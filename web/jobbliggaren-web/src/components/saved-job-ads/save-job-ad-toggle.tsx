"use client";

import { useState, useTransition } from "react";
import { useTranslations } from "next-intl";
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
 * F6 P5 Punkt 2 Del A — Spara/Ta bort bokmärke-toggle (ADR 0053 modal-footer).
 *
 * PR5-uppdatering (Klas-feedback 2026-05-23): Knappen är ALDRIG `disabled` —
 * Klas måste kunna ångra direkt utan att vänta på pending server-action.
 * Backend är idempotent (ADR 0032 §5 ON CONFLICT) → race-säkert mot
 * dubbelklick. Pending visas via subtle opacity i stället för disabled-state.
 *
 * Stilen är `jp-btn--secondary` paritet med övriga modal-footer-knappar
 * (civic-utility, inga konkurrerande CTA-hierarkier).
 */
export function SaveJobAdToggle({
  jobAdId,
  initialSaved,
  variant = "default",
}: SaveJobAdToggleProps) {
  const t = useTranslations("jobads.saved.toggle");
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

  const label = saved ? t("saved") : t("save");
  // Only the icon-only compact variant needs an aria-label — it has no visible text. The default
  // variant's accessible name is its visible text ("Spara"/"Sparad"), so an aria-label override
  // ("Ta bort bokmärke…") would DIVERGE from the visible label and break WCAG 2.5.3 label-in-name.
  const compactAriaLabel = saved ? t("removeAria") : t("saveAria");
  const Icon = saved ? BookmarkCheck : Bookmark;
  const opacity = isPending ? 0.7 : 1;

  if (variant === "compact") {
    return (
      <button
        type="button"
        className="jp-icon-btn"
        aria-label={compactAriaLabel}
        aria-pressed={saved}
        onClick={handleClick}
        style={{ opacity }}
      >
        <Icon size={18} aria-hidden="true" />
      </button>
    );
  }

  return (
    <div style={{ display: "inline-flex", flexDirection: "column", gap: 4 }}>
      <button
        type="button"
        className="jp-btn jp-btn--secondary"
        // No aria-label: the accessible name is the visible text; state rides aria-pressed (2.5.3).
        aria-pressed={saved}
        onClick={handleClick}
        style={{ opacity }}
      >
        <Icon
          size={14}
          aria-hidden="true"
          className={saved ? "text-success-600" : undefined}
        />{" "}
        {label}
      </button>
      {error && (
        <span role="alert" className="text-micro text-danger-700">
          {error}
        </span>
      )}
    </div>
  );
}

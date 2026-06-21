"use client";

import { useState, useTransition } from "react";
import { useTranslations } from "next-intl";
import { CheckCircle2, Circle } from "lucide-react";
import { createApplicationFromJobAdAction } from "@/lib/actions/applications";

interface HarAnsoktButtonProps {
  jobAdId: string;
  /**
   * Server-fetched initialApplied (`hasAppliedJobAd(id)`). När true visas
   * knappen som "Ansökt" från start (modal-öppning post-toggle visar
   * korrekt state utan extra round-trip — PR5 Klas-feedback fix).
   */
  initialApplied: boolean;
}

/**
 * F6 P5 Punkt 2 Del B + PR5 — "Markera som ansökt"-knapp i ADR 0053
 * jobbmodal-footer.
 *
 * PR5-ändringar (Klas-feedback 2026-05-23 + CTO Val 3 Variant A modifierad):
 * - Copy: "Markera som ansökt" (idle) / "Ansökt" (post-success) — ärlig om
 *   att handlingen skapar en Application i Status=Draft, inte ett
 *   marknadsutskick.
 * - Stil: `jp-btn--secondary` från start (paritet med Spara + Öppna annonsen).
 *   Ingen primär-CTA-hierarki som sticker ut.
 * - Layout: state-byte i samma knapp-position (ingen footer-bredd-skiftning).
 *   "Öppna ansökan"-länken renderas inte här — den läggs i en muted-rad UNDER
 *   footern av `JobAdDetail` när `initialApplied` blir true.
 * - State-persistence: `initialApplied` server-fetchas vid varje modal-mount
 *   via `hasAppliedJobAd(id)` (ADR 0063 single-endpoint). Modal-stäng/öppna
 *   återgår alltså inte till "Markera som ansökt"-state om Application redan
 *   skapats — Klas-feedback om modal-reset löst.
 */
export function HarAnsoktButton({
  jobAdId,
  initialApplied,
}: HarAnsoktButtonProps) {
  const tUi = useTranslations("applications.ui");
  const [applied, setApplied] = useState(initialApplied);
  const [error, setError] = useState<string | null>(null);
  const [isPending, startTransition] = useTransition();

  function handleClick() {
    if (applied) return; // Idempotent — backend tillåter dubbel-create, men FE-kontrakt = en gång
    setError(null);
    setApplied(true); // Optimistic

    startTransition(async () => {
      const result = await createApplicationFromJobAdAction(jobAdId);
      if (!result.success) {
        setApplied(false); // Rollback
        setError(result.error);
      }
    });
  }

  const label = applied
    ? tUi("harAnsokt.applied")
    : tUi("harAnsokt.markAsApplied");
  const ariaLabel = applied
    ? tUi("harAnsokt.appliedAriaLabel")
    : tUi("harAnsokt.markAriaLabel");
  const Icon = applied ? CheckCircle2 : Circle;
  const opacity = isPending ? 0.7 : 1;

  return (
    <div style={{ display: "inline-flex", flexDirection: "column", gap: 4 }}>
      <button
        type="button"
        className="jp-btn jp-btn--secondary"
        aria-label={ariaLabel}
        aria-pressed={applied}
        onClick={handleClick}
        style={{
          opacity,
          // Dimmad text när applied (civic tyst bekräftelse) men knappen
          // förblir klickbar (för future "ångra"-flöde — i Fas 7 ev.).
          color: applied ? "var(--jp-ink-2)" : undefined,
        }}
      >
        <Icon
          size={14}
          aria-hidden="true"
          style={{
            color: applied ? "var(--jp-success, #2e7d32)" : undefined,
          }}
        />{" "}
        {label}
      </button>
      {error && (
        <span role="alert" className="text-danger-700" style={{ fontSize: 12 }}>
          {error}
        </span>
      )}
    </div>
  );
}

"use client";

// Client-ö: den ENDA interaktiva biten av den (annars RSC) kanoniska granska-
// panelen. "use client" krävs för onClick-hanterare + useTransition (pending-UI)
// + lokal fel-state. Ingen klient-optimism: server-actionen skriver + revaliderar
// BÅDE granska-vyn och /cv, så statusen/stale-hinten re-beräknas server-side och
// kommer tillbaka som nya props vid re-render (CTO-bind).

import { useState, useTransition } from "react";
import { useTranslations } from "next-intl";
import { StatusPill } from "@/components/ui/status-pill";
import {
  setFindingStatusAction,
  type FindingStatusValue,
} from "@/lib/actions/resumes";

/**
 * Per-anmärkning statuskontroll (Fas 4b PR-8.4, CTO-bind Q3/Q4). Registrerar
 * användarens beslut om EN granskningsanmärkning i den kanoniska granskningen.
 *
 * §5-ärlighet (icke-förhandlingsbar): "Ignorera regeln (stilfråga)"-knappen
 * renderas ENBART när `isIgnorable === true` — samma mängd som backend
 * upprätthåller (400 `FindingNotIgnorable` annars). Aldrig ett erbjudande som
 * servern nekar.
 *
 * Status visas alltid med textetikett (aldrig enbart färg, WCAG 1.4.1): en
 * StatusPill bär både färg och ord. Fel ytas i en `role="alert"` med den civila
 * text som action:en redan returnerar.
 */
export function CvFindingStatusControl({
  resumeId,
  criterionId,
  userStatus,
  userStatusStaleAt,
  isIgnorable,
}: {
  resumeId: string;
  criterionId: string;
  userStatus: string | null;
  userStatusStaleAt: string | null;
  isIgnorable: boolean;
}) {
  const t = useTranslations("resumes.review.status");
  const [isPending, startTransition] = useTransition();
  // Lokal UI-fel-state (inte server-data — tillåtet). Vilket status-byte som
  // pågår spåras så att "Uppdaterar" visas på den knapp användaren tryckte.
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState<FindingStatusValue | null>(null);

  function submit(status: FindingStatusValue) {
    setError(null);
    setPending(status);
    startTransition(async () => {
      const result = await setFindingStatusAction(resumeId, criterionId, status);
      if (!result.success) {
        setError(result.error);
      }
      setPending(null);
    });
  }

  const isResolved = userStatus === "Resolved";
  const isIgnored = userStatus === "Ignored";

  function label(status: FindingStatusValue, resting: string): string {
    return isPending && pending === status ? t("updating") : resting;
  }

  return (
    <div
      className="jp-cvreview__status"
      role="group"
      aria-label={t("groupLabel")}
    >
      {isResolved && (
        <div className="jp-cvreview__status-indicator">
          <StatusPill tone="success">{t("resolvedLabel")}</StatusPill>
          <p className="jp-cvreview__status-hint">
            {userStatusStaleAt !== null ? t("staleHint") : t("resolvedHint")}
          </p>
        </div>
      )}

      {isIgnored && (
        <div className="jp-cvreview__status-indicator">
          <StatusPill tone="neutral">{t("ignoredLabel")}</StatusPill>
          <p className="jp-cvreview__status-hint">{t("ignoredHint")}</p>
        </div>
      )}

      <div className="jp-cvreview__status-actions">
        {!isResolved && (
          <button
            type="button"
            className="jp-btn jp-btn--secondary jp-btn--sm"
            onClick={() => submit("Resolved")}
            disabled={isPending}
            aria-busy={isPending && pending === "Resolved"}
          >
            {label("Resolved", t("markResolved"))}
          </button>
        )}

        {/* §5-honesty-gate: bara stilkriterier (isIgnorable) får ignoreras. */}
        {isIgnorable && !isIgnored && (
          <button
            type="button"
            className="jp-btn jp-btn--ghost jp-btn--sm"
            onClick={() => submit("Ignored")}
            disabled={isPending}
            aria-busy={isPending && pending === "Ignored"}
          >
            {label("Ignored", t("ignoreRule"))}
          </button>
        )}

        {(isResolved || isIgnored) && (
          <button
            type="button"
            className="jp-btn jp-btn--ghost jp-btn--sm"
            onClick={() => submit("Open")}
            disabled={isPending}
            aria-busy={isPending && pending === "Open"}
          >
            {label("Open", t("revert"))}
          </button>
        )}
      </div>

      {error !== null && (
        <p className="jp-cvreview__status-error" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}

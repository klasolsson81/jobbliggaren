"use client";

import { useEffect, useState } from "react";
import { ChevronDown } from "lucide-react";
import { AddFollowUpForm } from "./add-follow-up-form";
import { RecordFollowUpOutcomeForm } from "./record-follow-up-outcome-form";
import {
  CHANNEL_LABELS,
  FOLLOW_UP_OUTCOME_LABELS,
  formatSvDate,
} from "@/lib/applications/status";
import type { FollowUpDto } from "@/lib/types/applications";

interface FollowUpsSectionProps {
  applicationId: string;
  followUps: ReadonlyArray<FollowUpDto>;
}

const SECTION_LABEL_STYLE: React.CSSProperties = {
  fontSize: 13,
  fontWeight: 700,
  textTransform: "uppercase",
  letterSpacing: "0.06em",
  color: "var(--jp-ink-2)",
  marginBottom: 10,
};

/**
 * Disclosure-sektion för uppföljningar (Klas pre-F6 Prompt 4 2026-05-20).
 *
 * Mönster:
 *  - Kompakt rad per uppföljning: kanal + datum (höger) + utfall-badge +
 *    första raden av anteckning. Klick expanderar.
 *  - Endast EN rad expanderad åt gången (single-expand-id i state).
 *  - Pending-uppföljning expanderad → RecordFollowUpOutcomeForm inline.
 *  - Låst utfall (Responded/NoResponse) expanderad → plain text (utfall +
 *    outcome-datum + full anteckning), ingen dropdown.
 *  - "Lägg till uppföljning" är en knapp som default; klick → form expanderar
 *    inline. Lyckad spar eller Avbryt → kollapsa.
 *  - Esc kollapsar aktiv editor / aktiv expanderad rad.
 *
 * All API-/validerings-logik oförändrad — wrappar AddFollowUpForm och
 * RecordFollowUpOutcomeForm. Tidslinjen ovan i ApplicationDetail hanteras
 * separat (Klas-direktiv: oförändrad).
 */
export function FollowUpsSection({
  applicationId,
  followUps,
}: FollowUpsSectionProps) {
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [addOpen, setAddOpen] = useState(false);

  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape") {
        setExpandedId(null);
        setAddOpen(false);
      }
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  const sorted = [...followUps].sort(
    (a, b) =>
      new Date(b.scheduledAt).getTime() - new Date(a.scheduledAt).getTime(),
  );

  return (
    <div>
      <div style={SECTION_LABEL_STYLE}>Uppföljningar</div>

      {sorted.length === 0 ? (
        <p className="text-body-sm text-text-secondary">
          Inga uppföljningar registrerade.
        </p>
      ) : (
        <ul className="flex flex-col gap-2" role="list">
          {sorted.map((fu) => (
            <FollowUpRow
              key={fu.id}
              followUp={fu}
              applicationId={applicationId}
              expanded={expandedId === fu.id}
              onToggle={() =>
                setExpandedId((prev) => (prev === fu.id ? null : fu.id))
              }
              onClose={() => setExpandedId(null)}
            />
          ))}
        </ul>
      )}

      <div className="mt-4">
        {!addOpen ? (
          <button
            type="button"
            className="jp-btn jp-btn--secondary"
            onClick={() => setAddOpen(true)}
          >
            + Lägg till uppföljning
          </button>
        ) : (
          <div className="jp-disclosure-body">
            <h3 className="mb-3 text-body font-medium text-text-primary">
              Lägg till uppföljning
            </h3>
            <AddFollowUpForm
              applicationId={applicationId}
              onSuccess={() => setAddOpen(false)}
              onCancel={() => setAddOpen(false)}
            />
          </div>
        )}
      </div>
    </div>
  );
}

interface FollowUpRowProps {
  applicationId: string;
  followUp: FollowUpDto;
  expanded: boolean;
  onToggle: () => void;
  onClose: () => void;
}

function FollowUpRow({
  applicationId,
  followUp,
  expanded,
  onToggle,
  onClose,
}: FollowUpRowProps) {
  const recorded = followUp.outcome !== "Pending";
  const channel = CHANNEL_LABELS[followUp.channel] ?? followUp.channel;
  const scheduledLabel =
    formatSvDate(followUp.scheduledAt) ??
    new Date(followUp.scheduledAt).toLocaleDateString("sv-SE");
  const outcomeLabel =
    FOLLOW_UP_OUTCOME_LABELS[followUp.outcome] ?? followUp.outcome;
  const outcomeAt = recorded && followUp.outcomeAt
    ? formatSvDate(followUp.outcomeAt)
    : null;
  const noteFirstLine = followUp.note
    ? (followUp.note.split(/\r?\n/)[0] ?? null)
    : null;

  return (
    <li>
      <button
        type="button"
        className="jp-disclosure-row"
        aria-expanded={expanded}
        onClick={onToggle}
      >
        <span className="jp-disclosure-row__primary">{channel}</span>
        <span
          className={`jp-pill jp-pill--${recorded ? (followUp.outcome === "Responded" ? "success" : "neutral") : "info"} jp-disclosure-row__pill`}
        >
          <span className="jp-pill__dot" aria-hidden="true" />
          {outcomeLabel}
        </span>
        {noteFirstLine && (
          <span className="jp-disclosure-row__note">{noteFirstLine}</span>
        )}
        <span className="jp-disclosure-row__date jp-mono">
          {scheduledLabel}
        </span>
        <ChevronDown
          size={16}
          className="jp-disclosure-row__chevron"
          style={{
            transform: expanded ? "rotate(180deg)" : "rotate(0deg)",
            transition: "transform 120ms ease",
          }}
          aria-hidden="true"
        />
      </button>

      {expanded && (
        <div className="jp-disclosure-body">
          {recorded ? (
            <dl className="flex flex-col gap-2 text-body-sm">
              <div className="flex gap-2">
                <dt className="text-text-secondary">Utfall:</dt>
                <dd className="text-text-primary">
                  {outcomeLabel}
                  {outcomeAt && (
                    <span className="ml-2 font-mono text-text-secondary">
                      ({outcomeAt})
                    </span>
                  )}
                </dd>
              </div>
              {followUp.note && (
                <div className="flex gap-2">
                  <dt className="text-text-secondary">Anteckning:</dt>
                  <dd className="text-text-primary whitespace-pre-line">
                    {followUp.note}
                  </dd>
                </div>
              )}
            </dl>
          ) : (
            <>
              {followUp.note && (
                <div className="mb-3 text-body-sm">
                  <span className="text-text-secondary">Anteckning: </span>
                  <span className="text-text-primary whitespace-pre-line">
                    {followUp.note}
                  </span>
                </div>
              )}
              <RecordFollowUpOutcomeForm
                applicationId={applicationId}
                followUpId={followUp.id}
                onSuccess={onClose}
                onCancel={onClose}
              />
            </>
          )}
        </div>
      )}
    </li>
  );
}

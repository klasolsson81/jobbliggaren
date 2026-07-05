"use client";

import { useEffect, useState } from "react";
import { useFormatter, useTranslations } from "next-intl";
import { ChevronDown } from "lucide-react";
import { AddFollowUpForm } from "./add-follow-up-form";
import { RecordFollowUpOutcomeForm } from "./record-follow-up-outcome-form";
import {
  channelLabel,
  followUpOutcomeLabel,
} from "@/lib/applications/status";
import { formatDate } from "@/lib/i18n/format";
import type { FollowUpDto } from "@/lib/types/applications";

interface FollowUpsSectionProps {
  applicationId: string;
  followUps: ReadonlyArray<FollowUpDto>;
  /**
   * Read-mode (#630 PR 6 drawer): render the follow-ups as a static list only —
   * no expand, no inline "Lägg till", no record-outcome form. Capability-gating,
   * not a separate component: the "present follow-ups" responsibility is
   * unchanged. Default false = the full interactive disclosure (full-page /
   * current behaviour, unchanged).
   */
  readOnly?: boolean;
  /**
   * #630 PR 7 (CTO-bind 6b, komposition): valfri header-yta bredvid
   * sektionsrubriken — drawern monterar sin "+ Lägg till"-knapp (öppnar
   * "Logga uppföljning"-dialogen, Klas-låst §8.6) HÄR utan att sektionen får
   * mutationsansvar; den förblir ren presentation.
   */
  headerAction?: React.ReactNode;
  /** Valfri tomläges-text (drawern använder §8.6-copyn); default = befintlig. */
  emptyLabel?: string;
}

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
  readOnly = false,
  headerAction,
  emptyLabel,
}: FollowUpsSectionProps) {
  const tUi = useTranslations("applications.ui");
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [addOpen, setAddOpen] = useState(false);

  useEffect(() => {
    if (readOnly) return;
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape") {
        setExpandedId(null);
        setAddOpen(false);
      }
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [readOnly]);

  const sorted = [...followUps].sort(
    (a, b) =>
      new Date(b.scheduledAt).getTime() - new Date(a.scheduledAt).getTime(),
  );

  return (
    <div>
      <div className="jp-section-label jp-section-label--row">
        {tUi("followUps.sectionLabel")}
        {headerAction}
      </div>

      {sorted.length === 0 ? (
        <p className="text-body-sm text-text-primary">
          {emptyLabel ?? tUi("followUps.empty")}
        </p>
      ) : (
        <ul className="flex flex-col gap-2" role="list">
          {sorted.map((fu) => (
            <FollowUpRow
              key={fu.id}
              followUp={fu}
              applicationId={applicationId}
              readOnly={readOnly}
              expanded={!readOnly && expandedId === fu.id}
              onToggle={() =>
                setExpandedId((prev) => (prev === fu.id ? null : fu.id))
              }
              onClose={() => setExpandedId(null)}
            />
          ))}
        </ul>
      )}

      {!readOnly && (
        <div className="mt-4">
          {!addOpen ? (
            <button
              type="button"
              className="jp-btn jp-btn--secondary"
              onClick={() => setAddOpen(true)}
            >
              {tUi("followUps.add")}
            </button>
          ) : (
            <div className="jp-disclosure-body">
              <h3 className="mb-3 text-body font-medium text-text-primary">
                {tUi("followUps.addHeading")}
              </h3>
              <AddFollowUpForm
                applicationId={applicationId}
                onSuccess={() => setAddOpen(false)}
                onCancel={() => setAddOpen(false)}
              />
            </div>
          )}
        </div>
      )}
    </div>
  );
}

interface FollowUpRowProps {
  applicationId: string;
  followUp: FollowUpDto;
  expanded: boolean;
  onToggle: () => void;
  onClose: () => void;
  readOnly?: boolean;
}

function FollowUpRow({
  applicationId,
  followUp,
  expanded,
  onToggle,
  onClose,
  readOnly = false,
}: FollowUpRowProps) {
  const t = useTranslations("applications.enums");
  const tUi = useTranslations("applications.ui");
  const format = useFormatter();
  const recorded = followUp.outcome !== "Pending";
  const channel = channelLabel(t, followUp.channel);
  const scheduledLabel =
    formatDate(format, followUp.scheduledAt) ?? "";
  const outcomeLabel = followUpOutcomeLabel(t, followUp.outcome);
  const outcomeAt = recorded && followUp.outcomeAt
    ? formatDate(format, followUp.outcomeAt)
    : null;
  const noteFirstLine = followUp.note
    ? (followUp.note.split(/\r?\n/)[0] ?? null)
    : null;

  // Shared row summary (channel + outcome pill + note first line + date).
  const summary = (
    <>
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
      <span className="jp-disclosure-row__date jp-mono">{scheduledLabel}</span>
    </>
  );

  // Read-mode (#630 PR 6): a static, non-interactive list row — no expand button,
  // no chevron, no record form. Same visual as the disclosure summary (DRY).
  if (readOnly) {
    return (
      <li>
        <div className="jp-disclosure-row jp-disclosure-row--static">
          {summary}
        </div>
      </li>
    );
  }

  return (
    <li>
      <button
        type="button"
        className="jp-disclosure-row"
        aria-expanded={expanded}
        onClick={onToggle}
      >
        {summary}
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
                <dt className="text-text-secondary">{tUi("followUps.outcomeLabel")}</dt>
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
                  <dt className="text-text-secondary">{tUi("followUps.noteLabel")}</dt>
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
                  <span className="text-text-secondary">
                    {tUi("followUps.noteLabel")}{" "}
                  </span>
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

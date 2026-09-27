"use client";

import { useEffect, useState } from "react";
import { useFormatter, useTranslations } from "next-intl";
import { ChevronDown, Plus } from "lucide-react";
import { AddFollowUpForm } from "./add-follow-up-form";
import { RecordFollowUpOutcomeForm } from "./record-follow-up-outcome-form";
import { InfoDialog } from "@/components/common/info-dialog";
import {
  channelLabel,
  followUpOutcomeLabel,
} from "@/lib/applications/status";
import { formatDate } from "@/lib/i18n/format";
import type { FollowUpDto, FollowUpOutcome } from "@/lib/types/applications";

// The outcome reads as a square status tag on the existing variants (#1827 Minor 9).
const OUTCOME_TAG: Record<FollowUpOutcome, string> = {
  Pending: "status-info",
  Responded: "status-success",
  NoResponse: "status-neutral",
  Logged: "status-neutral",
};

interface FollowUpsSectionProps {
  applicationId: string;
  followUps: ReadonlyArray<FollowUpDto>;
  titleLevel: 1 | 2;
  canPlan: boolean;
  /**
   * #630 PR 7 (CTO-bind 6b, komposition): valfri header-yta bredvid
   * sektionsrubriken — detaljkroppen monterar sin Logga uppföljning-knapp
   * (Klas-låst §8.6) HÄR utan att sektionen får mutationsansvar; den förblir
   * ren presentation.
   */
  headerAction?: React.ReactNode;
}

/**
 * Disclosure-sektion för uppföljningar (Klas pre-F6 Prompt 4 2026-05-20).
 *
 * Mönster:
 *  - Kompakt rad per uppföljning: kanal + utfall + första raden av anteckning +
 *    datum (höger). Klick expanderar.
 *  - Endast EN rad expanderad åt gången (single-expand-id i state).
 *  - Pending-uppföljning expanderad → RecordFollowUpOutcomeForm inline.
 *  - Satt utfall expanderad → plain text (utfall + outcome-datum + full
 *    anteckning), ingen dropdown.
 *  - "Planera uppföljning" är en knapp som default; klick → form expanderar
 *    inline. Lyckad spar eller Avbryt → kollapsa.
 *  - Esc kollapsar aktiv editor / aktiv expanderad rad.
 *
 * All API-/validerings-logik oförändrad — wrappar AddFollowUpForm och
 * RecordFollowUpOutcomeForm.
 */
export function FollowUpsSection({
  applicationId,
  followUps,
  titleLevel,
  canPlan,
  headerAction,
}: FollowUpsSectionProps) {
  const tUi = useTranslations("applications.ui");
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [addOpen, setAddOpen] = useState(false);
  const FormHeading = titleLevel === 1 ? "h2" : "h3";

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
    <section aria-labelledby="jp-detail-followups-title">
      <div className="jp-section-label jp-section-label--row">
        {/* #805 punkt 5: inline "?"-hjälp bunden till etiketten förklarar
            skillnaden uppföljning vs anteckning (inline-help-doktrin, #408).
            Uppföljning = du agerade → väntetiden räknas om; anteckning =
            privat minnesanteckning utan tidseffekt. */}
        <span className="jp-labelhelp">
          {/* The region is named by this span alone, so the "?" trigger stays
              out of the name. */}
          <span id="jp-detail-followups-title">
            {tUi("followUps.sectionLabel")}
          </span>
          <InfoDialog
            title={tUi("followUps.help.title")}
            paragraphs={[tUi("followUps.help.p1"), tUi("followUps.help.p2")]}
            ariaLabel={tUi("followUps.help.aria")}
            triggerClassName="jp-labelhelp__trigger"
          />
        </span>
        {headerAction}
      </div>

      {sorted.length === 0 ? (
        <p className="text-body-sm text-text-primary">
          {tUi("followUps.emptyDrawer")}
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

      {canPlan && (
        <div className="mt-4">
          {!addOpen ? (
            <button
              type="button"
              className="jp-btn jp-btn--secondary"
              onClick={() => setAddOpen(true)}
            >
              <Plus className="size-4" aria-hidden="true" />
              {tUi("followUps.add")}
            </button>
          ) : (
            <div className="jp-disclosure-body">
              <FormHeading className="mb-3 text-body font-medium text-text-primary">
                {tUi("followUps.addHeading")}
              </FormHeading>
              <AddFollowUpForm
                applicationId={applicationId}
                onSuccess={() => setAddOpen(false)}
                onCancel={() => setAddOpen(false)}
              />
            </div>
          )}
        </div>
      )}
    </section>
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

  return (
    <li>
      <button
        type="button"
        className="jp-disclosure-row jp-disclosure-row--followup"
        aria-expanded={expanded}
        onClick={onToggle}
      >
        <span className="jp-disclosure-row__head">
          <span className="jp-disclosure-row__primary">{channel}</span>
          <span className="jp-tag" data-tag={OUTCOME_TAG[followUp.outcome]}>
            {outcomeLabel}
          </span>
        </span>
        {noteFirstLine && (
          <span className="jp-disclosure-row__note">{noteFirstLine}</span>
        )}
        <span className="jp-disclosure-row__date">{scheduledLabel}</span>
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
                    <span className="jp-detail-date ml-2">
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

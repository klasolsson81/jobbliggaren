import { useTranslations } from "next-intl";
import { FollowUpsSection } from "@/components/applications/follow-ups-section";
import { NotesSection } from "@/components/applications/notes-section";
import { PreservedAdPanel } from "@/components/applications/preserved-ad-panel";
import { TimelineList } from "@/components/applications/timeline-list";
import {
  applicationStatusLabel,
  channelLabel,
  followUpOutcomeLabel,
  PILL_VARIANT_CLASS,
  STATUS_BADGE_VARIANT,
} from "@/lib/applications/status";
import { composeTimeline, daysInCurrentStep } from "@/lib/applications/timeline";
import type { ApplicationDetailDto } from "@/lib/types/applications";

interface ApplicationDrawerBodyProps {
  application: ApplicationDetailDto;
  /** Server-computed reference time for "N dagar i detta steg" (per-request). */
  now: Date;
}

/**
 * ApplicationDrawerBody — read-mode detail panel content (#630 PR 6, design
 * handoff §8). Pure presentational Server Component rendered as children of the
 * client ApplicationDrawerShell (the shell owns the head: role + company + close).
 *
 * §8 order: status block → UPPFÖLJNINGAR (list-only) → ANNONSEN·SPARAD KOPIA
 * (fallback) → TIDSLINJE (real, newest-first, always open) → ANTECKNINGAR (kept
 * interactive) → cover letter.
 *
 * Strict read-mode (Klas 2026-07-05): NO StatusEditCard / Withdraw / step-picker /
 * primary CTA / dialogs — all status mutation is PR 7. The status block's day-count
 * and the timeline derive from REAL recorded StatusChanges (composeTimeline /
 * daysInCurrentStep); the retired `updatedAt` synthesis is never used here (§5,
 * never fabricate a transition).
 */
export function ApplicationDrawerBody({
  application,
  now,
}: ApplicationDrawerBodyProps) {
  const t = useTranslations("applications.enums");
  const tUi = useTranslations("applications.ui");

  const { jobAd } = application;
  const preservedAd = application.preservedAd ?? null;
  const showPreservedAd = jobAd == null && preservedAd != null;

  const variant = PILL_VARIANT_CLASS[STATUS_BADGE_VARIANT[application.status]];
  const statusLabel = applicationStatusLabel(t, application.status);

  const timeline = composeTimeline(application);
  const days = daysInCurrentStep(application.statusChanges, now);
  // "Senaste: {event}" — the newest event that has ACTUALLY happened (at <= now).
  // composeTimeline is newest-first, but a FUTURE-scheduled follow-up sorts to the
  // top by its scheduledAt — "senaste" must be present-anchored, never a future
  // post (design-reviewer Major; this line is also the dialog's aria-describedby).
  // The label mapping mirrors TimelineList; it is intentionally duplicated (rule of
  // three: only two call sites) because next-intl's namespace-scoped translator
  // types make a shared helper more friction than the small switch is worth.
  const latest =
    timeline.find((e) => new Date(e.at).getTime() <= now.getTime()) ?? null;
  const latestLabel = ((): string | null => {
    if (!latest) return null;
    switch (latest.kind) {
      case "created":
        return tUi("detail.eventCreated");
      case "note":
        return tUi("detail.eventNoteAdded");
      case "followUpScheduled":
        return tUi("detail.eventFollowUpScheduled", {
          channel: channelLabel(t, latest.channel),
        });
      case "followUpOutcome":
        return tUi("detail.eventOutcome", {
          outcome: followUpOutcomeLabel(t, latest.outcome),
        });
      case "statusChange":
        // Colon-free in the "Senaste:" context (avoids "Senaste: Status: …").
        return tUi("detail.eventStatusChangeShort", {
          from: applicationStatusLabel(t, latest.from),
          to: applicationStatusLabel(t, latest.to),
        });
    }
  })();

  const hasMeta = days != null || latestLabel != null;

  return (
    <>
      {/* Statusblock (§8.2) — 4px vänsterkant i statusfärg (.jp-status-block +
          data-status-variant), STATUS-kicker + värde + underrad "N dagar i detta
          steg · Senaste: {händelse}". id="jp-modal-desc" behålls OVILLKORLIGT
          (drawer-shellens aria-describedby dinglar aldrig). */}
      <div
        className="jp-modal__match jp-status-block"
        data-status-variant={variant}
      >
        <div className="jp-modal__match__expl" id="jp-modal-desc">
          <div className="jp-status-block__label">
            {tUi("detail.statusLabel")}
          </div>
          <b className="jp-status-block__value">{statusLabel}</b>
          {hasMeta && (
            <div className="jp-status-block__next">
              {days != null && tUi("detail.daysInStep", { days })}
              {days != null && latestLabel != null ? " · " : null}
              {latestLabel != null &&
                tUi("detail.latestEvent", { event: latestLabel })}
            </div>
          )}
        </div>
      </div>

      {/* Uppföljningar (§8.6) — list-only i PR 6 (add-dialog → PR 7). */}
      <FollowUpsSection
        applicationId={application.id}
        followUps={application.followUps}
        readOnly
      />

      {/* Om annonsen (sparad kopia) (§8.7) — fallback när live-annonsen är
          arkiverad. Delad PreservedAdPanel (samma som fullsidan). */}
      {showPreservedAd && <PreservedAdPanel preservedAd={preservedAd} />}

      {/* Tidslinje (§8.8) — REALA händelser, nyast först, alltid öppen (till
          skillnad från fullsidans kollapsade <details>). Ingen updatedAt-syntes. */}
      <section aria-labelledby="jp-drawer-timeline-title">
        <div className="jp-section-label" id="jp-drawer-timeline-title">
          {tUi("detail.timelineLabel")}
        </div>
        <TimelineList events={timeline} />
      </section>

      {/* Anteckningar (§8.9) — behåll befintlig interaktiv NotesSection. */}
      <NotesSection applicationId={application.id} notes={application.notes} />

      {/* Personligt brev — läs-prosa, behålls (ingen mutationsyta). */}
      {application.coverLetter && (
        <div>
          <div className="jp-section-label">
            {tUi("detail.coverLetterLabel")}
          </div>
          <p className="jp-modal__description jp-detail-prose">
            {application.coverLetter}
          </p>
        </div>
      )}
    </>
  );
}

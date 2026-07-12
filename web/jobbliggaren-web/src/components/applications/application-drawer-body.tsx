import { useTranslations } from "next-intl";
import { DrawerLogFollowUpButton } from "@/components/applications/drawer-log-follow-up-button";
import { DrawerStatusActions } from "@/components/applications/drawer-status-actions";
import { FollowUpsSection } from "@/components/applications/follow-ups-section";
import { NotesSection } from "@/components/applications/notes-section";
import { SourceAdSection } from "@/components/applications/source-ad-section";
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
 * ApplicationDrawerBody — detail panel content (#630 PR 6 läs-läge; PR 7 gör
 * den interaktiv per design §8; "Drawer"-namnet är ett PR 6-arv — sedan
 * 2026-07-10 renderas kroppen i den centrerade ApplicationModalShell, ADR 0092
 * Livscykel-amendment). Server Component rendered as children of the client
 * ApplicationModalShell (the shell owns the head: role + company + close);
 * mutationsmaskineriet är KLIENT-öar som får serialiserbara props
 * (DrawerStatusActions, DrawerLogFollowUpButton, NotesSection).
 *
 * §8 order: status block → primär-CTA + stegväljare + AVSLUTA ELLER PARKERA
 * (PR 7, §8.3–8.5) → UPPFÖLJNINGAR (statisk lista + "+ Lägg till" →
 * Logga-uppföljning-dialogen, Klas-låst §8.6) → ANNONSEN·SPARAD KOPIA
 * (fallback) → TIDSLINJE (real, newest-first, always open) → ANTECKNINGAR
 * (interactive) → cover letter.
 *
 * The status block's day-count and the timeline derive from REAL recorded
 * StatusChanges (composeTimeline / daysInCurrentStep); the retired `updatedAt`
 * synthesis is never used here (§5, never fabricate a transition).
 */
export function ApplicationDrawerBody({
  application,
  now,
}: ApplicationDrawerBodyProps) {
  const t = useTranslations("applications.enums");
  const tUi = useTranslations("applications.ui");

  // ?? null: schemat är .nullable().optional() (deploy-skew-resiliens) → normalisera
  // en gång, så guarden nedströms bara har två fall att resonera om.
  const jobAd = application.jobAd ?? null;
  // #805-3: NÄR den bevarade kopian visas avgörs av SourceAdSection (SPOT) —
  // på källannonsens Status, inte på jobAd == null (den guarden var vakuös, #821).
  const preservedAd = application.preservedAd ?? null;
  // Toast-visningsnamn ("{company}: …"): företag (live → sparad kopia) före
  // det korta id:t — samma precedens som drawer-headern.
  const displayName =
    jobAd?.company ??
    preservedAd?.company ??
    `#${application.id.slice(0, 8)}`;

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

      {/* Statusmaskineriet (§8.3–8.5, PR 7): primär-CTA + stegväljare +
          AVSLUTA ELLER PARKERA — klient-ö, direktbyten med ångra-toast. */}
      <DrawerStatusActions
        applicationId={application.id}
        status={application.status}
        displayName={displayName}
      />

      {/* Uppföljningar (§8.6) — statisk lista; "+ Lägg till" öppnar
          Logga-uppföljning-dialogen (Klas-låst, prototyp-trogen). */}
      <FollowUpsSection
        applicationId={application.id}
        followUps={application.followUps}
        readOnly
        emptyLabel={tUi("followUps.emptyDrawer")}
        headerAction={
          <DrawerLogFollowUpButton
            applicationId={application.id}
            contextTitle={jobAd?.title ?? preservedAd?.title ?? null}
            contextCompany={jobAd?.company ?? preservedAd?.company ?? null}
            toastCompany={displayName}
          />
        }
      />

      {/* Om annonsen (§8.7) — #805-3 (Beslut B). SourceAdSection äger guarden:
          live → utlänk till källans annons · borta → bevarad kopia (ADR 0086)
          eller lugn not · manuell → länken användaren sparade. Delad med
          fullsidan (SPOT). */}
      <SourceAdSection jobAd={jobAd} preservedAd={preservedAd} />

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

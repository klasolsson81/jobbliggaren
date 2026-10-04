import { InformationLink } from "@/components/information/InformationLink";
import { useFormatter, useTranslations } from "next-intl";
import { RecruiterContactBlock } from "@/components/job-ads/recruiter-contact-block";
import contactLinkStyles from "@/components/job-ads/recruiter-contact-link.module.css";
import { adIdentityOf } from "@/components/applications/ad-identity";
import { ApplicationStatusActions } from "@/components/applications/application-status-actions";
import { FollowUpsSection } from "@/components/applications/follow-ups-section";
import { LogFollowUpButton } from "@/components/applications/log-follow-up-button";
import { NotesSection } from "@/components/applications/notes-section";
import { SourceAdSection } from "@/components/applications/source-ad-section";
import { TimelineList } from "@/components/applications/timeline-list";
import {
  applicationStatusLabel,
  channelLabel,
  followUpOutcomeLabel,
  isClosedForActivity,
  PILL_VARIANT_CLASS,
  STATUS_BADGE_VARIANT,
} from "@/lib/applications/status";
import { composeTimeline, daysInCurrentStep } from "@/lib/applications/timeline";
import { formatDate } from "@/lib/i18n/format";
import type { ApplicationDetailDto } from "@/lib/types/applications";

interface ApplicationDetailBodyProps {
  application: ApplicationDetailDto;
  /** Server-computed reference time for "N dagar i steget" (per-request). */
  now: Date;
  titleLevel: 1 | 2;
}

/**
 * ApplicationDetailBody — the application detail, one body in both contexts (ADR 0053
 * Beslut 2): the route modal renders it inside ApplicationModalShell, and the full page inside
 * its own header and foot. A Server Component; the mutation machinery is client islands with
 * serializable props (ApplicationStatusActions, FollowUpsSection, LogFollowUpButton,
 * NotesSection).
 *
 * §8 order: status block → primary CTA + step picker + AVSLUTA ELLER PARKERA (§8.3–8.5) →
 * available frozen contacts → UPPFÖLJNINGAR (logged and planned follow-ups) →
 * ANNONSEN · SPARAD KOPIA → TIDSLINJE (newest first, always open) → ANTECKNINGAR → cover letter.
 *
 * The status block's day count and the timeline derive from REAL recorded StatusChanges
 * (composeTimeline / daysInCurrentStep); the retired `updatedAt` synthesis is never used here
 * (§5, never fabricate a transition).
 */
export function ApplicationDetailBody({
  application,
  now,
  titleLevel,
}: ApplicationDetailBodyProps) {
  const t = useTranslations("applications.enums");
  const tUi = useTranslations("applications.ui");
  const format = useFormatter();

  // ?? null: schemat är .nullable().optional() (deploy-skew-resiliens) → normalisera
  // en gång, så guarden nedströms bara har två fall att resonera om.
  const jobAd = application.jobAd ?? null;
  // #805-3: NÄR den bevarade kopian visas avgörs av SourceAdSection (SPOT) —
  // på källannonsens Status, inte på jobAd == null (den guarden var vakuös, #821).
  const preservedAd = application.preservedAd ?? null;
  const contacts = preservedAd?.contacts ?? [];
  // #892: strukturell identitet — en raderad annons utan snapshot bär TOM
  // identitet på wiren; adIdentityOf normaliserar tomt → null så coalescingen
  // nedan aldrig väljer en tom sträng framför den sparade kopian/id-fallbacken.
  const { title: adTitle, company: adCompany } = adIdentityOf(jobAd);
  // Toast display name ("{company}: …"): the company (live, then the saved copy)
  // before the short id.
  const displayName =
    adCompany ??
    preservedAd?.company ??
    `#${application.id.slice(0, 8)}`;

  const variant = PILL_VARIANT_CLASS[STATUS_BADGE_VARIANT[application.status]];
  const statusLabel = applicationStatusLabel(t, application.status);
  const closedForActivity = isClosedForActivity(application.status);

  const timeline = composeTimeline(application);
  const days = daysInCurrentStep(application.statusChanges, now);
  // "Senaste: {event}" — the newest event that has ACTUALLY happened (at <= now).
  // composeTimeline is newest-first, but a FUTURE-scheduled follow-up sorts to the
  // top by its scheduledAt — "senaste" must be present-anchored, never a future
  // post (design-reviewer Major; this line is also part of the modal's description).
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

  // The earliest follow-up still waiting for its outcome (K3, Klas 2026-09-26).
  const nextFollowUp = application.followUps
    .filter((fu) => fu.outcome === "Pending")
    .sort(
      (a, b) =>
        new Date(a.scheduledAt).getTime() - new Date(b.scheduledAt).getTime(),
    )[0];
  const nextFollowUpDate = formatDate(format, nextFollowUp?.scheduledAt);

  return (
    <>
      {/* Statusblock (§8.2) — 4px vänsterkant i statusfärg (.jp-status-block +
          data-status-variant), STATUS-kicker + värde + underrader. id="jp-modal-desc"
          renderas OVILLKORLIGT, så modalskalets aria-describedby aldrig dinglar. */}
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
          {!closedForActivity && nextFollowUpDate != null && (
            <div className="jp-status-block__next">
              {tUi("detail.nextFollowUp")}{" "}
              <span className="jp-status-block__next-date">
                {nextFollowUpDate}
              </span>
            </div>
          )}
        </div>
      </div>

      {/* Statusmaskineriet (§8.3–8.5, PR 7): primär-CTA + stegväljare +
          AVSLUTA ELLER PARKERA — klient-ö. */}
      <ApplicationStatusActions
        applicationId={application.id}
        status={application.status}
        displayName={displayName}
        // The copy is projected even while the ad is active, so the detail always knows.
        copyHasText={preservedAd?.description != null}
      />

      {contacts.length > 0 && (
        <div>
          <RecruiterContactBlock contacts={contacts} />
          <p className="jp-recruiter-notice mt-2">
            <InformationLink id={`information-application-${application.id}`} href="/kontaktperson-i-annons" className={contactLinkStyles.link}>
              {tUi("preservedAd.recruiterNoticeLink")}
            </InformationLink>
          </p>
        </div>
      )}

      {/* Uppföljningar (§8.6): "Logga uppföljning" i rubrikraden öppnar dialogen
          (Klas-låst, prototyp-trogen); planeringen med kanal och datum ligger under
          listan (K3). */}
      {(!closedForActivity || application.followUps.length > 0) && (
        <FollowUpsSection
          applicationId={application.id}
          followUps={application.followUps}
          titleLevel={titleLevel}
          canPlan={!closedForActivity}
          headerAction={
            closedForActivity ? null : (
              <LogFollowUpButton
                applicationId={application.id}
                contextTitle={adTitle ?? preservedAd?.title ?? null}
                contextCompany={adCompany ?? preservedAd?.company ?? null}
                toastCompany={displayName}
              />
            )
          }
        />
      )}

      {/* Om annonsen (§8.7) — #805-3 (Beslut B). SourceAdSection äger guarden:
          live → utlänk till källans annons · borta → bevarad kopia (ADR 0086)
          eller lugn not · manuell → länken användaren sparade. */}
      <SourceAdSection jobAd={jobAd} preservedAd={preservedAd} />

      {/* Tidslinje (§8.8) — REALA händelser, nyast först, alltid öppen. Ingen
          updatedAt-syntes. */}
      <section aria-labelledby="jp-detail-timeline-title">
        <div className="jp-section-label" id="jp-detail-timeline-title">
          {tUi("detail.timelineLabel")}
        </div>
        <TimelineList events={timeline} />
      </section>

      {/* Anteckningar (§8.9) — behåll befintlig interaktiv NotesSection. */}
      <NotesSection
        applicationId={application.id}
        notes={application.notes}
        titleLevel={titleLevel}
      />

      {/* Personligt brev — läs-prosa, behålls (ingen mutationsyta). */}
      {application.coverLetter && (
        <section aria-labelledby="jp-detail-cover-letter-title">
          <div className="jp-section-label" id="jp-detail-cover-letter-title">
            {tUi("detail.coverLetterLabel")}
          </div>
          <p className="jp-modal__description jp-detail-prose">
            {application.coverLetter}
          </p>
        </section>
      )}
    </>
  );
}

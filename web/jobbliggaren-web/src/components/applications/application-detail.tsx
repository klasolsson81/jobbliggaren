import { useFormatter, useTranslations } from "next-intl";
import { StatusEditCard } from "@/components/applications/status-edit-card";
import { FollowUpsSection } from "@/components/applications/follow-ups-section";
import { NotesSection } from "@/components/applications/notes-section";
import {
  applicationStatusLabel,
  channelLabel,
  followUpOutcomeLabel,
  PILL_VARIANT_CLASS,
  STATUS_BADGE_VARIANT,
} from "@/lib/applications/status";
import { formatDate } from "@/lib/i18n/format";
import type { ApplicationDetailDto } from "@/lib/types/applications";

interface ApplicationDetailProps {
  application: ApplicationDetailDto;
  /**
   * När true renderas titel/företag i modal-headern av anroparen
   * (ApplicationModalShell), så detaljen utelämnar sin egen rubrik-header.
   * Fullsidan sätter false och äger rubriken själv. Speglar F3
   * JobAdDetail.headless exakt (ADR 0053, en presentationskomponent,
   * två kontexter — DRY).
   */
  headless?: boolean;
}

interface TimelineEvent {
  date: string;
  label: string;
  primary?: boolean;
}

/**
 * ApplicationDetail — ren presentational Server Component (ingen "use
 * client"). Delas av fullsidan (`/ansokningar/[id]`) och ansökan-modalen
 * (`@modal/(.)ansokningar/[id]`) per ADR 0053 (en presentations-komponent,
 * två kontexter — DRY, speglar F3 JobAdDetail exakt).
 *
 * Innehållet är REAL ApplicationDetailDto (no-mock): status-block
 * (statusbadge-ikon + "Status"-label + STATUS_LABELS), Tidslinje komponerad
 * av REALA events (createdAt + notes[].createdAt + followUps[]
 * scheduledAt/outcomeAt + updatedAt, sorterade), Anteckningar (real notes[]
 * + AddNoteForm), Uppföljningar (real followUps[] + AddFollowUpForm +
 * RecordFollowUpOutcomeForm), Personligt brev om coverLetter finns.
 *
 * "Uppdatera status" + destruktiv-bekräftelse återanvänder den befintliga
 * StatusEditCard (REAL transition-wiring via getAllowedTransitions +
 * isDestructiveTransition + Dialog-bekräftelse, redan ADR 0047 Area-5-
 * godkänd) OFÖRÄNDRAD — endast omgivande presentation omstylas till v3.
 * Mutationsformulären (AddNoteForm/AddFollowUpForm/
 * RecordFollowUpOutcomeForm) är "use client"-öar i detta RSC-träd och
 * passas EJ som icke-serialiserbara props över @modal-gränsen — de är
 * children i ett server-renderat träd (F3-mönster).
 */
export function ApplicationDetail({
  application,
  headless = false,
}: ApplicationDetailProps) {
  // next-intl `useTranslations` resolves synchronously in a Server Component
  // (reads the per-request config), so ApplicationDetail stays a non-async RSC
  // — its synchronous render tests and serialized @modal slot are unaffected.
  const t = useTranslations("applications.enums");
  const tUi = useTranslations("applications.ui");
  const format = useFormatter();
  const { jobAd } = application;
  const hasIdentity = jobAd != null;
  const shortId = application.id.slice(0, 8);
  const title = hasIdentity
    ? jobAd.title
    : tUi("detail.fallbackTitle", { shortId });

  const variant = PILL_VARIANT_CLASS[STATUS_BADGE_VARIANT[application.status]];
  const statusLabel = applicationStatusLabel(t, application.status);

  // Nästa öppna uppföljning (tidigast schemalagd, ej besvarad) → "Nästa"-
  // raden i status-blocket. REAL fält (followUps[].scheduledAt), ej v3-mock.
  const nextFollowUp = [...application.followUps]
    .filter((fu) => fu.outcome === "Pending")
    .sort(
      (a, b) =>
        new Date(a.scheduledAt).getTime() -
        new Date(b.scheduledAt).getTime()
    )[0];
  const nextDate = formatDate(format, nextFollowUp?.scheduledAt);

  // Tidslinje: komponera REALA händelser, nyast först. Ingen mock.
  const timeline: TimelineEvent[] = [];
  const createdAt = formatDate(format, application.createdAt);
  if (createdAt) {
    timeline.push({ date: createdAt, label: tUi("detail.eventCreated") });
  }
  for (const note of application.notes) {
    const d = formatDate(format, note.createdAt);
    if (d) timeline.push({ date: d, label: tUi("detail.eventNoteAdded") });
  }
  for (const fu of application.followUps) {
    const scheduled = formatDate(format, fu.scheduledAt);
    if (scheduled) {
      timeline.push({
        date: scheduled,
        label: tUi("detail.eventFollowUpScheduled", {
          channel: channelLabel(t, fu.channel),
        }),
      });
    }
    if (fu.outcome !== "Pending" && fu.outcomeAt) {
      const outcomeAt = formatDate(format, fu.outcomeAt);
      if (outcomeAt) {
        timeline.push({
          date: outcomeAt,
          label: tUi("detail.eventOutcome", {
            outcome: followUpOutcomeLabel(t, fu.outcome),
          }),
        });
      }
    }
  }
  const updatedAt = formatDate(format, application.updatedAt);
  if (updatedAt) {
    timeline.push({
      date: updatedAt,
      label: tUi("detail.eventStatus", { status: statusLabel }),
      primary: true,
    });
  }
  timeline.sort(
    (a, b) => new Date(b.date).getTime() - new Date(a.date).getTime()
  );

  return (
    <>
      {!headless && (
        <header className="jp-modal__head">
          <div style={{ flex: 1 }}>
            <h1 className={hasIdentity ? "jp-modal__title" : "jp-modal__title jp-mono"}>
              {title}
            </h1>
            <p className="jp-modal__company">
              {hasIdentity ? (
                <>
                  {jobAd.company} ·{" "}
                  <span className="jp-mono">#{shortId}</span>
                </>
              ) : (
                /* Titel = "Ansökan #shortId"-fallback. Ekas EJ som subtitle
                   (duplikat); skapad-datum är informativ metadata istället
                   (design-reviewer F5 Major #2 2026-05-20). */
                <>
                  {tUi("detail.createdPrefix")}{" "}
                  <span className="jp-mono">{createdAt}</span>
                </>
              )}
            </p>
          </div>
        </header>
      )}

      <div className="jp-modal__body">
        {/* headless: ModalShell-subtitlen bär redan "{company} · #{shortId}"
            (prototyp pages.jsx ApplicationModal: #id EN gång i headern).
            Ingen dubblerad #shortId-body-rad (F5 design-reviewer M1). */}

        {/* Status-block (v3 jp-modal__match-stil). Klas pre-F6 Prompt 3
            (2026-05-20): cirkulär status-ikon borttagen — såg AI-genererad
            ut. Status markeras nu med en 4px vänsterkant-stapel i status-
            färg (civic-utility, dovt). Färg + label-färg styrs av
            data-status-variant (#344, speglar .jp-tag[data-tag="status-*"]).
            Neutral kort-bg + border (CSS-default) så stapeln blir den enda
            statusindikatorn. */}
        <div className="jp-modal__match jp-status-block" data-status-variant={variant}>
          {/* id="jp-modal-desc" OVILLKORLIGT här (status-blocket renderas
              alltid) → ApplicationModalShell aria-describedby dinglar
              aldrig (F5 code-reviewer M1, F3 job-ad-detail.tsx-mönster:
              beskrivnings-id alltid i DOM). */}
          <div className="jp-modal__match__expl" id="jp-modal-desc">
            <div className="jp-status-block__label">
              {tUi("detail.statusLabel")}
            </div>
            <b className="jp-status-block__value">{statusLabel}</b>
            {nextDate && (
              <div className="jp-status-block__next">
                {tUi("detail.nextFollowUp")}{" "}
                <span className="jp-mono jp-status-block__next-date">
                  {nextDate}
                </span>
              </div>
            )}
          </div>
        </div>

        {/* Uppdatera status — REAL transition (ALLOWED_TRANSITIONS) +
            ADR 0047 Area-5 destruktiv-bekräftelse. StatusEditCard
            oförändrad — bevarat beteende, ej regression. Hårfin
            border-top + spacing separerar "var jag är" (status-blocket)
            från "vad jag kan göra" (åtgärden) — ADR 0047 (#344). */}
        <div className="jp-status-action">
          <StatusEditCard
            applicationId={application.id}
            currentStatus={application.status}
          />
        </div>

        {/* Tidslinje — REALA events, nyast först. Kollapsad som default via
            native <details> (#344): redundant med status-blockets "Nästa",
            FollowUpsSection och NotesSection, så den göms bakom en summary.
            Native <details> håller ApplicationDetail som Server Component
            (ingen "use client") och är tangentbords-/SR-tillgänglig per
            default. Tom-fallet renderar ingen <details> alls. */}
        {timeline.length > 0 && (
          <details className="jp-timeline">
            {/* No aria-label: the visible "Tidslinje" is the accessible name
                (WCAG 2.5.3 Label in Name), and native <details>/<summary>
                already announces the expand/collapse state. */}
            <summary className="jp-timeline__summary">
              <svg
                className="jp-timeline__chevron"
                viewBox="0 0 16 16"
                fill="none"
                aria-hidden="true"
              >
                <path
                  d="M6 4l4 4-4 4"
                  stroke="currentColor"
                  strokeWidth="1.5"
                  strokeLinecap="round"
                  strokeLinejoin="round"
                />
              </svg>
              {tUi("detail.timelineLabel")}
            </summary>
            <ul className="jp-timeline__list">
              {timeline.map((e, i) => (
                <li key={`${e.date}-${i}`} className="jp-timeline__item">
                  <span className="jp-mono jp-timeline__date">{e.date}</span>
                  <span
                    className={
                      e.primary
                        ? "jp-timeline__label jp-timeline__label--primary"
                        : "jp-timeline__label"
                    }
                  >
                    {e.label}
                  </span>
                </li>
              ))}
            </ul>
          </details>
        )}

        {/* Uppföljningar — REAL followUps[] (Prompt 4: disclosure-mönster
            via client-island FollowUpsSection. State är lokal i client-ön;
            API-/validerings-logik oförändrad — AddFollowUpForm +
            RecordFollowUpOutcomeForm är wrappade men i sak orörda). */}
        <FollowUpsSection
          applicationId={application.id}
          followUps={application.followUps}
        />

        {/* Anteckningar — REAL notes[] (Prompt 4: speglar disclosure-
            mönstret från FollowUpsSection). */}
        <NotesSection
          applicationId={application.id}
          notes={application.notes}
        />

        {/* Personligt brev — endast om coverLetter finns. Sist + 68ch
            läsbredd (#344). */}
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
      </div>
    </>
  );
}

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

const BADGE_COLOR_VAR: Record<string, string> = {
  info: "var(--jp-info)",
  brand: "var(--jp-accent-700)", /* G1: brand-interaktionsfärg = accent (navy är logo-only) */
  success: "var(--jp-success)",
  warning: "var(--jp-warning)",
  danger: "var(--jp-danger)",
  neutral: "var(--jp-ink-3)",
};

const SECTION_LABEL_STYLE: React.CSSProperties = {
  fontSize: 13,
  fontWeight: 700,
  textTransform: "uppercase",
  letterSpacing: "0.06em",
  color: "var(--jp-ink-2)",
  marginBottom: 10,
};

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
  const statusColor = BADGE_COLOR_VAR[variant];
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
            färg (civic-utility, dovt). Neutral kort-bg + border (CSS-
            default) så stapeln blir den enda statusindikatorn. */}
        <div
          className="jp-modal__match"
          style={{
            borderLeft: `4px solid ${statusColor}`,
          }}
        >
          {/* id="jp-modal-desc" OVILLKORLIGT här (status-blocket renderas
              alltid) → ApplicationModalShell aria-describedby dinglar
              aldrig (F5 code-reviewer M1, F3 job-ad-detail.tsx-mönster:
              beskrivnings-id alltid i DOM). */}
          <div className="jp-modal__match__expl" id="jp-modal-desc">
            <div
              style={{
                fontSize: 13,
                color: statusColor,
                textTransform: "uppercase",
                letterSpacing: "0.06em",
                fontWeight: 700,
                marginBottom: 2,
              }}
            >
              {tUi("detail.statusLabel")}
            </div>
            <b style={{ fontSize: 16 }}>{statusLabel}</b>
            {nextDate && (
              <div
                style={{
                  marginTop: 4,
                  fontSize: 14,
                  color: "var(--jp-ink-2)",
                }}
              >
                {tUi("detail.nextFollowUp")}{" "}
                <span
                  className="jp-mono"
                  style={{ color: "var(--jp-ink-1)", fontWeight: 600 }}
                >
                  {nextDate}
                </span>
              </div>
            )}
          </div>
        </div>

        {/* Uppdatera status — REAL transition (ALLOWED_TRANSITIONS) +
            ADR 0047 Area-5 destruktiv-bekräftelse. StatusEditCard
            oförändrad — bevarat beteende, ej regression. */}
        <StatusEditCard
          applicationId={application.id}
          currentStatus={application.status}
        />

        {/* Tidslinje — REALA events, nyast först */}
        <div>
          <div style={SECTION_LABEL_STYLE}>{tUi("detail.timelineLabel")}</div>
          {timeline.length === 0 ? (
            <p className="text-body-sm text-text-secondary">
              {tUi("detail.timelineEmpty")}
            </p>
          ) : (
            <ul
              style={{
                listStyle: "none",
                padding: 0,
                margin: 0,
                display: "flex",
                flexDirection: "column",
                gap: 12,
              }}
            >
              {timeline.map((e, i) => (
                <li
                  key={`${e.date}-${i}`}
                  style={{
                    display: "flex",
                    gap: 14,
                    alignItems: "baseline",
                  }}
                >
                  <span
                    className="jp-mono"
                    style={{
                      fontSize: 12,
                      color: "var(--jp-ink-3)",
                      width: 120,
                      flexShrink: 0,
                      // Längsta sv-SE month:"short"-datum ("30 sep. 2026")
                      // får aldrig wrappa/trunkeras, light+dark (F5
                      // design-reviewer M2).
                      whiteSpace: "nowrap",
                    }}
                  >
                    {e.date}
                  </span>
                  <span
                    style={{
                      color: "var(--jp-ink-1)",
                      fontWeight: e.primary ? 600 : 400,
                    }}
                  >
                    {e.label}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </div>

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

        {/* Personligt brev — endast om coverLetter finns */}
        {application.coverLetter && (
          <div>
            <div style={SECTION_LABEL_STYLE}>{tUi("detail.coverLetterLabel")}</div>
            <p
              className="jp-modal__description"
              style={{ maxWidth: "68ch" }}
            >
              {application.coverLetter}
            </p>
          </div>
        )}
      </div>
    </>
  );
}

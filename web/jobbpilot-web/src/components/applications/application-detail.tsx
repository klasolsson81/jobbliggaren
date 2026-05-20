import { StatusEditCard } from "@/components/applications/status-edit-card";
import { AddNoteForm } from "@/components/applications/add-note-form";
import { AddFollowUpForm } from "@/components/applications/add-follow-up-form";
import { RecordFollowUpOutcomeForm } from "@/components/applications/record-follow-up-outcome-form";
import {
  CHANNEL_LABELS,
  FOLLOW_UP_OUTCOME_LABELS,
  formatSvDate,
  getStatusLabel,
  PILL_VARIANT_CLASS,
  STATUS_BADGE_VARIANT,
} from "@/lib/applications/status";
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
  brand: "var(--jp-navy-700)",
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
  const { jobAd } = application;
  const hasIdentity = jobAd != null;
  const shortId = application.id.slice(0, 8);
  const title = hasIdentity
    ? jobAd.title
    : `Ansökan #${shortId}`;

  const variant = PILL_VARIANT_CLASS[STATUS_BADGE_VARIANT[application.status]];
  const statusColor = BADGE_COLOR_VAR[variant];
  const statusLabel = getStatusLabel(application.status);

  const sortedNotes = [...application.notes].sort(
    (a, b) =>
      new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime()
  );
  const sortedFollowUps = [...application.followUps].sort(
    (a, b) =>
      new Date(b.scheduledAt).getTime() - new Date(a.scheduledAt).getTime()
  );

  // Nästa öppna uppföljning (tidigast schemalagd, ej besvarad) → "Nästa"-
  // raden i status-blocket. REAL fält (followUps[].scheduledAt), ej v3-mock.
  const nextFollowUp = [...application.followUps]
    .filter((fu) => fu.outcome === "Pending")
    .sort(
      (a, b) =>
        new Date(a.scheduledAt).getTime() -
        new Date(b.scheduledAt).getTime()
    )[0];
  const nextDate = formatSvDate(nextFollowUp?.scheduledAt);

  // Tidslinje: komponera REALA händelser, nyast först. Ingen mock.
  const timeline: TimelineEvent[] = [];
  const createdAt = formatSvDate(application.createdAt);
  if (createdAt) {
    timeline.push({ date: createdAt, label: "Ansökan skapades" });
  }
  for (const note of application.notes) {
    const d = formatSvDate(note.createdAt);
    if (d) timeline.push({ date: d, label: "Anteckning tillagd" });
  }
  for (const fu of application.followUps) {
    const scheduled = formatSvDate(fu.scheduledAt);
    if (scheduled) {
      timeline.push({
        date: scheduled,
        label: `Uppföljning (${CHANNEL_LABELS[fu.channel] ?? fu.channel}) schemalagd`,
      });
    }
    if (fu.outcome !== "Pending" && fu.outcomeAt) {
      const outcomeAt = formatSvDate(fu.outcomeAt);
      if (outcomeAt) {
        timeline.push({
          date: outcomeAt,
          label: `Utfall: ${FOLLOW_UP_OUTCOME_LABELS[fu.outcome] ?? fu.outcome}`,
        });
      }
    }
  }
  const updatedAt = formatSvDate(application.updatedAt);
  if (updatedAt) {
    timeline.push({
      date: updatedAt,
      label: `Status: ${statusLabel}`,
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
                  Skapad <span className="jp-mono">{createdAt}</span>
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
              Status
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
                Nästa uppföljning:{" "}
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
          <div style={SECTION_LABEL_STYLE}>Tidslinje</div>
          {timeline.length === 0 ? (
            <p className="text-body-sm text-text-secondary">
              Inga händelser registrerade ännu.
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

        {/* Uppföljningar — REAL followUps[] */}
        <div>
          <div style={SECTION_LABEL_STYLE}>Uppföljningar</div>
          {sortedFollowUps.length === 0 ? (
            <p className="text-body-sm text-text-secondary">
              Inga uppföljningar registrerade.
            </p>
          ) : (
            <ul className="flex flex-col gap-3">
              {sortedFollowUps.map((fu) => {
                const recorded = fu.outcome !== "Pending";
                return (
                  <li
                    key={fu.id}
                    className="rounded-md border border-border-default px-4 py-3"
                  >
                    <div className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
                      <span className="font-medium text-text-primary">
                        {CHANNEL_LABELS[fu.channel] ?? fu.channel}
                      </span>
                      <span className="font-mono text-body-sm text-text-secondary">
                        {formatSvDate(fu.scheduledAt) ??
                          new Date(fu.scheduledAt).toLocaleDateString(
                            "sv-SE"
                          )}
                      </span>
                    </div>
                    <dl className="mt-2 flex flex-col gap-1 text-body-sm">
                      <div className="flex gap-2">
                        <dt className="text-text-secondary">Utfall:</dt>
                        <dd className="text-text-primary">
                          {FOLLOW_UP_OUTCOME_LABELS[fu.outcome] ??
                            fu.outcome}
                          {recorded && fu.outcomeAt && (
                            <span className="ml-1 font-mono text-text-secondary">
                              ({formatSvDate(fu.outcomeAt)})
                            </span>
                          )}
                        </dd>
                      </div>
                      {fu.note && (
                        <div className="flex gap-2">
                          <dt className="text-text-secondary">
                            Anteckning:
                          </dt>
                          <dd className="text-text-primary">{fu.note}</dd>
                        </div>
                      )}
                    </dl>
                    {fu.outcome === "Pending" && (
                      <RecordFollowUpOutcomeForm
                        applicationId={application.id}
                        followUpId={fu.id}
                      />
                    )}
                  </li>
                );
              })}
            </ul>
          )}
          <div className="mt-4 border-t border-border-default pt-4">
            <h3 className="mb-3 text-body font-medium text-text-primary">
              Lägg till uppföljning
            </h3>
            <AddFollowUpForm applicationId={application.id} />
          </div>
        </div>

        {/* Anteckningar — REAL notes[] */}
        <div>
          <div style={SECTION_LABEL_STYLE}>Anteckningar</div>
          {sortedNotes.length === 0 ? (
            <p className="text-body-sm text-text-secondary">
              Inga anteckningar ännu.
            </p>
          ) : (
            <ul className="flex flex-col gap-3">
              {sortedNotes.map((note) => (
                <li
                  key={note.id}
                  className="rounded-md border border-border-default px-4 py-3"
                >
                  <p className="text-body text-text-primary">
                    {note.content}
                  </p>
                  <p className="mt-1 font-mono text-body-sm text-text-secondary">
                    {formatSvDate(note.createdAt)}
                  </p>
                </li>
              ))}
            </ul>
          )}
          <div className="mt-4 border-t border-border-default pt-4">
            <h3 className="mb-3 text-body font-medium text-text-primary">
              Lägg till anteckning
            </h3>
            <AddNoteForm applicationId={application.id} />
          </div>
        </div>

        {/* Personligt brev — endast om coverLetter finns */}
        {application.coverLetter && (
          <div>
            <div style={SECTION_LABEL_STYLE}>Personligt brev</div>
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

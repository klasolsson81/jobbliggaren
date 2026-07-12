import { useFormatter, useTranslations } from "next-intl";
import { applicationSourceLabel } from "@/lib/applications/status";
import { formatDate } from "@/lib/i18n/format";
import type { AdSnapshotDto } from "@/lib/types/applications";

interface PreservedAdPanelProps {
  preservedAd: AdSnapshotDto;
}

/**
 * "Om annonsen (sparad kopia)" — #315 (ADR 0086). The frozen ad-text snapshot,
 * captured at apply-time. Pure presentational Server Component — no client state.
 *
 * #805-3: rendered ONLY by {@link SourceAdSection}, and only when the source ad
 * is no longer active (`JobAdSummaryDto.Status !== "Active"`). The caller owns
 * that decision (SPOT) — this component renders, it does not decide.
 *
 * The previous guard keyed on `jobAd == null`, which is unreachable: `JobAd`
 * never resolves to null for a JobAd-linked application because the soft-delete
 * axis it relied on (`JobAd.DeletedAt`) has no writer (#821). This panel — and
 * with it the product's only "Visa annonsen" link, which used to live inside it —
 * therefore never rendered in production. #805-3 fixes the guard and moves the
 * out-link to where it is truthful: SourceAdSection, while the ad IS active.
 *
 * Rows are omitted when the source field is null (same omission pattern as the
 * rest of the detail — no placeholder dashes). Calm, informative tone (a saved
 * copy notice, not a warning). description == null (terminal-status retention
 * minimisation, ADR 0092 D3 — unchanged by #805-3) → a short neutral note
 * instead of an empty body.
 */
export function PreservedAdPanel({ preservedAd }: PreservedAdPanelProps) {
  const t = useTranslations("applications.enums");
  const tUi = useTranslations("applications.ui");
  const format = useFormatter();

  const preservedPublished = formatDate(format, preservedAd.publishedAt);
  const preservedExpires = formatDate(format, preservedAd.expiresAt);
  const preservedCaptured = formatDate(format, preservedAd.capturedAt);
  const preservedSourceLabel = applicationSourceLabel(t, preservedAd.source);

  return (
    <section aria-labelledby="jp-preserved-ad-title">
      <div className="jp-section-label" id="jp-preserved-ad-title">
        {tUi("preservedAd.panelTitle")}
      </div>

      {/* Lugn "sparad kopia"-not: den befintliga neutrala bordered
          surface-2-rutan (.jp-modal__match), ingen status-färg, ingen
          varningston. */}
      <div className="jp-modal__match">
        <div className="jp-modal__match__expl">
          {tUi("preservedAd.savedNotice", {
            date: preservedCaptured ?? "",
          })}
        </div>
      </div>

      {/* Bevarad metadata: label · värde-rader, hårfin separator
          (.jp-modal__matchrow återbrukad — ingen ny klass). Rader
          utelämnas när källfältet är null. */}
      <dl className="jp-modal__matchrows" style={{ marginTop: "12px" }}>
        <div className="jp-modal__matchrow">
          <dt className="jp-modal__matchrow-label">
            {tUi("preservedAd.company")}
          </dt>
          <dd className="jp-modal__matchrow-evidence">
            {preservedAd.company}
          </dd>
        </div>

        {preservedAd.location && (
          <div className="jp-modal__matchrow">
            <dt className="jp-modal__matchrow-label">
              {tUi("preservedAd.location")}
            </dt>
            <dd className="jp-modal__matchrow-evidence">
              {preservedAd.location}
            </dd>
          </div>
        )}

        {preservedPublished && (
          <div className="jp-modal__matchrow">
            <dt className="jp-modal__matchrow-label">
              {tUi("preservedAd.published")}
            </dt>
            <dd className="jp-modal__matchrow-evidence jp-mono">
              {preservedPublished}
            </dd>
          </div>
        )}

        {preservedExpires && (
          <div className="jp-modal__matchrow">
            <dt className="jp-modal__matchrow-label">
              {tUi("preservedAd.applyBy")}
            </dt>
            <dd className="jp-modal__matchrow-evidence jp-mono">
              {preservedExpires}
            </dd>
          </div>
        )}

        <div className="jp-modal__matchrow">
          <dt className="jp-modal__matchrow-label">
            {tUi("preservedAd.source")}
          </dt>
          <dd className="jp-modal__matchrow-evidence">
            {preservedSourceLabel}
          </dd>
        </div>
      </dl>

      {/* #805-3 (Beslut B): utlänken som stod här är BORTTAGEN. Panelen renderas
          numera ENBART när källannonsen inte längre är aktiv (SourceAdSection,
          Status != "Active") — och då kan vi inte hävda att snapshot-URL:en
          fortfarande svarar. Det var exakt den döda länk Beslut B förbjuder
          ("ingen död länk"). Utlänken lever vidare i SourceAdSection, där den
          visas medan annonsen ÄR aktiv. Den bevarade kopian nedan är vad vi kan
          stå för (ADR 0086) och är strikt bättre än en slantsingling. */}

      {/* Annonstexten. Vid description == null (terminal status →
          retention-minimering) visas EJ en tom kropp: en kort, neutral
          not förklarar att texten rensats men metadatan finns kvar. */}
      <div style={{ marginTop: "16px" }}>
        <div className="jp-section-label">
          {tUi("preservedAd.descriptionLabel")}
        </div>
        {preservedAd.description ? (
          <p className="jp-modal__description jp-detail-prose">
            {preservedAd.description}
          </p>
        ) : (
          <div className="jp-modal__match">
            <div className="jp-modal__match__expl">
              {tUi("preservedAd.minimizedNotice")}
            </div>
          </div>
        )}
      </div>
    </section>
  );
}

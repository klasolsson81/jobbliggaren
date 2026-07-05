import { ExternalLink } from "lucide-react";
import { useFormatter, useTranslations } from "next-intl";
import { applicationSourceLabel } from "@/lib/applications/status";
import { formatDate } from "@/lib/i18n/format";
import type { AdSnapshotDto } from "@/lib/types/applications";

interface PreservedAdPanelProps {
  preservedAd: AdSnapshotDto;
}

/**
 * "Om annonsen (sparad kopia)" — #315 (ADR 0086). The frozen ad-text snapshot,
 * shown as the FALLBACK when the live JobAd is archived (jobAd == null) but a copy
 * was captured at apply-time. Extracted verbatim from ApplicationDetail so the
 * full-page detail and the read-mode drawer body render ONE identical panel
 * (DRY / SPOT). Pure presentational Server Component — no client state.
 *
 * Rows are omitted when the source field is null (same omission pattern as the
 * rest of the detail — no placeholder dashes). Calm, informative tone (a saved
 * copy notice, not a warning). description == null (terminal-status retention
 * minimisation) → a short neutral note instead of an empty body.
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

      {/* Säker länk (befintlig F3-behandling, speglar JobAdDetail):
          jp-btn--secondary + ExternalLink-ikon, ny flik, noopener/
          noreferrer. aria-label bär källa + "öppnas i ny flik" så
          den utgående länken annonseras tydligt. */}
      {preservedAd.url && (
        <p style={{ marginTop: "12px" }}>
          <a
            href={preservedAd.url}
            target="_blank"
            rel="noopener noreferrer"
            aria-label={tUi("preservedAd.viewAdAriaLabel", {
              source: preservedSourceLabel,
            })}
            className="jp-btn jp-btn--secondary"
          >
            <ExternalLink size={14} aria-hidden="true" />{" "}
            {tUi("preservedAd.viewAd")}
          </a>
        </p>
      )}

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

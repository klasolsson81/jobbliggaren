import { ExternalLink } from "lucide-react";
import { getJobAdStatusLabel } from "@/lib/job-ads/status";
import type { JobAdDto, JobAdStatus } from "@/lib/dto/job-ads";
import { SaveJobAdToggle } from "@/components/saved-job-ads/save-job-ad-toggle";
import { HarAnsoktButton } from "@/components/applications/har-ansokt-button";
import { formatAdDescription } from "./format-ad-description";

/**
 * JobAdDetail — ren presentational Server Component (ingen "use client",
 * noll interaktivitet). Delas av både fullsida (`/jobb/[id]`) och
 * jobbmodalen (`@modal/(.)jobb/[id]`) per ADR 0053 (en presentations-
 * komponent, två kontexter — DRY-positiv konsekvens).
 *
 * Fält-set är ADR 0053 amendment 2026-05-19 (Fas-3-gated): ENDAST real
 * JobAdDto. Match-score / requirements / occupation / location / "Spara
 * annons" / "Har ansökt" var v3-prototyp-mock och saknas i domänen — de
 * renderas EJ (frånvaro, inte mock; HANDOVER §0.5-veto uppfylls genom
 * frånvaro, ingen disabled-knapp-teater).
 */

interface JobAdDetailProps {
  jobAd: JobAdDto;
  /**
   * När true renderas titel/företag i modal-headern av anroparen
   * (JobAdModalShell), så detaljen utelämnar sin egen rubrik-header.
   * Fullsidan sätter false och äger rubriken själv.
   */
  headless?: boolean;
  /**
   * F6 P5 Punkt 2 Del A — initialt sparat-tillstånd för Spara-toggle
   * i modal-footer. Server-renderas via `isJobAdSaved(id)`-lookup.
   * `undefined` (default) = för icke-inloggade/system-vyer → toggle döljs.
   * Lyfter ADR 0053 Amendment 2026-05-19-deferralen (Spara/Har-ansökt
   * → Fas-4-uppskjutet) i samband med backend-leverans 2026-05-23.
   */
  initialSaved?: boolean;
}

// Active/Expired/Archived → .jp-pill-variant. Speglar
// JOB_AD_STATUS_BADGE_VARIANT-semantiken (Active=success, Expired=warning,
// Archived=neutral) men mot v3 .jp-pill-systemet (HANDOVER §5.7).
const STATUS_PILL_CLASS: Record<JobAdStatus, string> = {
  Active: "jp-pill jp-pill--success",
  Expired: "jp-pill jp-pill--warning",
  Archived: "jp-pill jp-pill--neutral",
};

function formatDate(iso: string): string {
  // CLAUDE.md §10.2 — svensk locale (sv-SE).
  return new Date(iso).toLocaleDateString("sv-SE");
}

export function JobAdDetail({
  jobAd,
  headless = false,
  initialSaved,
}: JobAdDetailProps) {
  const publishedAt = formatDate(jobAd.publishedAt);
  const expiresAt = jobAd.expiresAt ? formatDate(jobAd.expiresAt) : null;

  return (
    <>
      {!headless && (
        <header className="jp-modal__head">
          <div style={{ flex: 1 }}>
            <h1 className="jp-modal__title">{jobAd.title}</h1>
            <p className="jp-modal__company">{jobAd.companyName}</p>
          </div>
          <span className={STATUS_PILL_CLASS[jobAd.status]}>
            <span className="jp-pill__dot" aria-hidden="true" />
            {getJobAdStatusLabel(jobAd.status)}
          </span>
        </header>
      )}

      <div className="jp-modal__body">
        {headless && (
          <span className={STATUS_PILL_CLASS[jobAd.status]} style={{ alignSelf: "flex-start" }}>
            <span className="jp-pill__dot" aria-hidden="true" />
            {getJobAdStatusLabel(jobAd.status)}
          </span>
        )}

        <dl className="jp-modal__metarow">
          <div className="jp-modal__metaitem">
            <dt>Publicerad</dt>
            <dd>{publishedAt}</dd>
          </div>
          {expiresAt && (
            <div className="jp-modal__metaitem">
              <dt>Sista ansökningsdag</dt>
              <dd>{expiresAt}</dd>
            </div>
          )}
          <div className="jp-modal__metaitem">
            <dt>Annons-ID</dt>
            <dd>{jobAd.id}</dd>
          </div>
        </dl>

        <div>
          <div
            style={{
              fontSize: 13,
              fontWeight: 700,
              textTransform: "uppercase",
              letterSpacing: "0.06em",
              color: "var(--jp-ink-2)",
              marginBottom: 8,
            }}
          >
            Annonsbeskrivning
          </div>
          <div id="jp-modal-desc" className="jp-modal__description">
            {formatAdDescription(jobAd.description)}
          </div>
        </div>
      </div>

      <div className="jp-modal__foot">
        <span className="jp-modal__foot__spacer" />
        {initialSaved !== undefined && (
          <>
            <SaveJobAdToggle jobAdId={jobAd.id} initialSaved={initialSaved} />
            <HarAnsoktButton jobAdId={jobAd.id} />
          </>
        )}
        {jobAd.url && (
          <a
            href={jobAd.url}
            target="_blank"
            rel="noopener noreferrer"
            className="jp-btn jp-btn--secondary"
          >
            <ExternalLink size={14} aria-hidden="true" /> Öppna annonsen
          </a>
        )}
      </div>
    </>
  );
}

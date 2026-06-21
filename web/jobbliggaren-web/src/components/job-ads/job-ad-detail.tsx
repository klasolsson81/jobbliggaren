import Link from "next/link";
import { ExternalLink } from "lucide-react";
import { getJobAdStatusLabel } from "@/lib/job-ads/status";
import type { JobAdDto, JobAdStatus } from "@/lib/dto/job-ads";
import type { JobAdMatchDetail } from "@/lib/dto/job-ad-match";
import { SaveJobAdToggle } from "@/components/saved-job-ads/save-job-ad-toggle";
import { HarAnsoktButton } from "@/components/applications/har-ansokt-button";
import { JobAdMatchSection } from "./job-ad-match-section";
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
   * F6 P5 Punkt 2 — initial-state för Spara/Har-ansökt-knappar i modal-footer.
   * `undefined` (default) = anonym/system-vy → knappar döljs helt
   * (civic-utility — ingen disabled-knapp-teater).
   * När definerade: båda måste vara satta tillsammans (PR5 — server-fetchar
   * isJobAdSaved + hasAppliedJobAd parallellt i page-handler).
   */
  initialSaved?: boolean;
  initialApplied?: boolean;
  /**
   * F4-16 (ADR 0076, CTO D3) — matchnings-detalj mot användarens profil.
   * `undefined`/`null` = ingen sektion renderas (anonym / ingen träffdata /
   * gäst — frånvaro, ej teater, ADR 0053). Server-fetchad parallellt i
   * page-handlern (parity initialSaved/initialApplied).
   */
  match?: JobAdMatchDetail | null;
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
  initialApplied,
  match,
}: JobAdDetailProps) {
  // Typ-narrowing-pattern: bind till en `userActions`-konst som är non-null
  // när BÅDA props är definierade. Eliminerar `!`-suppressions i JSX nedan
  // (code-reviewer Minor 6).
  const userActions =
    initialSaved !== undefined && initialApplied !== undefined
      ? { saved: initialSaved, applied: initialApplied }
      : null;
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

        {/* F4-16 — matchnings-sektionen ovanför Annonsbeskrivning (design §2.A:
            "passar jobbet mig" är frågan modalen öppnas för → före annons-prosan).
            Renderas bara när matchdata finns (anonym/gäst → match=undefined → null). */}
        {match != null && <JobAdMatchSection match={match} />}

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
        {userActions && (
          <>
            <SaveJobAdToggle jobAdId={jobAd.id} initialSaved={userActions.saved} />
            <HarAnsoktButton jobAdId={jobAd.id} initialApplied={userActions.applied} />
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
      {userActions?.applied && (
        <p
          className="jp-muted"
          style={{
            marginTop: 8,
            fontSize: 13,
            color: "var(--jp-ink-2)",
            textAlign: "right",
          }}
        >
          Du har markerat denna annons som ansökt. Se i{" "}
          <Link
            href="/ansokningar"
            style={{ color: "var(--jp-link, currentColor)", textDecoration: "underline" }}
          >
            Mina ansökningar
          </Link>
          .
        </p>
      )}
    </>
  );
}

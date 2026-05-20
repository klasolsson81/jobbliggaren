"use client";

import Link from "next/link";
import { getJobSourceLabel } from "@/lib/job-ads/status";
import type { JobAdDto } from "@/lib/dto/job-ads";
import { JobTags, computeFreshnessLabel } from "./job-tags";
import { useReadJobAds } from "./use-read-job-ads";

interface JobAdCardProps {
  jobAd: JobAdDto;
}

function formatDate(iso: string): string {
  // CLAUDE.md §10.2 — svensk locale (sv-SE).
  return new Date(iso).toLocaleDateString("sv-SE");
}

/**
 * v3 jobbrad (`.jp-job`). Hela raden är en Link till `/jobb/[id]` — vid
 * soft-nav fångar `@modal/(.)jobb/[id]` den och visar modal; vid hard-nav
 * / delad länk renderas fullsidan (ADR 0053). Länk (ej div+onClick) ger
 * tangentbordsnåbarhet och rätt semantik utan extra ARIA (CLAUDE.md
 * §5.2 / jobbpilot-design-a11y).
 *
 * `jp-job ≡ jp-app` visuell paritet (HANDOVER §5.3 / §9): samma .jp-job-
 * CSS, ingen avvikande markup. Spara-knapp deferred (FE-action-fas).
 *
 * Tagg-system (pre-F6 Prompt 1, 2026-05-20): NY/färskhet/match-placeholder
 * renderas högerjusterat inom `.jp-job__title` h3 via `JobTags` (CTO-dom
 * 2026-05-20, Variant D). Freshness-strängen beräknas server-stil-stabilt
 * (Date.parse — render-deterministisk för en given DTO och nu-tidpunkt).
 *
 * Client-komponent: krävs för NY-taggens localStorage-driven läst-state samt
 * onClick-markering vid navigation. Den initiala HTML-renderingen sker
 * fortfarande server-side (Next App Router) — "use client" ändrar inte att
 * SSR producerar markup, bara att komponenten hydreras klient-side.
 */
export function JobAdCard({ jobAd }: JobAdCardProps) {
  const publishedAt = formatDate(jobAd.publishedAt);
  const expiresAt = jobAd.expiresAt ? formatDate(jobAd.expiresAt) : null;
  const freshnessLabel = computeFreshnessLabel(jobAd.publishedAt);
  const { markRead } = useReadJobAds();

  return (
    <Link
      href={`/jobb/${jobAd.id}`}
      className="jp-job"
      aria-label={`${jobAd.title} – ${jobAd.companyName}`}
      onClick={() => markRead(jobAd.id)}
    >
      <div className="jp-job__body">
        <h3 className="jp-job__title">
          <span>{jobAd.title}</span>
          <JobTags
            jobAdId={jobAd.id}
            showNew={jobAd.isNew}
            freshnessLabel={freshnessLabel}
            // TODO: Fas 4 — koppla mot CV-match-domän + tröskel-beslut
            // Klas (ADR 0053 amendment: match-score är Fas 4-gated). I
            // Prompt 1 alltid undefined → "Bra match"-taggen renderas aldrig.
            matchScore={undefined}
          />
        </h3>
        <div className="jp-job__company">{jobAd.companyName}</div>
        <div className="jp-job__meta">
          <span>{getJobSourceLabel(jobAd.source)}</span>
          <span>
            Publicerad <b>{publishedAt}</b>
          </span>
          {expiresAt && (
            <span>
              Sista ansökan <b>{expiresAt}</b>
            </span>
          )}
        </div>
      </div>
    </Link>
  );
}

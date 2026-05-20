import Link from "next/link";
import { getJobSourceLabel } from "@/lib/job-ads/status";
import type { JobAdDto } from "@/lib/dto/job-ads";
import { JobTags, computeFreshnessLabel } from "./job-tags";

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
 * renderas högerjusterat inom `.jp-job__title` h3 via `JobTags`-client-island
 * (CTO-dom 2026-05-20, Variant D). JobAdCard förblir RSC — tagg-island
 * hydrerar self-contained. NY-modell: high-water mark via lastSeen-timestamp
 * markerad av `<MarkJobbVisited />`-island på sidnivå (Klas-direktiv
 * 2026-05-20 — per-annons "läst" gjorde gamla oöppnade annonser röriga).
 */
export function JobAdCard({ jobAd }: JobAdCardProps) {
  const publishedAt = formatDate(jobAd.publishedAt);
  const expiresAt = jobAd.expiresAt ? formatDate(jobAd.expiresAt) : null;
  const freshnessLabel = computeFreshnessLabel(jobAd.publishedAt);
  const publishedAtMs = Date.parse(jobAd.publishedAt);

  return (
    <Link
      href={`/jobb/${jobAd.id}`}
      className="jp-job"
      aria-label={`${jobAd.title} – ${jobAd.companyName}`}
    >
      <div className="jp-job__body">
        <h3 className="jp-job__title">
          <span>{jobAd.title}</span>
          <JobTags
            showNew={jobAd.isNew}
            publishedAtMs={publishedAtMs}
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

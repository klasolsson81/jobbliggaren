import Link from "next/link";
import { getJobSourceLabel } from "@/lib/job-ads/status";
import type { JobAdDto } from "@/lib/dto/job-ads";

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
 * CSS, ingen avvikande markup. INGEN match-chip (ingen match-domän, ADR
 * 0053 amendment), INGEN spara-knapp (FE-action-fas deferrad — frånvaro,
 * ej disabled-teater).
 */
export function JobAdCard({ jobAd }: JobAdCardProps) {
  const publishedAt = formatDate(jobAd.publishedAt);
  const expiresAt = jobAd.expiresAt ? formatDate(jobAd.expiresAt) : null;

  return (
    <Link
      href={`/jobb/${jobAd.id}`}
      className="jp-job"
      aria-label={`${jobAd.title} – ${jobAd.companyName}`}
    >
      <div className="jp-job__body">
        <h3 className="jp-job__title">
          {jobAd.isNew && <span className="jp-job__newflag">Ny</span>}
          <span>{jobAd.title}</span>
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

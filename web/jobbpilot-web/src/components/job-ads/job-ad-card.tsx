import { JobAdStatusBadge } from "./job-ad-status-badge";
import { getJobSourceLabel } from "@/lib/job-ads/status";
import type { JobAdDto } from "@/lib/dto/job-ads";

interface JobAdCardProps {
  jobAd: JobAdDto;
}

function formatDate(iso: string): string {
  // Datum-formatering enligt CLAUDE.md §10.2 (svensk locale, sv-SE).
  return new Date(iso).toLocaleDateString("sv-SE");
}

function truncateDescription(text: string, max = 200): string {
  if (text.length <= max) return text;
  // Klipp vid ord-gräns inom max-fönstret för att undvika att klippa mitt i ord.
  const slice = text.slice(0, max);
  const lastSpace = slice.lastIndexOf(" ");
  return (lastSpace > 0 ? slice.slice(0, lastSpace) : slice) + "…";
}

export function JobAdCard({ jobAd }: JobAdCardProps) {
  const publishedAt = formatDate(jobAd.publishedAt);
  const expiresAt = jobAd.expiresAt ? formatDate(jobAd.expiresAt) : null;

  return (
    <article className="flex flex-col gap-3 rounded-md border border-border bg-card px-4 py-4 text-sm">
      <header className="flex items-start justify-between gap-3">
        <div className="flex flex-col gap-1">
          <h3 className="font-heading text-base leading-snug font-medium text-text-primary">
            {jobAd.title}
          </h3>
          <p className="text-body-sm text-text-secondary">{jobAd.companyName}</p>
        </div>
        <JobAdStatusBadge status={jobAd.status} />
      </header>

      <p className="text-body text-text-secondary">
        {truncateDescription(jobAd.description)}
      </p>

      <dl className="flex flex-wrap gap-x-4 gap-y-1 text-body-sm text-text-secondary">
        <div className="flex gap-1">
          <dt>Publicerad:</dt>
          <dd>{publishedAt}</dd>
        </div>
        {expiresAt && (
          <div className="flex gap-1">
            <dt>Sista ansökningsdag:</dt>
            <dd>{expiresAt}</dd>
          </div>
        )}
        <div className="flex gap-1">
          <dt>Källa:</dt>
          <dd>{getJobSourceLabel(jobAd.source)}</dd>
        </div>
      </dl>

      {jobAd.url && (
        <p className="text-body-sm">
          <a
            href={jobAd.url}
            target="_blank"
            rel="noopener noreferrer"
            className="text-brand-700 underline underline-offset-2 hover:text-brand-600"
          >
            Läs hela annonsen
          </a>
        </p>
      )}
    </article>
  );
}

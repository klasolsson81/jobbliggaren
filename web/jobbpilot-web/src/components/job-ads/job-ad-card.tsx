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
    <article className="flex flex-col gap-2 border-b border-border-strong px-3 py-6 text-sm transition-colors duration-75 last:border-b-0 hover:bg-surface-tertiary hover:[box-shadow:inset_3px_0_0_var(--jp-brand-600)]">
      <header className="flex items-start justify-between gap-4">
        <div className="flex min-w-0 flex-col gap-0.5">
          <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
            <h3 className="text-[15px] leading-snug font-medium tracking-[-0.005em] text-text-primary">
              {jobAd.title}
            </h3>
            {/* ADR 0042 Beslut E — "Ny"-badge. Civic-utility pill-mönster
                (text + färg, ingen emoji), brand-accent som signalerar
                "ny sedan ditt fönster". isNew===false döljer helt. */}
            {jobAd.isNew && (
              <span className="inline-flex items-center rounded-pill bg-brand-50 px-2 py-0.5 text-xs font-medium text-brand-700">
                Ny
              </span>
            )}
          </div>
          <p className="text-body-sm text-text-secondary">{jobAd.companyName}</p>
        </div>
        <JobAdStatusBadge status={jobAd.status} />
      </header>

      <p className="max-w-[68ch] text-body-sm text-text-secondary">
        {truncateDescription(jobAd.description)}
      </p>

      <dl className="font-mono mt-1 flex flex-wrap gap-x-5 gap-y-1 text-[13px] text-text-secondary">
        <div className="flex gap-1.5">
          <dt>Publicerad:</dt>
          <dd className="text-text-secondary">{publishedAt}</dd>
        </div>
        {expiresAt && (
          <div className="flex gap-1.5">
            <dt>Sista ansökningsdag:</dt>
            <dd className="text-text-secondary">{expiresAt}</dd>
          </div>
        )}
        <div className="flex gap-1.5">
          <dt>Källa:</dt>
          <dd className="text-text-secondary">{getJobSourceLabel(jobAd.source)}</dd>
        </div>
      </dl>

      {jobAd.url && (
        <p className="text-body-sm">
          <a
            href={jobAd.url}
            target="_blank"
            rel="noopener noreferrer"
            className="text-brand-700 underline underline-offset-[3px] hover:text-brand-600"
          >
            Läs hela annonsen
          </a>
        </p>
      )}
    </article>
  );
}

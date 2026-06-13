import Link from "next/link";
import { getJobSourceLabel } from "@/lib/job-ads/status";
import type { JobAdDto } from "@/lib/dto/job-ads";
import { JobTags } from "./job-tags";
import { computeFreshnessLabel } from "./freshness";

interface JobAdCardProps {
  jobAd: JobAdDto;
  /** PR5 — per-user overlay-status (ADR 0063 batch-port). */
  isSaved?: boolean;
  isApplied?: boolean;
}

function formatDate(iso: string): string {
  // CLAUDE.md §10.2 — svensk locale (sv-SE).
  return new Date(iso).toLocaleDateString("sv-SE");
}

/**
 * PR5 Klas-feedback 2026-05-23 — Platsbanken-paritet: visa klockslag på
 * publicerad-tidsstämpeln. Idag → "idag, kl. HH.MM"; igår → "igår, kl. HH.MM";
 * äldre → "YYYY-MM-DD, kl. HH.MM". Hjälper användaren skilja annonser som
 * postas under dagen (flera hundra dagligen).
 */
function formatPublishedAtWithTime(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;

  const time = date.toLocaleTimeString("sv-SE", {
    hour: "2-digit",
    minute: "2-digit",
  });

  const now = new Date();
  const isToday =
    date.getFullYear() === now.getFullYear() &&
    date.getMonth() === now.getMonth() &&
    date.getDate() === now.getDate();

  if (isToday) return `idag, kl. ${time}`;

  const yesterday = new Date(now);
  yesterday.setDate(now.getDate() - 1);
  const isYesterday =
    date.getFullYear() === yesterday.getFullYear() &&
    date.getMonth() === yesterday.getMonth() &&
    date.getDate() === yesterday.getDate();

  if (isYesterday) return `igår, kl. ${time}`;

  return `${date.toLocaleDateString("sv-SE")}, kl. ${time}`;
}

/**
 * v3 jobbrad (`.jp-job`). Hela raden är en Link till `/jobb/[id]` — vid
 * soft-nav fångar `@modal/(.)jobb/[id]` den och visar modal; vid hard-nav
 * / delad länk renderas fullsidan (ADR 0053). Länk (ej div+onClick) ger
 * tangentbordsnåbarhet och rätt semantik utan extra ARIA (CLAUDE.md
 * §5.2 / jobbliggaren-design-a11y).
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
export function JobAdCard({ jobAd, isSaved = false, isApplied = false }: JobAdCardProps) {
  const publishedAt = formatPublishedAtWithTime(jobAd.publishedAt);
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
            isSaved={isSaved}
            isApplied={isApplied}
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

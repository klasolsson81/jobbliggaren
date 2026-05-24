import Link from "next/link";
import { getJobSourceLabel } from "@/lib/job-ads/status";
import type { GuestMockJobAd } from "@/lib/guest/mock-data";

// F-Pre Punkt 5b 2026-05-24 — gäst-variant av JobAdCard. Länk pekar mot
// `/gast/jobb/[id]` (gäst-tree-isolering — får aldrig länka till
// `/jobb/[id]` som är auth-gated). Återanvänder `.jp-job`-CSS-chassi
// (delad med live, HANDOVER §5.3) men utan JobTags-island (mockdata
// behöver inga NY/färskhet-tags som faller från BE).

function formatPublishedAt(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return date.toLocaleDateString("sv-SE", {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  });
}

function formatExpires(iso: string | null): string | null {
  if (!iso) return null;
  return new Date(iso).toLocaleDateString("sv-SE");
}

export function GuestJobAdCard({ jobAd }: { jobAd: GuestMockJobAd }) {
  const publishedAt = formatPublishedAt(jobAd.publishedAtIso);
  const expiresAt = formatExpires(jobAd.expiresAtIso);

  return (
    <Link
      href={`/gast/jobb/${jobAd.id}`}
      className="jp-job"
      aria-label={`${jobAd.title} – ${jobAd.companyName}`}
    >
      <div className="jp-job__body">
        <h3 className="jp-job__title">
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

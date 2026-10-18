import { MapPin } from "lucide-react";
import { useFormatter, useTranslations } from "next-intl";
import type { PublicJobAdSummary } from "@/lib/dto/public-job-ad-catalogue";
import { formatDate } from "@/lib/i18n/format";

interface PublicJobAdListProps {
  rows: readonly PublicJobAdSummary[];
  /** The id of the count line that names the list. */
  labelledBy: string;
}

const named = (name: string | null): name is string => name !== null && name.trim() !== "";

/**
 * The public catalogue's rows (ADR 0175 F5). Typed to the six-member summary, so it can render nothing else: never the
 * private `JobAdCard`, whose props carry save, follow, applied and match state.
 *
 * Each title is a plain `<a>`, never `next/link` (F10): a prefetched row would spend the visitor's job-page budget
 * before any click, and a client navigation into the edge's rewrite was never measured. A full document load is the
 * measured path, and for a signed-in visitor it opens the private page, never a modal.
 */
export function PublicJobAdList({ rows, labelledBy }: PublicJobAdListProps) {
  const t = useTranslations("publicjob.catalogue");
  const format = useFormatter();

  return (
    <ol className="jp-jobs mt-4" aria-labelledby={labelledBy}>
      {rows.map((row) => {
        const idBase = `catalogue-${row.id}`;
        const place = named(row.municipalityName) ? row.municipalityName : named(row.regionName) ? row.regionName : null;
        const published = formatDate(format, row.publishedAt);

        return (
          <li key={row.id}>
            <article className="jp-job jp-job--single">
              <div className="jp-job__body">
                <h2 className="jp-job__title">
                  <a
                    href={`/jobb/${row.id}`}
                    className="jp-job__rowlink"
                    aria-describedby={`${idBase}-company ${idBase}-meta`}
                  >
                    {row.title}
                  </a>
                </h2>
                <div id={`${idBase}-company`} className="jp-job__company">
                  {row.companyName}
                </div>
                <div id={`${idBase}-meta`} className="jp-job__meta">
                  {place !== null && (
                    <span className="inline-flex items-center gap-1">
                      <MapPin size={14} aria-hidden="true" />
                      <span>
                        <span className="sr-only">{`${t("location")} `}</span>
                        {place}
                      </span>
                    </span>
                  )}
                  {published !== null && (
                    // Label and space in one text node: Chrome drops a whitespace-only node between label and value
                    // from the link's computed description (#1828).
                    <span>
                      {`${t("published")} `}
                      <b>
                        <time dateTime={row.publishedAt}>{published}</time>
                      </b>
                    </span>
                  )}
                </div>
              </div>
            </article>
          </li>
        );
      })}
    </ol>
  );
}

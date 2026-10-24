import { MapPin } from "lucide-react";
import { useFormatter, useTranslations } from "next-intl";
import type { PublicJobAdSummary } from "@/lib/dto/public-job-ad-catalogue";
import { formatDate } from "@/lib/i18n/format";

interface PublicJobAdListProps {
  rows: readonly PublicJobAdSummary[];
  /** The id of the element that names the list. */
  labelledBy: string;
  /** The rows' heading level: 2 under a page's own h1, 3 under a section's h2. */
  headingLevel: 2 | 3;
  /** Prefixes every id a row carries, so two lists never share one. */
  idPrefix: "catalogue" | "related";
}

const named = (name: string | null): name is string => name !== null && name.trim() !== "";

/**
 * The public catalogue's rows (ADR 0176 F5), and the related section's under a job page (ADR 0182). Typed to the
 * six-member summary, so it can render nothing else: never the private `JobAdCard`, whose props carry save, follow,
 * applied and match state.
 *
 * Each title is a plain `<a>`, never `next/link` (F10): a prefetched row would spend the visitor's job-page budget
 * before any click, and a client navigation into the edge's rewrite was never measured. A full document load is the
 * measured path, and for a signed-in visitor it opens the private page, never a modal.
 */
export function PublicJobAdList({ rows, labelledBy, headingLevel, idPrefix }: PublicJobAdListProps) {
  const t = useTranslations("publicjob.catalogue");
  const format = useFormatter();
  const Heading = headingLevel === 2 ? "h2" : "h3";

  return (
    <ol className="jp-jobs mt-4" aria-labelledby={labelledBy}>
      {rows.map((row) => {
        const idBase = `${idPrefix}-${row.id}`;
        const place = named(row.municipalityName) ? row.municipalityName : named(row.regionName) ? row.regionName : null;
        const published = formatDate(format, row.publishedAt);

        return (
          <li key={row.id}>
            <article className="jp-job jp-job--single">
              <div className="jp-job__body">
                <Heading className="jp-job__title">
                  <a
                    href={`/jobb/${row.id}`}
                    className="jp-job__rowlink"
                    aria-describedby={`${idBase}-company ${idBase}-meta`}
                  >
                    {row.title}
                  </a>
                </Heading>
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

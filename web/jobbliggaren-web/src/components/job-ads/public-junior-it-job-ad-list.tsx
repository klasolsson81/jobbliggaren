import { MapPin } from "lucide-react";
import { useFormatter, useTranslations } from "next-intl";
import type { PublicJuniorItJobAd } from "@/lib/dto/public-junior-it-job-ads";
import { formatDate } from "@/lib/i18n/format";

const named = (name: string | null): name is string => name !== null && name.trim() !== "";

/**
 * The open junior IT list's rows (ADR 0186): the catalogue row's values and the last day to apply, which the list sorts
 * on. Typed to the seven-member row, so it can render nothing else, and kept apart from `PublicJobAdList`, whose six
 * members ADR 0176 pins.
 *
 * Each title is a plain `<a>`, never `next/link`: a prefetched row would spend the visitor's job-page budget before any
 * click. A full document load opens the public job page, or the private one for a signed-in visitor, where the ad can
 * be saved and marked as applied.
 */
export function PublicJuniorItJobAdList({
  rows,
  labelledBy,
}: {
  rows: readonly PublicJuniorItJobAd[];
  /** The id of the element that names the list. */
  labelledBy: string;
}) {
  const tRow = useTranslations("publicjob.catalogue");
  const t = useTranslations("publicjob.junior");
  const format = useFormatter();

  return (
    <ol className="jp-jobs mt-4" aria-labelledby={labelledBy}>
      {rows.map((row) => {
        const idBase = `junior-${row.id}`;
        const place = named(row.municipalityName) ? row.municipalityName : named(row.regionName) ? row.regionName : null;
        const published = formatDate(format, row.publishedAt);
        const lastDay = row.expiresAt === null ? null : formatDate(format, row.expiresAt);

        return (
          <li key={row.id}>
            <article className="jp-job jp-job--single">
              <div className="jp-job__body">
                <h2 className="jp-job__title">
                  <a href={`/jobb/${row.id}`} className="jp-job__rowlink" aria-describedby={`${idBase}-company ${idBase}-meta`}>
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
                        <span className="sr-only">{`${tRow("location")} `}</span>
                        {place}
                      </span>
                    </span>
                  )}
                  {published !== null && (
                    // Label and space in one text node: Chrome drops a whitespace-only node between label and value
                    // from the link's computed description (#1828).
                    <span>
                      {`${tRow("published")} `}
                      <b>
                        <time dateTime={row.publishedAt}>{published}</time>
                      </b>
                    </span>
                  )}
                  {lastDay !== null && row.expiresAt !== null && (
                    <span>
                      {`${t("lastApplication")} `}
                      <b>
                        <time dateTime={row.expiresAt}>{lastDay}</time>
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

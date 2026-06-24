import Link from "next/link";
import { useTranslations } from "next-intl";
import { ExternalLink } from "lucide-react";
import type { MatchList as MatchListData } from "@/lib/dto/me-matches";
import { formatSwedishShortDateWithYear } from "@/lib/oversikt/aggregations";
import { MatchChip } from "@/components/job-ads/match-chip";

interface MatchListProps {
  items: MatchListData;
}

/**
 * ADR 0080 Vag 4 PR-5 — listan på `/matchningar`: användarens persisterade
 * bakgrundsmatchningar, nyast först (cap:at 50 av backend). REN presentations-
 * komponent, ingen client-state → Server Component (hydrerar inget; paritet
 * `MatchChip`).
 *
 * Designkontrakt (civic-utility, modern-civic):
 * - Graden visas via den DELADE `MatchChip` (`.jp-matchchip`) — en NAMNGIVEN
 *   kategori (Bra/Stark/Topp), aldrig en siffra/procent/mätare (Goodhart-vakt,
 *   ADR 0071/0076/0080). `NotifiableMatchGrade` (Good/Strong/Top) är en delmängd
 *   av `MatchGrade` så chip:en återanvänds direkt utan ny komponent.
 * - "Ny" = den befintliga `.jp-tag jp-tag--accent`-taggen (rektangulär, 11px
 *   versaler). TEXTEN "Ny" bär betydelsen → färg är aldrig ensam signal
 *   (WCAG 1.4.1); en `aria-label` ger skärmläsaren full kontext.
 * - Titeln länkar till den interna annonsdetaljen (`/jobb/{id}`, paritet
 *   `/sparade`); den externa annons-URL:en yttas som sekundär `ExternalLink`-
 *   action ENBART när den finns.
 */
export function MatchList({ items }: MatchListProps) {
  const t = useTranslations("pages.matchningar");
  // Aria-label for the icon-only external link reuses the proven jobads key
  // (visible text would duplicate the icon's meaning — civic, no label clutter).
  const tJobads = useTranslations("jobads.saved");

  if (items.length === 0) {
    return (
      <div className="jp-empty">
        <div className="jp-empty__title">{t("emptyTitle")}</div>
        <p>{t("emptyBody")}</p>
      </div>
    );
  }

  return (
    <ul className="jp-jobs" aria-label={t("listLabel")}>
      {items.map((item) => (
        <li key={item.jobAdId}>
          <article
            className="jp-job"
            style={{ gridTemplateColumns: "1fr auto" }}
          >
            <div className="jp-job__body">
              <h3 className="jp-job__title">
                <Link
                  href={`/jobb/${item.jobAdId}`}
                  style={{ color: "inherit", textDecoration: "none" }}
                >
                  {item.title}
                </Link>
                {item.isNew && (
                  <span
                    className="jp-tag jp-tag--accent"
                    data-tag="new"
                    aria-label={t("newBadgeAriaLabel")}
                  >
                    {t("newBadge")}
                  </span>
                )}
              </h3>
              <div className="jp-job__company">{item.company}</div>
              <div className="jp-job__meta">
                <span>
                  {t("matchedAt")}{" "}
                  <b>{formatSwedishShortDateWithYear(item.createdAt)}</b>
                </span>
              </div>
            </div>
            <div
              className="jp-job__actions"
              style={{ flexDirection: "row", alignItems: "center" }}
            >
              <MatchChip grade={item.grade} />
              {item.url && (
                <a
                  href={item.url}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="jp-icon-btn"
                  aria-label={tJobads("openExternal")}
                >
                  <ExternalLink size={16} aria-hidden="true" />
                </a>
              )}
            </div>
          </article>
        </li>
      ))}
    </ul>
  );
}

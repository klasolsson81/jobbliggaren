import Link from "next/link";
import { useTranslations } from "next-intl";
import { ExternalLink } from "lucide-react";
import type { MatchList as MatchListData } from "@/lib/dto/me-matches";
import { formatSwedishShortDateWithYear } from "@/lib/oversikt/aggregations";
import { MatchChip } from "@/components/job-ads/match-chip";

interface MatchListProps {
  items: MatchListData;
}

// Backend hard-caps the persisted match list at 50 (`GetMyMatchesQueryHandler`
// `MaxItems`, #273 — intentional cap; `GetMyNewMatchCount` stays uncapped). When
// the list fills the window a heavy opted-in user would otherwise read 50 as the
// total, so #424 surfaces the bound honestly beneath the list.
const MATCH_LIST_CAP = 50;

// The bounded-window hint links to /jobb filtered to the two NOTIFIABLE grades
// that ARE filterable there (Good + Strong). `Top` is honest-by-design NOT
// filterable on /jobb — `LIST_MATCH_GRADES` excludes it because the cacheable
// Fast band cannot compute Toppmatch (#291), and the list validator 400s `Top`.
// So the copy points to "fler matchande jobb" rather than claiming to surface
// every match (a Top-graded overflow ad stays reachable only as a card badge).
const MORE_MATCHES_HREF = "/jobb?matchGrades=Good&matchGrades=Strong";

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
 * - "Ny" = den delade `.jp-tag`-taggen med `data-tag="new"` (rektangulär,
 *   11px versaler). Sedan #290 renderas den som en NEUTRAL chip
 *   (--jp-surface-3 / ink-1) — grönt är reserverat för match-grad-chip:en, så
 *   `[data-tag="new"]`-overriden flyttar NY av den gamla leaf-gröna globalt
 *   (avsiktligt tvär-yta per CTO). TEXTEN "Ny" bär betydelsen → färg är aldrig
 *   ensam signal (WCAG 1.4.1); en `sr-only`-text ger skärmläsaren full kontext
 *   (`aria-label` är ogiltig på en generisk span / role=generic).
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

  // #424: the list is the front of a window the backend capped at 50. When it is
  // full, name the bound (honest-data ethos, CLAUDE.md §5) and point to where more
  // matching jobs are browsable — never let 50 read as the total.
  const atCap = items.length === MATCH_LIST_CAP;

  return (
    <>
      <ul className="jp-jobs" aria-label={t("listLabel")}>
        {items.map((item) => (
          <li key={item.jobAdId}>
            <article
              className="jp-job"
              style={{ gridTemplateColumns: "1fr auto" }}
            >
              <div className="jp-job__body">
                <h3 className="jp-job__title">
                  <Link href={`/jobb/${item.jobAdId}`} className="text-inherit no-underline">
                    {item.title}
                  </Link>
                  {item.isNew && (
                    <span className="jp-tag jp-tag--accent" data-tag="new">
                      {t("newBadge")}
                      <span className="sr-only">{t("newBadgeAriaLabel")}</span>
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
      {atCap && (
        <p className="jp-matchsort-note">
          {t("boundedNote", { count: MATCH_LIST_CAP })}{" "}
          <Link href={MORE_MATCHES_HREF} className="jp-matchsort-note__link">
            {t("boundedLink")}
          </Link>
        </p>
      )}
    </>
  );
}

import Link from "next/link";
import { useFormatter, useTranslations } from "next-intl";
import { jobSourceLabel } from "@/lib/job-ads/status";
import { formatDate, formatTime, type JpFormatter } from "@/lib/i18n/format";
import type { JobAdDto } from "@/lib/dto/job-ads";
import type { MatchGrade } from "@/lib/dto/job-ad-match";
import { JobTags } from "./job-tags";
import { MatchChip } from "./match-chip";
import { computeFreshnessLabel } from "./freshness";

interface JobAdCardProps {
  jobAd: JobAdDto;
  /**
   * NY = oläst (per-användar watermark, #293/#306). Beräknas i `JobbResults`
   * (`createdAt > lastSeenJobsAt`) och bärs ner via `JobAdList`s `newIdSet`.
   * Default false (kall start / anon / list-yta utan auth → ingen NY).
   */
  isNew?: boolean;
  /** PR5 — per-user overlay-status (ADR 0063 batch-port). */
  isSaved?: boolean;
  isApplied?: boolean;
  /**
   * F4-13 (ADR 0076) — graderad match-tagg (server-fetchad via
   * `getJobAdMatchTags`). `undefined` = ingen positiv grad ⇒ ingen chip
   * (POSITIVE-ONLY). Aldrig en siffra — graden är en namngiven kategori.
   */
  matchGrade?: MatchGrade;
}

/**
 * PR5 Klas-feedback 2026-05-23 — Platsbanken-paritet: visa klockslag på
 * publicerad-tidsstämpeln. Idag → "idag, kl. HH.MM"; igår → "igår, kl. HH.MM";
 * äldre → "<datum>, kl. HH.MM". Hjälper användaren skilja annonser som postas
 * under dagen (flera hundra dagligen). Den svenska prosan resolveras via
 * next-intl (`ui.card.published*`); funktionen tar translatorn + den
 * locale-medvetna formattern så den förblir en ren render-helper (anropas i
 * RSC:n med komponentens `t`/`format`).
 */
function formatPublishedAtWithTime(
  iso: string,
  t: ReturnType<typeof useTranslations<"jobads.ui.card">>,
  format: JpFormatter,
): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;

  const time = formatTime(format, date);

  const now = new Date();
  const isToday =
    date.getFullYear() === now.getFullYear() &&
    date.getMonth() === now.getMonth() &&
    date.getDate() === now.getDate();

  if (isToday) return t("publishedToday", { time });

  const yesterday = new Date(now);
  yesterday.setDate(now.getDate() - 1);
  const isYesterday =
    date.getFullYear() === yesterday.getFullYear() &&
    date.getMonth() === yesterday.getMonth() &&
    date.getDate() === yesterday.getDate();

  if (isYesterday) return t("publishedYesterday", { time });

  return t("publishedOlder", {
    date: formatDate(format, iso) ?? iso,
    time,
  });
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
 * Tagg-system (pre-F6 Prompt 1, 2026-05-20): NY/färskhet/match renderas
 * högerjusterat inom `.jp-job__title` h3 via `JobTags` (CTO-dom 2026-05-20,
 * Variant D). NY-modell (#293/#306, ADR 0042 Beslut E-amendment 2026-06-28):
 * NY = OLÄST (per-användar watermark, beräknad i `JobbResults` mot
 * `lastSeenJobsAt`), INTE tidsbaserat — den tidigare localStorage-high-water-
 * mark-modellen + `<MarkJobbVisited />`-island är borttagna (watermarken bor
 * server-side nu, spegling av /matchningar).
 */
export function JobAdCard({
  jobAd,
  isNew = false,
  isSaved = false,
  isApplied = false,
  matchGrade,
}: JobAdCardProps) {
  // Synchronous next-intl translators — keep JobAdCard a non-async RSC (it
  // renders as a serialized list slot and has synchronous render tests).
  const t = useTranslations("jobads.enums");
  const tUi = useTranslations("jobads.ui.card");
  const format = useFormatter();
  const publishedAt = formatPublishedAtWithTime(jobAd.publishedAt, tUi, format);
  const expiresAt = formatDate(format, jobAd.expiresAt);
  const freshnessLabel = computeFreshnessLabel(jobAd.publishedAt);

  return (
    <Link
      href={`/jobb/${jobAd.id}`}
      className="jp-job"
      aria-label={tUi("ariaLabel", {
        title: jobAd.title,
        company: jobAd.companyName,
      })}
    >
      <div className="jp-job__body">
        <h3 className="jp-job__title">
          <span>{jobAd.title}</span>
          <JobTags
            isNew={isNew}
            freshnessLabel={freshnessLabel}
            isSaved={isSaved}
            isApplied={isApplied}
          />
          {/* F4-13 (ADR 0076) — graderad match-tagg. POSITIVE-ONLY: renderas
              bara när annonsen har en grad. Lever i titel-radens flex-wrap
              bredvid JobTags; `.jp-job-tags` har redan margin-left:auto, så
              chip:en lägger sig efter tagg-blocket högerjusterat. */}
          {matchGrade && <MatchChip grade={matchGrade} />}
        </h3>
        <div className="jp-job__company">{jobAd.companyName}</div>
        <div className="jp-job__meta">
          <span>{jobSourceLabel(t, jobAd.source)}</span>
          <span>
            {tUi("published")} <b>{publishedAt}</b>
          </span>
          {expiresAt && (
            <span>
              {tUi("lastApplication")} <b>{expiresAt}</b>
            </span>
          )}
        </div>
      </div>
    </Link>
  );
}

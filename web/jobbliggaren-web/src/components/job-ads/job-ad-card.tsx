import Link from "next/link";
import { useFormatter, useTranslations } from "next-intl";
import { jobSourceLabel } from "@/lib/job-ads/status";
import { formatDate, formatTime, type JpFormatter } from "@/lib/i18n/format";
import type { JobAdDto } from "@/lib/dto/job-ads";
import type { MatchGrade } from "@/lib/dto/job-ad-match";
import { hasJobTags, JobTags } from "./job-tags";
import { MatchChip } from "./match-chip";

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
   * #1000 (V1) — BEVAKAR = du bevakar annonsens arbetsgivare. Per-user-overlay
   * (`getFollowedJobAdIds`), buret via `JobAdList`s `followedIdSet`. Driver BÅDE
   * BEVAKAR-taggen (JobTags) OCH kortets `data-followed`-vänsteredge (`.jp-job`).
   * Default false (anon / arbetsgivare du inte följer ⇒ ingen markör).
   */
  isFollowed?: boolean;
  /**
   * F4-13 (ADR 0076) — graderad match-tagg (server-fetchad via
   * `getJobAdMatchTags`). `undefined` = ingen positiv grad ⇒ ingen chip
   * (POSITIVE-ONLY). Aldrig en siffra — graden är en namngiven kategori.
   */
  matchGrade?: MatchGrade;
  /**
   * #446 (#311) — antalet av den inloggade användarens EGNA tidigare (inskickade)
   * ansökningar till annonsens arbetsgivare (samma org.nr), server-resolverat via
   * `getEmployerApplicationCounts`. `undefined`/0 ⇒ ingen badge (POSITIVE-ONLY —
   * mappen bär bara positiva räknare). Ett rent heltal: INGET org.nr färdas i
   * texten, attribut eller URL (enskild firma = personnummer, CLAUDE.md §5). B1
   * (senior-cto-advisor 2026-07-03): informativ text, INTE en länk — länkmålet
   * `/foretag/historik` visar hela historiken, inte den här arbetsgivarens (#1828).
   */
  previousApplicationCount?: number;
  /**
   * #380 — den nuvarande listans query-sträng (filter + match + sort + sök,
   * byggd i `JobbResults` via `buildJobbHref`), utan inledande `?`. Bärs in i
   * radlänken så att soft-nav till modalen ALDRIG tappar list-URL:ens view-
   * state. Den intercepting modalen byter bara `@modal`-slotten; en NAKEN
   * `/jobb/[id]`-länk lät däremot children-slottens `/jobb` re-rendras till
   * tomma searchParams (Suspense-keyn flippade relaterade/grader/matchning till
   * av), och `router.back()` återställer bara modal-slotten ⇒ listan fastnade i
   * av-läget. Med hela list-staten i länken speglar modal-URL:en listan exakt,
   * så öppna→stäng bevarar HELA filter-/match-läget. Default tom = naken länk
   * (gäst-/övriga ytor som inte trådar list-state). Modalen läser bara
   * `relaterade` ur denna; övriga params är inerta på detalj-/modal-sidan.
   */
  listQuery?: string;
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
 * v3 jobbrad (`.jp-job`). Titeln är radens enda länk till `/jobb/[id]` och
 * sträcks över hela kortet via `::after` (`.jp-job__rowlink`, samma overlay som
 * `.jp-app__rowlink`), så hela kortet är klickbart. Vid soft-nav fångar
 * `@modal/(.)jobb/[id]` länken och visar modal; vid hard-nav / delad länk
 * renderas fullsidan (ADR 0053). Länkens namn är titeln; kortets övriga rader är
 * dess beskrivning (`aria-describedby`, i visuell ordning), så Tab ger titel +
 * beskrivning och läsläget läser kortet som vanlig text (design-reviewer #1828).
 *
 * `jp-job ≡ jp-app` visuell paritet (HANDOVER §5.3 / §9): samma .jp-job-
 * CSS. Spara-knapp deferred (FE-action-fas).
 *
 * Tagg-system (pre-F6 Prompt 1, 2026-05-20): NY/färskhet/match renderas
 * VÄNSTERANSATT inom `.jp-job__title` h3 via `JobTags` + `MatchChip` (ADR 0118 —
 * inline-left, ersätter den tidigare högerjusteringen "Variant D" 2026-05-20).
 * NY-modell (#293/#306, ADR 0042 Beslut E-amendment 2026-06-28):
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
  isFollowed = false,
  matchGrade,
  previousApplicationCount,
  listQuery = "",
}: JobAdCardProps) {
  // Synchronous next-intl translators — keep JobAdCard a non-async RSC (it
  // renders as a serialized list slot and has synchronous render tests).
  const t = useTranslations("jobads.enums");
  const tUi = useTranslations("jobads.ui.card");
  const format = useFormatter();
  const publishedAt = formatPublishedAtWithTime(jobAd.publishedAt, tUi, format);
  const expiresAt = formatDate(format, jobAd.expiresAt);

  // #380 — bär list-URL:ens view-state in i radlänken så modal-soft-nav inte
  // tappar filter/match-läget (se `listQuery`-doc). Tom query ⇒ naken länk.
  const href = listQuery ? `/jobb/${jobAd.id}?${listQuery}` : `/jobb/${jobAd.id}`;

  // The ad id is unique within a list, so it keys the description's IDREFs without a hook.
  const idBase = `jobad-${jobAd.id}`;
  const hasCount = previousApplicationCount != null && previousApplicationCount > 0;
  const describedBy = [
    hasJobTags({ isNew, isFollowed, isSaved, isApplied }) ? `${idBase}-tags` : null,
    matchGrade ? `${idBase}-grade` : null,
    `${idBase}-company`,
    hasCount ? `${idBase}-count` : null,
    `${idBase}-meta`,
  ]
    .filter((ref): ref is string => ref !== null)
    .join(" ");

  return (
    <article
      className="jp-job"
      // #1000 (V1) — `data-followed` drives the card's left-edge (`.jp-job[data-followed]::before`,
      // a pseudo-element so it survives the green :hover border). Attribute present iff followed.
      data-followed={isFollowed ? "" : undefined}
    >
      <div className="jp-job__body">
        <h3 className="jp-job__title">
          <Link href={href} className="jp-job__rowlink" aria-describedby={describedBy}>
            {jobAd.title}
          </Link>
          <JobTags
            id={`${idBase}-tags`}
            isNew={isNew}
            isFollowed={isFollowed}
            isSaved={isSaved}
            isApplied={isApplied}
          />
          {/* F4-13 (ADR 0076) — graderad match-tagg. POSITIVE-ONLY: renderas
              bara när annonsen har en grad. Lever SIST i titel-radens flex-wrap,
              efter JobTags (ADR 0118 — inline-left: `.jp-job-tags` har INGEN
              margin-left:auto, så graden håller en konstant ordinal position
              [sist, direkt efter titel + ev. taggar] oavsett om taggraden
              renderar — den byter aldrig sida beroende på follow/spara-status). */}
          {matchGrade && <MatchChip id={`${idBase}-grade`} grade={matchGrade} />}
        </h3>
        <div id={`${idBase}-company`} className="jp-job__company">
          {jobAd.companyName}
        </div>
        {/* #446 (#311) — räknaren, POSITIVE-ONLY (> 0). Rent heltal, inget org.nr i
            text/attribut. #824 PR 4: ett GOLV, aldrig en totalsumma, så "Minst" styr
            talet (ADR 0144 D4 rad 9). */}
        {hasCount && (
          <div id={`${idBase}-count`} className="jp-job__meta">
            <span>
              {tUi("previousApplications", { count: previousApplicationCount })}
            </span>
          </div>
        )}
        <div id={`${idBase}-meta`} className="jp-job__meta">
          {/* Platsbanken is the one source /jobb ingests; another source is information
              and keeps its label. */}
          {jobAd.source !== "Platsbanken" && (
            <span>{jobSourceLabel(t, jobAd.source)}</span>
          )}
          {/* Label and space in ONE text node: Chrome drops a whitespace-only node
              between label and value from the link's computed description
              ("Publiceradidag", measured #1828). */}
          <span>
            {`${tUi("published")} `}
            <b>{publishedAt}</b>
          </span>
          {expiresAt && (
            <span>
              {`${tUi("lastApplication")} `}
              <b>{expiresAt}</b>
            </span>
          )}
        </div>
      </div>
    </article>
  );
}

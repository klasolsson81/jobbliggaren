import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { getJobAds } from "@/lib/api/job-ads";
import { getJobAdStatusBatch } from "@/lib/api/job-ad-status";
import { getJobAdMatchTags } from "@/lib/api/job-ad-match";
import { getEmployerApplicationCounts } from "@/lib/api/employer-application-counts";
import { getMyProfile } from "@/lib/api/me";
import { getJobsWatermark, markJobsSeen } from "@/lib/api/me-jobs";
import { resolveTaxonomyLabels } from "@/lib/api/taxonomy";
import type { JobAdSortBy } from "@/lib/dto/job-ads";
import type { MatchGrade, JobAdMatchBatch } from "@/lib/dto/job-ad-match";
import { assertNever } from "@/lib/dto/_helpers";
import {
  buildJobbHref,
  clampSubMinimumQ,
  parseEmployerParam,
} from "@/lib/job-ads/search-params";
import { maxCreatedAt } from "@/lib/job-ads/seen-window";
import { JobAdList } from "@/components/job-ads/job-ad-list";
import { JobbResultsToolbar } from "@/components/job-ads/jobb-results-toolbar";
import { JobAdPagination } from "@/components/job-ads/job-ad-pagination";

/**
 * Resultatdelen av /jobb (F6 P4).
 *
 * Detta är den enda delen av /jobb som hänger på `getJobAds()`. Den är
 * extraherad till en egen `async` Server Component så att `jobb/page.tsx`
 * kan rendera hero (sökfält, filter-pills) synkront och wrappa ENBART
 * denna komponent i `<Suspense fallback={<JobAdListSkeleton />}>`.
 *
 * Effekten: under en sökning byts bara resultat-ytan mot skeleton —
 * sökfältet användaren just använde, hero:n och sidans chrome förblir
 * renderade. Detta är den idiomatiska Next.js streaming-patternen och
 * ersätter det tidigare `loading.tsx`, som var en route-segment-fallback
 * och därför raderade HELA /jobb-segmentet (inklusive hero + träffräknare)
 * vid varje sökning (design-reviewer F6 P4 B1).
 *
 * Träffräknaren bor i `JobbResultsToolbar` och är data-beroende
 * (`totalCount` + filter-chip-labels). Den kan därför inte ligga utanför
 * Suspense-gränsen — den skulle inte kunna visa rätt antal innan
 * `getJobAds()` landat. Toolbaren renderas alltså tillsammans med listan
 * här inne, och `JobAdListSkeleton` speglar toolbar-raden så layouten
 * inte hoppar när data landar.
 *
 * `resolveTaxonomyLabels` hämtas också här: chip-labels i toolbaren beror
 * på de valda concept-id:na och hör ihop med resultat-renderingen. Träd-
 * och senaste-sökningar-hämtning ligger kvar i `page.tsx` (hero-beroenden
 * som måste renderas synkront).
 */

interface JobbResultsProps {
  page: number;
  pageSize: number;
  sortBy: JobAdSortBy;
  occupationGroup: string[];
  region: string[];
  municipality: string[];
  // Klass 2 (2026-06-13) — anställningsform + omfattning.
  employmentType: string[];
  worktimeExtent: string[];
  // STEG 5 (grade-filter, 2026-06-23) — valda matchningsgrader (enum-namn,
  // delmängd av Basic/Good/Strong; validerad + Top-strippad i page.tsx). Tom =
  // alla grader visas (NÄR matchningen är PÅ). Skickas vidare till list-queryn.
  matchGrades: string[];
  /**
   * issue #292 — matchnings-huvudbrytaren (parsad ur `?matchning=off` i
   * page.tsx). `true` = AV. Härleds HÄR till `matchActive` (SSOT):
   * `matchActive = hasStatedDesiredOccupation && !matchningOff`.
   */
  matchningOff: boolean;
  /**
   * #300 PR-5 (ADR 0084) — "Visa relaterade också"-toggle:n (parsad ur
   * `?relaterade=on` i page.tsx). `true` ⇒ related-graderade annonser tas med i
   * list-queryn OCH i badge-batchen (master-switch). Default false (ren lista).
   * Endast meningsfull när `matchActive` (badges hämtas bara då).
   */
  includeRelated: boolean;
  /**
   * #383 → förenklat 2026-06-30 — "Dölj ansökta" (parsad ur `?doljAnsokta=on` i
   * page.tsx). ORTOGONAL mot matchningen — gallrar bort annonser den inloggade
   * seekern redan sökt. Skickas vidare till list-queryn; kontrollen (toggle:n) bor
   * i hero-filterraden (gatad på hasSeeker där). ("Visa sparade"/"Visa bara
   * ansökta" borttagna — Klas-förenkling.)
   */
  hideApplied: boolean;
  /**
   * #419 pt1 (CTO Approach A) — "Visa bara matchade" (parsad ur `?baraMatchade=on` i
   * page.tsx). Visar ENDAST annonser med en positiv matchningsgrad för seekern. Som
   * includeRelated är den ett MATCHNINGS-koncept: gate:as på `matchActive` här
   * (`effectiveOnlyMatched`) så en stale URL utan angivet yrke / matchning av inte gallrar
   * listan. Kontrollen (kryssrutan) bor i Matchning-popovern (hero-filterraden).
   */
  onlyMatched: boolean;
  /**
   * #454 PR-0 (ADR 0087 D6 FE-konsumtion) — arbetsgivar-filtret: ETT org.nr
   * (10 siffror, validerat i page.tsx). Skickas till list-queryn som ett
   * string[]-element; ORTOGONAL mot matchningen (ren IN-equality-gallring).
   */
  employer: string | undefined;
  q: string;
  /**
   * E2j (ADR 0060 amend 2026-06-12) — commit-intent: när URL:en bär
   * ?commit=1 (avsiktlig sökning via Enter/Sök/förslags-val/toolbar) skickas
   * det vidare till list-queryn så backend auto-capturerar sökningen.
   * Live-förhandsvisning (utan flaggan) fångas EJ. Transient — strippas ur
   * URL:en efter mount av `StripCommitParam`.
   */
  commit: boolean;
  /** Råa searchParams — endast för att bygga paginerings-href. */
  rawParams: {
    page?: string;
    pageSize?: string;
    sortBy?: string;
    occupationGroup?: string | string[];
    region?: string | string[];
    municipality?: string | string[];
    employmentType?: string | string[];
    worktimeExtent?: string | string[];
    matchGrades?: string | string[];
    // #300 PR-5 — bärs i paginerings-href:en så sida-2-klicket inte tappar
    // "Visa relaterade också"-toggle:n (samma felklass som matchGrades).
    relaterade?: string;
    // #383 → förenklat — bärs i paginerings-href:en så sida-2-klicket inte tappar
    // "Dölj ansökta" (samma felklass som relaterade/matchGrades).
    doljAnsokta?: string;
    // #419 pt1 — bärs i paginerings-href:en så sida-2-klicket inte tappar "Visa bara
    // matchade" (samma felklass som doljAnsokta/relaterade).
    baraMatchade?: string;
    // #454 PR-0 — bärs i paginerings-href:en så sida-2-klicket inte tappar
    // arbetsgivar-filtret (samma felklass som ovan).
    employer?: string | string[];
    q?: string;
  };
}

export async function JobbResults({
  page,
  pageSize,
  sortBy,
  occupationGroup,
  region,
  municipality,
  employmentType,
  worktimeExtent,
  matchGrades,
  matchningOff,
  includeRelated,
  hideApplied,
  onlyMatched,
  employer,
  q,
  commit,
  rawParams,
}: JobbResultsProps) {
  // Async Server Component → awaitable next-intl translator (jobads.ui).
  const t = await getTranslations("jobads.ui");
  // Chip-labels hör ihop med resultatet — hämtas parallellt med listan.
  // Reverse-lookup-miss → chip faller till "Okänd kod (<id>)" i toolbaren
  // (ADR 0043 Beslut B graceful degradation).
  // Cap-aritmetik (E2b-architect fråga 5): backend-resolve-capet är
  // MaxConceptIds × 4 = 1600; teoretiskt max här = 400 yrkesgrupper +
  // 21 län + 290 kommuner = 711 — täcker, men marginalen krymper om en
  // fjärde dimension (employmentType, B2) någonsin chip-resolvas.
  // Klass 2 — anställningsform/omfattning chip-resolvas via samma server-
  // reverse-lookup (kind-agnostisk sedan PR-1). Cap-aritmetik (E2b fråga 5):
  // backend-resolve-capet MaxConceptIds×4 = 1600 täcker även de ~8+2 nya.
  const selectedConceptIds = [
    ...occupationGroup,
    ...region,
    ...municipality,
    ...employmentType,
    ...worktimeExtent,
  ];
  // F4-16 (CTO D8) — `hasStatedDesiredOccupation` hämtas via getMyProfile
  // (`cache()`:ad → dedupar mot andra läsare i samma request). Fel/anonym →
  // false (ingen falsk disclosure).
  //
  // issue #292 — den måste resolveras FÖRE getJobAds: `matchActive` (SSOT,
  // härledd här) gatar list-queryns sort-koercion (gate (b): MatchDesc →
  // PublishedAtDesc när matchningen är av/saknar yrke). getMyProfile är
  // `cache()`:ad och deduppas per request (app-shellen läser den redan), så
  // detta sekventiella await är en gratis cache-träff utan extra round-trip.
  // Det är en MINIMAL waterfall — getJobAds startar efter den (instant) cache-
  // träffen — medvetet motiverad: matchActive måste vara känt för sort-
  // koercionen + badge-gaten innan list-queryn körs. De tunga anropen
  // (getJobAds/resolveTaxonomyLabels) körs sedan parallellt i Promise.all nedan.
  const profileResult = await getMyProfile();
  const hasStatedDesiredOccupation =
    profileResult.kind === "ok" &&
    profileResult.data.hasStatedDesiredOccupation;

  // issue #292 (senior-cto-advisor-bind) — matchnings-axelns SSOT: PÅ exakt när
  // användaren angett ett yrke OCH huvudbrytaren inte är avstängd. Allt nedan
  // (badge-fetch, sort-koercion, toolbar) hänger på detta enda härledda värde.
  const matchActive = hasStatedDesiredOccupation && !matchningOff;

  // #300 PR-5 — den effektiva "Visa relaterade också". Related är ett MATCHNINGS-
  // koncept: det är bara meningsfullt när matchnings-axeln är aktiv. En stale URL
  // som bär `?relaterade=on` MEN matchningen av (eller inget angett yrke) ska inte
  // bredda listan med related-yrken — gate:a på matchActive (paritet med badge-/
  // sort-gaterna). Toggle:n renderas ändå bara inne i matchningens PÅ-block, så
  // det här är skyddet mot en manipulerad/stale URL.
  const effectiveIncludeRelated = matchActive && includeRelated;

  // #419 pt1 — den effektiva "Visa bara matchade". Som includeRelated är "bara matchade"
  // ett MATCHNINGS-koncept: bara meningsfullt när matchnings-axeln är aktiv. En stale URL
  // som bär `?baraMatchade=on` MEN matchningen av (eller inget angett yrke) ska inte gallra
  // listan till positiv-grad-only — gate:a på matchActive (paritet effectiveIncludeRelated).
  // Kontrollen renderas ändå bara inne i matchningens PÅ-block; detta är skyddet mot en
  // manipulerad/stale URL.
  const effectiveOnlyMatched = matchActive && onlyMatched;

  // Gate (b) — list-queryns sort. När matchningen inte är aktiv coerceras en
  // aktiv MatchDesc-sort honest tillbaka till nyaste-först (PublishedAtDesc):
  // match-sorten får inte styra ordningen när matchnings-axeln är av/saknar
  // yrke. Toolbaren gör samma koercion på SIN sida (select-värdet) — bägge
  // läser samma matchActive så SYNLIG ordning (select) och faktisk ordning
  // aldrig divergerar (URL-strängen kan bära en inert MatchDesc-token tills
  // nästa aktiva sort-byte — pre-existerande self-healing-doktrin).
  const effectiveSortBy: JobAdSortBy =
    !matchActive && sortBy === "MatchDesc" ? "PublishedAtDesc" : sortBy;

  // #380 — bygg den nuvarande listans query-sträng som varje radlänk bär in i
  // modal-soft-naven (se `JobAdCard.listQuery`). En NAKEN `/jobb/[id]`-länk lät
  // children-slottens `/jobb` re-rendras till tomma searchParams under modalen
  // (Suspense-keyn flippade relaterade/grader/matchning till av), och
  // `router.back()` återställer bara `@modal`-slotten ⇒ listan fastnade i av-
  // läget. Med hela list-staten i länken speglar modal-URL:en listan exakt, så
  // öppna→stäng bevarar HELA filter-/match-läget. Spegla den RÅA URL-staten
  // (sortBy/includeRelated/matchningOff/matchGrades som de ligger i adressen),
  // INTE de coercerade `effective*`-värdena — målet är att close-URL === open-
  // URL. Återbruka den kanoniska `buildJobbHref` (hanterar default-utelämning)
  // och lägg på `page` så djupa sidor också överlever.
  const listHref = buildJobbHref({
    q,
    occupationGroup,
    region,
    municipality,
    employmentType,
    worktimeExtent,
    matchGrades,
    matchningOff,
    includeRelated,
    // #383 → förenklat — bär "Dölj ansökta" i modal-soft-naven så öppna→stäng av
    // ett jobbkort bevarar HELA list-läget (paritet relaterade/matchGrades).
    hideApplied,
    // #419 pt1 — bär "Visa bara matchade" i modal-soft-naven så öppna→stäng bevarar HELA
    // list-läget. Spegla den RÅA URL-staten (onlyMatched, INTE effectiveOnlyMatched) — målet
    // är close-URL === open-URL (paritet hideApplied/includeRelated/matchningOff).
    onlyMatched,
    // #454 PR-0 — bär arbetsgivar-filtret i modal-soft-naven så öppna→stäng
    // av ett jobbkort bevarar det (samma felklass som ovan).
    employer,
    sortBy,
    pageSize: rawParams.pageSize,
  });
  const listBaseQuery = listHref.includes("?")
    ? listHref.slice(listHref.indexOf("?") + 1)
    : "";
  const pageParam =
    rawParams.page && rawParams.page !== "1" ? `page=${rawParams.page}` : "";
  const listQuery = [listBaseQuery, pageParam].filter(Boolean).join("&");

  // #293/#306 — den per-användar oläst-watermarken (`lastSeenJobsAt`) hämtas
  // parallellt med listan. NY renderas mot den HÄMTADE (gamla) watermarken
  // (fetch-then-mark, spegling av /matchningar) — sedan flyttas den fram nedan.
  // Degraderar civilt: läs-fel/anon → null ⇒ ingen NY (W4 cold-start).
  const [result, labelsResult, watermarkResult] = await Promise.all([
    getJobAds({
      page,
      pageSize,
      sortBy: effectiveSortBy,
      occupationGroup,
      region,
      municipality,
      employmentType,
      worktimeExtent,
      matchGrades,
      // #300 PR-5 — master-switch för related-yrken i listan (gate:ad på
      // matchActive ovan). Default false ⇒ ren exakt-match-lista.
      includeRelated: effectiveIncludeRelated,
      // #383 → förenklat — "Dölj ansökta". Skickas rakt igenom; backend gallrar
      // bort annonser seekern redan sökt (guardar en seeker-lös begäran med tom
      // sida). ORTOGONAL mot matchningen — passeras oavsett matchActive.
      hideApplied,
      // #419 pt1 — "Visa bara matchade" (gate:ad på matchActive ovan → effectiveOnlyMatched).
      // Backend visar då ENDAST annonser med positiv matchningsgrad för seekern.
      onlyMatched: effectiveOnlyMatched,
      // #454 PR-0 — arbetsgivar-filtret (ortogonalt mot matchningen; skickas
      // rakt igenom — backend IN-equality-gallrar på organization_number).
      employer,
      q,
      commit,
    }),
    resolveTaxonomyLabels(selectedConceptIds),
    getJobsWatermark(),
  ]);

  const watermark =
    watermarkResult.kind === "ok" ? watermarkResult.data.lastSeenJobsAt : null;

  // Plain Record (EJ Map) — passas över RSC→client-gränsen till
  // JobbResultsToolbar (Map serialiseras inte i RSC-payloaden).
  const resolvedLabels: Record<string, string> =
    labelsResult.kind === "ok"
      ? Object.fromEntries(
          labelsResult.data.map((l) => [l.conceptId, l.label] as const)
        )
      : {};

  switch (result.kind) {
    case "ok": {
      // #293/#306 — NY = oläst: annonser vars `createdAt` (ingestion, Klas-val)
      // ligger EFTER den hämtade (gamla) watermarken. Kall start (null) eller
      // läs-fel → tomt set ⇒ ingen NY (W4 cold-start). Beräknas FÖRE mark-seen
      // (fetch-then-mark) så nästa besök bara visar nytt-sedan-detta-besök.
      const watermarkMs = watermark != null ? Date.parse(watermark) : Number.NaN;
      const newIdSet = new Set<string>(
        Number.isNaN(watermarkMs)
          ? []
          : result.data.items
              .filter((it) => Date.parse(it.createdAt) > watermarkMs)
              .map((it) => it.id)
      );

      // PR5 / ADR 0063 — per-user-overlay-status batch (Sparad/Ansökt-taggar
      // på list-kort). Anonym/utan-auth → tomma set:n (degraderar civilt,
      // inga taggar visas). Max 100 IDs per anrop = validator-cap.
      const itemIds = result.data.items.map((it) => it.id);
      // issue #292 — gate (a): badge-fetchen är BARA av när matchningen är aktiv.
      // När den är av (huvudbrytare av, eller inget angett yrke) hämtas inga
      // grad-taggar alls → tom matchGradeById → inga MatchChip på korten. Status-
      // batchen (Sparad/Ansökt) är oberoende av matchnings-axeln och hämtas
      // alltid.
      const [status, matchTags, employerApplicationCounts] = await Promise.all([
        getJobAdStatusBatch(itemIds),
        // F4-13 (ADR 0076) — graderad match-tagg-overlay. Anonym/utan-auth →
        // tom batch (degraderar civilt, inga taggar). POSITIVE-ONLY: bara
        // annonser med positiv grad finns i `entries`. Hoppas över helt när
        // matchningen är av (issue #292) — Promise.resolve undviker round-trip.
        // #300 PR-5 — `effectiveIncludeRelated` (master-switch, gate:ad på
        // matchActive) ⇒ related-graderade annonser får sin `Related`-chip i
        // listan, koherent med list-queryns breddning ovan.
        matchActive
          ? getJobAdMatchTags(itemIds, effectiveIncludeRelated)
          : Promise.resolve<JobAdMatchBatch>({ entries: {} }),
        // #446 (#311) — per-arbetsgivare "tidigare ansökningar"-räknare. ORTOGONAL
        // mot matchnings-axeln (application-historik, inte match): hämtas ALLTID
        // (paritet status-batchen), aldrig gate:ad på matchActive — badgen ska
        // synas även vid ren bläddring, inte bara i match-läge. Anonym/utan-auth/
        // fel → tom batch (civil degradering, inga badges). POSITIVE-ONLY.
        getEmployerApplicationCounts(itemIds),
      ]);
      const savedIdSet = new Set(status.savedIds);
      const appliedIdSet = new Set(status.appliedIds);
      // #446 — Map<JobAdId, antal> för O(1)-lookup per kort (paritet
      // savedIdSet/matchGradeById). Bara positiva räknare finns i mappen ⇒ en
      // saknad nyckel = 0 tidigare ansökningar ⇒ ingen badge.
      const employerApplicationCountById = new Map<string, number>(
        Object.entries(employerApplicationCounts.countsByJobAdId)
      );
      // Map<JobAdId, MatchGrade> — O(1)-lookup per kort (paritet med
      // savedIdSet/appliedIdSet). `entries` är ett plain Record; bygg Map här.
      // matchActive=false ⇒ entries={} ⇒ tom Map ⇒ inga badges.
      const matchGradeById = new Map<string, MatchGrade>(
        Object.entries(matchTags.entries).map(
          ([id, entry]) => [id, entry.grade] as const
        )
      );

      // fetch-then-mark: flytta fram watermarken EFTER att NY beräknats mot den
      // gamla (newIdSet ovan) — nästa besök visar då bara annonser som kommit in
      // sedan detta besök. #759 (syskon #477 Low 4): flytta fram till det HÄMTADE
      // fönstrets max `createdAt`, INTE klock-nu — en annons som ingestas mellan
      // hämtningen och detta anrop (`createdAt > seenThrough`) förblir korrekt
      // flaggad NY. Till skillnad från /matchningars nyast-först-lista kan /jobb
      // vara relevans-/matchrank-sorterad, så vi tar MAX över sidan (inte
      // items[0]) och bär den ORIGINALA ISO-strängen (full precision, paritet
      // markMatchesSeen). Tom sida → undefined ⇒ backend faller tillbaka på nu.
      // Icke-blockerande: markJobsSeen degraderar civilt (kastar aldrig), ett fel
      // lämnar bara watermarken orörd denna gång. Gatas på en lyckad watermark-
      // LÄSNING: utan en koherent baseline avancerar vi inte (annars kan en
      // transient läs-miss tyst nolla NY). Speglar /matchningar (mark-seen on open).
      if (watermarkResult.kind === "ok") {
        await markJobsSeen(maxCreatedAt(result.data.items));
      }

      return (
        <>
          {/* Result-toolbar (client-island): N träffar + aktiva chips +
              sort-dropdown på samma rad (F4/ADR 0055). totalCount kommer
              från RSC-fetchen; chips/sort live-commit:ar searchParams
              symmetriskt med hero-pills (buildJobbHref). */}
          <JobbResultsToolbar
            totalCount={result.data.totalCount}
            occupationGroup={occupationGroup}
            region={region}
            municipality={municipality}
            employmentType={employmentType}
            worktimeExtent={worktimeExtent}
            matchGrades={matchGrades}
            includeRelated={includeRelated}
            matchningOff={matchningOff}
            hideApplied={hideApplied}
            onlyMatched={onlyMatched}
            employer={employer}
            resolvedLabels={resolvedLabels}
            q={q}
            sortBy={sortBy}
            pageSize={rawParams.pageSize}
            hasStatedDesiredOccupation={hasStatedDesiredOccupation}
            matchActive={matchActive}
          />
          <div className="flex flex-col gap-2.5">
            <JobAdList
              jobAds={result.data.items}
              newIdSet={newIdSet}
              savedIdSet={savedIdSet}
              appliedIdSet={appliedIdSet}
              matchGradeById={matchGradeById}
              employerApplicationCountById={employerApplicationCountById}
              listQuery={listQuery}
            />
            <JobAdPagination
              page={result.data.page}
              pageSize={result.data.pageSize}
              totalCount={result.data.totalCount}
              buildHref={(targetPage) =>
                buildPageHref(rawParams, targetPage, pageSize)
              }
            />
          </div>
        </>
      );
    }
    case "unauthorized":
      redirect("/logga-in");
    case "rateLimited":
      return (
        <div
          role="alert"
          className="rounded-md border border-warning-700/30 bg-warning-50 px-6 py-4"
        >
          <p className="text-body font-medium text-warning-700">
            {t("results.rateLimitedTitle")}
          </p>
          <p className="mt-1 text-body-sm text-warning-700">
            {t("results.rateLimitedBody", {
              seconds: result.retryAfterSeconds,
            })}
          </p>
        </div>
      );
    // notFound/forbidden/error kollapsas till samma copy: list-endpointen kan
    // aldrig runtime-faktiskt returnera 404 (responseToResult sätter inte
    // includeNotFound) och job-ads endpoint är endast auth-gated (forbidden
    // exponeras inte idag) — alla tre faller till samma "tekniskt fel"-copy.
    case "notFound":
    case "forbidden":
    case "error":
      return (
        <div className="rounded-md border border-danger-600/30 bg-danger-50 px-6 py-4 text-danger-700">
          <p className="text-body font-medium">{t("results.errorTitle")}</p>
          <p className="mt-1 text-body-sm">{t("results.errorBody")}</p>
        </div>
      );
    default:
      return assertNever(result);
  }
}

// Normaliserar string | string[] | undefined → string[] (tomma värden bort).
function toStringList(raw: string | string[] | undefined): string[] {
  if (raw === undefined) return [];
  const arr = Array.isArray(raw) ? raw : [raw];
  return arr.map((v) => v.trim()).filter((v) => v.length > 0);
}

function buildPageHref(
  params: JobbResultsProps["rawParams"],
  targetPage: number,
  defaultPageSize: number
): string {
  const url = new URLSearchParams();
  if (targetPage !== 1) url.set("page", String(targetPage));
  if (params.pageSize && Number(params.pageSize) !== defaultPageSize) {
    url.set("pageSize", params.pageSize);
  }
  if (params.sortBy && params.sortBy !== "PublishedAtDesc") {
    url.set("sortBy", params.sortBy);
  }
  for (const v of toStringList(params.occupationGroup))
    url.append("occupationGroup", v);
  for (const v of toStringList(params.region)) url.append("region", v);
  // E2b — utan denna rad tappar sida-2-klicket kommun-filtret (samma
  // felklass som F3 B-FIX; buildPageHref är en ANDRA URL-builder vid
  // sidan av buildJobbHref — architect-dom fråga 4.1).
  for (const v of toStringList(params.municipality))
    url.append("municipality", v);
  // Klass 2 — utan dessa tappar sida-2-klicket anställningsform/omfattning
  // (samma felklass som municipality ovan; buildPageHref är en andra URL-
  // builder vid sidan av buildJobbHref).
  for (const v of toStringList(params.employmentType))
    url.append("employmentType", v);
  for (const v of toStringList(params.worktimeExtent))
    url.append("worktimeExtent", v);
  // STEG 5 — utan denna rad tappar sida-2-klicket grad-filtret (samma felklass
  // som municipality/Klass-2 ovan; buildPageHref är en andra URL-builder vid
  // sidan av buildJobbHref). Page-validatorn droppar Top/okänt redan.
  for (const v of toStringList(params.matchGrades))
    url.append("matchGrades", v);
  // #300 PR-5 — utan denna rad tappar sida-2-klicket "Visa relaterade också"-
  // toggle:n (samma felklass som matchGrades ovan). Bevaras BARA när on (paritet
  // med buildJobbHref); page.tsx parsar bara on-värdet.
  if (params.relaterade === "on") url.set("relaterade", "on");
  // #383 → förenklat — utan denna rad tappar sida-2-klicket "Dölj ansökta" (samma
  // felklass som relaterade ovan). Bevaras BARA när on (paritet buildJobbHref).
  if (params.doljAnsokta === "on") url.set("doljAnsokta", "on");
  // #419 pt1 — utan denna rad tappar sida-2-klicket "Visa bara matchade" (samma felklass
  // som doljAnsokta ovan). Bevaras BARA när on (paritet buildJobbHref).
  if (params.baraMatchade === "on") url.set("baraMatchade", "on");
  // #454 PR-0 — utan denna rad tappar sida-2-klicket arbetsgivar-filtret
  // (samma felklass som ovan; buildPageHref är en andra URL-builder vid sidan
  // av buildJobbHref). SPOT-gaten (parseEmployerParam) delas med page-parsern.
  const employerParam = parseEmployerParam(params.employer);
  if (employerParam) url.set("employer", employerParam);
  // #823 — KLAMPA. Utan detta re-emitterar sidlänkarna det råa under-minimum-q:t
  // (/jobb?q=a → "Nästa sida" = /jobb?page=2&q=a): en URL vi själva genererar som påstår
  // ett sök sidan inte kör, medan sökfältet står tomt. Samma SPOT-klamp som page.tsx.
  const clampedQ = clampSubMinimumQ(params.q);
  if (clampedQ) url.set("q", clampedQ);
  const qs = url.toString();
  return qs.length > 0 ? `/jobb?${qs}` : "/jobb";
}

import { Suspense } from "react";
import { redirect } from "next/navigation";
import { getTranslations } from "next-intl/server";
import { getServerSession } from "@/lib/auth/session";
import { getMyProfile } from "@/lib/api/me";
import { getRecentSearches } from "@/lib/api/recent-searches";
import { getSavedJobAds } from "@/lib/api/saved-job-ads";
import { getTaxonomyTree } from "@/lib/api/taxonomy";
import { jobAdSortBySchema, type JobAdSortBy } from "@/lib/dto/job-ads";
import { isListMatchGrade } from "@/lib/dto/job-ad-match";
import {
  clampSubMinimumQ,
  MATCHNING_OFF_VALUE,
  RELATERADE_ON_VALUE,
  STATUS_ON_VALUE,
  parseEmployerParam,
} from "@/lib/job-ads/search-params";
// #419 pt1 — "Visa bara matchade"-sentinelparamen delar STATUS_ON_VALUE ("on") med
// doljAnsokta/relaterade; den separata param-konstanten dokumenterar nyckeln.
import { JobbHeroFilters } from "@/components/job-ads/jobb-hero-filters";
import { JobbHeroSearch } from "@/components/job-ads/jobb-hero-search";
import { JobbResults } from "@/components/job-ads/jobb-results";
import { JobAdListSkeleton } from "@/components/job-ads/job-ad-list-skeleton";
import { StripCommitParam } from "@/components/job-ads/strip-commit-param";
import { RecentSearchesHeroChip } from "@/components/recent-searches/recent-searches-hero-chip";
import { SavedJobAdsHeroChip } from "@/components/saved-job-ads/saved-job-ads-hero-chip";

// searchParams-värden kan vara string | string[] | undefined.
// occupationGroup/region/municipality är upprepade query-params (ADR 0042
// Beslut B) → string[] vid flera värden. occupationGroup = ssyk-level-4/
// yrkesgrupp (E2a nivå-skifte); municipality = kommun (E2b — backend
// unionerar region∪municipality, ADR 0067 impl-notat E2b).
type JobbSearchParams = {
  page?: string;
  pageSize?: string;
  sortBy?: string;
  occupationGroup?: string | string[];
  region?: string | string[];
  municipality?: string | string[];
  // Klass 2 (2026-06-13) — anställningsform + omfattning, upprepade params.
  employmentType?: string | string[];
  worktimeExtent?: string | string[];
  // STEG 5 (grade-filter, 2026-06-23) — matchningsgrad-filter, upprepad param
  // (enum-namn Basic/Good/Strong). Okända värden + Top droppas tyst.
  matchGrades?: string | string[];
  // issue #292 — matchnings-huvudbrytaren. `?matchning=off` = AV (göm badges +
  // match-sort). Frånvaro = default PÅ (när användaren angett ett yrke). Allt
  // annat än "off" tolkas som frånvaro (PÅ) — page.tsx parsar bara off-värdet.
  matchning?: string;
  // #300 PR-5 — "Visa relaterade också"-toggle:n. `?relaterade=on` = PÅ (ta med
  // related-graderade annonser). Frånvaro = default AV. Allt annat än "on" tolkas
  // som frånvaro (AV) — page.tsx parsar bara on-värdet.
  relaterade?: string;
  // #383 → förenklat 2026-06-30 — "Dölj ansökta"-toggle:n. `?doljAnsokta=on`.
  // Frånvaro = ingen status-gallring. Endast on-värdet parsas (paritet relaterade).
  // (`sparade`/`ansokta` borttagna med "Visa sparade"/"Visa bara ansökta".)
  doljAnsokta?: string;
  // #419 pt1 — "Visa bara matchade"-toggle:n. `?baraMatchade=on`. Frånvaro = hela
  // listan. Endast on-värdet parsas (paritet doljAnsokta/relaterade).
  baraMatchade?: string;
  // #454 PR-0 — arbetsgivar-filtret: ETT org.nr (exakt 10 siffror). Felformat
  // droppas tyst (paritet matchGrades drop-unknown; backend-validatorn skulle
  // annars 400:a hela list-queryn). string[] (manipulerad URL) → första värdet.
  employer?: string | string[];
  q?: string;
  // E2j (ADR 0060 amend) — commit-intent: "1" vid avsiktlig sökning.
  commit?: string;
};

interface PageProps {
  // Next.js 16 App Router: searchParams är Promise (verifierat mot
  // node_modules/next/dist/docs/.../page#searchparams-optional).
  searchParams: Promise<JobbSearchParams>;
}

const DEFAULT_PAGE_SIZE = 20;

export default async function JobbPage({ searchParams }: PageProps) {
  const user = await getServerSession();
  if (!user) redirect("/logga-in");

  const t = await getTranslations("pages");
  const params = await searchParams;
  const page = parsePositiveInt(params.page, 1);
  const pageSize = Math.min(
    parsePositiveInt(params.pageSize, DEFAULT_PAGE_SIZE),
    100
  );
  const sortBy = parseSortBy(params.sortBy);
  const occupationGroup = toStringList(params.occupationGroup);
  const region = toStringList(params.region);
  const municipality = toStringList(params.municipality);
  // Klass 2 — anställningsform (multi) + omfattning (radio → 0–1 element).
  const employmentType = toStringList(params.employmentType);
  const worktimeExtent = toStringList(params.worktimeExtent);
  // STEG 5 — matchningsgrad-filter. Validera mot {Basic, Good, Strong}: okända
  // värden OCH `Top` droppas tyst (Top är inte filtrerbart i listan — Fast-
  // bandet; backend skulle 400:a det). Dedupe så en manipulerad URL inte
  // dubblerar params. Tom = inget grad-filter ("Matchning av").
  const matchGrades = [
    ...new Set(toStringList(params.matchGrades).filter(isListMatchGrade)),
  ];
  // issue #292 — matchnings-huvudbrytaren. Parsa BARA off-värdet → boolean.
  // page.tsx förblir presentationellt: den härleder inte matchActive (det är
  // jobb-results.tsx SSOT — `matchActive = hasStatedDesiredOccupation &&
  // !matchningOff`), den trådar bara den parsade flaggan vidare.
  const matchningOff = params.matchning === MATCHNING_OFF_VALUE;
  // #300 PR-5 — "Visa relaterade också"-toggle:n. Parsa BARA on-värdet → boolean
  // (master-switch för includeRelated genom alla tre matchnings-anropen). Trådas
  // vidare till JobbResults; default AV (frånvaro = ren lista).
  const includeRelated = params.relaterade === RELATERADE_ON_VALUE;
  // #383 → förenklat — "Dölj ansökta". Parsa BARA on-värdet → boolean (paritet
  // relaterade). page.tsx förblir presentationellt: den trådar flaggan vidare;
  // hero-filterraden gatar den på seeker-närvaro (ortogonalt mot matchningen).
  const hideApplied = params.doljAnsokta === STATUS_ON_VALUE;
  // #419 pt1 — "Visa bara matchade". Parsa BARA on-värdet → boolean (paritet
  // doljAnsokta/relaterade). Trådas vidare till hero-filterraden (kontrollen, gatad på
  // matchnings-axeln) + JobbResults (gatad på matchActive där, paritet includeRelated).
  const onlyMatched = params.baraMatchade === STATUS_ON_VALUE;
  // #454 PR-0 — arbetsgivar-filtret. SPOT-parsern (search-params.ts) gatar
  // på exakt 10 siffror med tyst drop (drop-unknown-disciplinen, paritet
  // matchGrades); buildPageHref använder SAMMA parser så page-parse och
  // paginerings-href aldrig divergerar.
  const employer = parseEmployerParam(params.employer);
  // #823 — klampa en söktext under backendens minimum till "ingen söktext", exakt som
  // backendens egen SearchQueryParser gör med en residual under QMinLength. Utan detta
  // 400:ar ListJobAdsQueryValidator på ?q=a och sidan målar teknisk-fel-kortet — vilket
  // träffar bokmärkta/delade/handredigerade länkar och no-JS-submit:en, alltså vägar som
  // ingen klient-grind når. Dessutom ÄRVDE heron det förgiftade q:t: `base.q` blev "a",
  // så varje efterföljande commit skickade med det och 400:ade igen även efter att
  // användaren lagt till ett giltigt filter. Klampen sker tyst (paritet med parsern).
  const q = clampSubMinimumQ(emptyToUndefined(params.q));
  // E2j — commit-intent gatar backend-auto-capture. Strippas ur URL:en efter
  // mount av <StripCommitParam> (delningsbar länk re-capturerar inte).
  const commit = params.commit === "true";

  // ADR 0043 — picker-träd hämtas server-side för hero-filter-popovern
  // (CLAUDE.md §4.3/§5.2 — ingen useEffect-fetch). Träd + senaste
  // sökningar är HERO-beroenden: de måste vara klara innan hero renderas
  // och blockerar därför INTE resultat-streamingen. getJobAds() +
  // chip-label-resolvern flyttades till `JobbResults` (F6 P4 B1) så att
  // bara resultat-ytan — inte hero:n — byts mot skeleton under en sökning.
  // getMyProfile är `cache()`:ad (app-shellen + JobbResults läser samma värde
  // per request — noll extra round-trip). Hoistad hit så hero-filterraden (utanför
  // Suspense) kan rendera Matchning + Dölj ansökta: `hasStatedDesiredOccupation`
  // gatar Matchning-pillen, en lyckad profil-läsning ⇒ seekern finns och gatar
  // Dölj ansökta (paritet med backend-guarden). Fel/anon → false (kontrollerna göms).
  const [taxonomyResult, recentSearchesResult, savedJobAdsResult, profileResult] =
    await Promise.all([
      getTaxonomyTree(),
      getRecentSearches(),
      getSavedJobAds(),
      getMyProfile(),
    ]);
  const hasStatedDesiredOccupation =
    profileResult.kind === "ok" && profileResult.data.hasStatedDesiredOccupation;
  const hasSeeker = profileResult.kind === "ok";

  // ADR 0060: Senaste-sökningar-hero-chip degraderar civilt — vid fel
  // (network/parse/auth-edge) faller chipen till tom-tillstånd och inget
  // visas i hero-topbaren (no-mock-doktrin). Capturen är best-effort på BE.
  const recentSearches =
    recentSearchesResult.kind === "ok" ? recentSearchesResult.data : [];

  // PR5 (Klas-feedback 2026-05-23 + Platsbanken-paritet): Sparade-chip
  // paritet med Senaste-sökningar. Civil degradering vid fel.
  const savedJobAds =
    savedJobAdsResult.kind === "ok" ? savedJobAdsResult.data : [];

  // Träd-hämtning får aldrig blockera sök-ytan. Misslyckas trädet
  // degraderar popovern civilt (tom lista + informativ rad i
  // JobbFilterPopover) (ADR 0043 Beslut B graceful degradation).
  const taxonomy = taxonomyResult.kind === "ok" ? taxonomyResult.data : null;

  // Suspense-key: byts vid varje ny sökning så fallbacken (skeleton)
  // visas om även navigeringen sker mellan två /jobb-URL:er. Utan en
  // ny key skulle React behålla föregående resultat-träd medan nästa
  // sökning hämtas. searchParams-strängen är en stabil, kollisionsfri
  // identitet för "den här sökningen".
  const resultsKey = new URLSearchParams(
    Object.entries({
      page: params.page ?? "",
      pageSize: params.pageSize ?? "",
      sortBy: params.sortBy ?? "",
      q: q ?? "",
    })
  ).toString();
  const occupationGroupKey = occupationGroup.join(",");
  const regionKey = region.join(",");
  const municipalityKey = municipality.join(",");
  // Klass 2 — ingår i Suspense-keyn så resultat-skeletonen visas även när
  // bara anställningsform/omfattning ändras (samma princip som dimensionerna).
  const employmentTypeKey = employmentType.join(",");
  const worktimeExtentKey = worktimeExtent.join(",");
  // STEG 5 — matchningsgrad ingår i Suspense-keyn så listan re-renderas (visar
  // skeleton) när bara grad-filtret ändras (samma princip som dimensionerna).
  const matchGradesKey = matchGrades.join(",");
  // issue #292 — matchnings-axeln (på/av) ingår i Suspense-keyn så listan
  // re-renderas när bara huvudbrytaren toggle:as: badge-fetchen och sort-
  // koercionen i jobb-results.tsx hänger på matchActive, vars värde ändras med
  // den här flaggan (samma princip som dimensionerna/grad-filtret).
  const matchningKey = matchningOff ? "off" : "";
  // #300 PR-5 — "Visa relaterade också" ingår i Suspense-keyn så listan
  // re-renderas (visar skeleton) när bara toggle:n flippas: list-/badge-fetchen
  // hänger på includeRelated (samma princip som matchningsaxeln/grad-filtret).
  const relateradeKey = includeRelated ? "on" : "";
  // #383 → förenklat — "Dölj ansökta" ingår i Suspense-keyn så listan re-renderas
  // (visar skeleton) när toggle:n flippas (samma princip som matchnings-axeln).
  const statusKey = hideApplied ? "h" : "";
  // #419 pt1 — "Visa bara matchade" ingår i Suspense-keyn så listan re-renderas (visar
  // skeleton) när toggle:n flippas (samma princip som status/matchnings-axeln).
  const onlyMatchedKey = onlyMatched ? "m" : "";
  // #454 PR-0 — arbetsgivar-filtret ingår i Suspense-keyn så listan re-renderas
  // (visar skeleton) när filtret sätts/tas bort (samma princip som dimensionerna).
  const employerKey = employer ?? "";

  return (
    <>
      {/* E2j — strippar ?commit=1 ur URL:en efter mount (transient capture-
          signal; delad länk får inte re-capturera). Render-null. */}
      <StripCommitParam active={commit} />
      {/* G1 "F4 Hybrid"-banner (ADR 0068) — inramad mörkgrön gradient-
          platta på canvas-wrapper, asymmetrisk komposition: display-rubrik
          vänster, sök + actions höger (kompositions-facit:
          docs/handoff-banner/referens/F4-banner-referens.html).
          GET-form mot /jobb behåller befintlig searchParams-mekanik/URL-
          kontrakt utan client-JS: aktiva filter (occupationGroup[]/region[]/
          sortBy/pageSize) bärs som hidden inputs så en ny sökning inte
          tappar dem; `page` utelämnas medvetet (ny sökterm → sida 1).
          INGEN placeholder i sökfältet (Klas hård regel 2026-06-10 —
          labeln ovanför bär instruktionen). Stats stannar i headern.
          Hero renderas SYNKRONT — utanför Suspense-gränsen och förblir
          synlig medan resultatet hämtas (F6 P4 B1). */}
      <section className="jp-hero">
        <div className="jp-hero__inner">
          <div className="jp-hero__plate">
            <div>
              {/* G2 (Klas rendered-feedback 2026-06-10): enkel funktionell
                  rubrik — "Lediga jobb./I lugn och ro." lät AI-aktigt.
                  Inget utropstecken (civic-utility, CLAUDE.md §10.3). */}
              <h1 className="jp-hero__title">{t("jobb.title")}</h1>
              <p className="jp-hero__lede">{t("jobb.lede")}</p>
            </div>

            <div className="jp-hero__panel">
              {/* Actions-rad: Senaste-sökningar (ADR 0060) + Sparade-chip
                  (F6 P5 Punkt 2 PR5) — flyttade från hero-topbaren in i
                  höger-panelen (G1), samma komponenter. */}
              <div className="jp-hero__actions">
                <RecentSearchesHeroChip items={recentSearches} />
                <SavedJobAdsHeroChip items={savedJobAds} />
              </div>

              {/* Hero-sökruta (client-ö, ADR 0067 Fas E2d): typeahead-chip-
                  komponist. Taxonomi-förslag → strukturerat dimension-chip
                  via router.push; fri text → q. No-JS: ön server-renderar en
                  äkta `<form action="/jobb" method="get">` med hidden inputs
                  som bär aktiva filter (progressive enhancement, §5.2).
                  INGEN placeholder (Klas hård regel 2026-06-10 — labeln bär
                  instruktionen). */}
              <JobbHeroSearch
                taxonomy={taxonomy}
                q={q ?? ""}
                occupationGroup={occupationGroup}
                region={region}
                municipality={municipality}
                employmentType={employmentType}
                worktimeExtent={worktimeExtent}
                matchGrades={matchGrades}
                employer={employer}
                sortBy={sortBy}
                pageSize={params.pageSize}
                initialCommitted={commit}
              />

              {/* Hero-filter-pills + Platsbanken-popovers (client-island,
                  F4/ADR 0055). Serialiserbara props: taxonomy-träd, valda
                  conceptId string[], q/sortBy/pageSize. Live-commit per
                  klick via router.push (transition) — searchParams ADR 0042
                  Beslut B (upprepade occupationGroup/region) OFÖRÄNDRAT. */}
              <JobbHeroFilters
                taxonomy={taxonomy}
                initialOccupationGroup={occupationGroup}
                initialRegion={region}
                initialMunicipality={municipality}
                initialEmploymentType={employmentType}
                initialWorktimeExtent={worktimeExtent}
                initialMatchGrades={matchGrades}
                initialMatchningOff={matchningOff}
                initialIncludeRelated={includeRelated}
                initialHideApplied={hideApplied}
                initialOnlyMatched={onlyMatched}
                hasStatedDesiredOccupation={hasStatedDesiredOccupation}
                hasSeeker={hasSeeker}
                q={q ?? ""}
                employer={employer}
                sortBy={sortBy}
                pageSize={params.pageSize}
              />
            </div>
          </div>
        </div>
      </section>

      <div className="jp-container jp-page">
        {/* Resultat-ytan streamas: <Suspense> visar JobAdListSkeleton
            medan JobbResults await:ar getJobAds(). Hero ovan är redan
            renderad och förblir synlig. `key` byts per sökning så
            skeleton:en visas även vid /jobb→/jobb-navigering (F6 P4 B1). */}
        <Suspense
          key={`${resultsKey}|${occupationGroupKey}|${regionKey}|${municipalityKey}|${employmentTypeKey}|${worktimeExtentKey}|${matchGradesKey}|${matchningKey}|${relateradeKey}|${statusKey}|${onlyMatchedKey}|${employerKey}`}
          fallback={<JobAdListSkeleton />}
        >
          <JobbResults
            page={page}
            pageSize={pageSize}
            sortBy={sortBy}
            occupationGroup={occupationGroup}
            region={region}
            municipality={municipality}
            employmentType={employmentType}
            worktimeExtent={worktimeExtent}
            matchGrades={matchGrades}
            matchningOff={matchningOff}
            includeRelated={includeRelated}
            hideApplied={hideApplied}
            onlyMatched={onlyMatched}
            employer={employer}
            q={q ?? ""}
            commit={commit}
            rawParams={params}
          />
        </Suspense>
      </div>
    </>
  );
}

function parsePositiveInt(raw: string | undefined, fallback: number): number {
  if (!raw) return fallback;
  const n = Number.parseInt(raw, 10);
  return Number.isFinite(n) && n > 0 ? n : fallback;
}

function parseSortBy(raw: string | undefined): JobAdSortBy {
  if (!raw) return "PublishedAtDesc";
  const parsed = jobAdSortBySchema.safeParse(raw);
  return parsed.success ? parsed.data : "PublishedAtDesc";
}

function emptyToUndefined(s: string | undefined): string | undefined {
  return s && s.trim().length > 0 ? s.trim() : undefined;
}

// Normaliserar string | string[] | undefined → string[] (tomma värden bort).
function toStringList(raw: string | string[] | undefined): string[] {
  if (raw === undefined) return [];
  const arr = Array.isArray(raw) ? raw : [raw];
  return arr.map((v) => v.trim()).filter((v) => v.length > 0);
}

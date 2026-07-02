import type { JobAdSortBy } from "@/lib/dto/job-ads";

/**
 * Centraliserad searchParams-builder för /jobb (F4). Hero-filter-popovers
 * och result-toolbarens sort-dropdown bygger URL:en HÄR — symmetriskt
 * param-bevarande (samma lärdom som F3 B-FIX: två ytor som skriver samma
 * URL får inte radera varandras params).
 *
 * Kontrakt (ADR 0042 Beslut B, OFÖRÄNDRAT):
 * - `occupationGroup` / `region` / `municipality` = upprepade query-params
 *   (conceptId string[]). `occupationGroup` = ssyk-level-4/yrkesgrupp (ADR
 *   0067 Fas E2a nivå-skifte). `municipality` = kommun (Fas E2b — backend
 *   kombinerar region∪municipality som union, ADR 0067 impl-notat E2b).
 * - `q` = hero-sökordet (ägs av hero-GET-formuläret; bärs vidare här så
 *   en filter-/sort-ändring aldrig tappar användarens sökterm).
 * - `sortBy` utelämnas när = default (PublishedAtDesc).
 * - `pageSize` bevaras om explicit satt.
 * - `page` utelämnas ALLTID: filter-/sort-ändring → tillbaka till sida 1
 *   (annars riskerar användaren en sida som inte längre finns).
 */
export interface JobbUrlState {
  q: string;
  occupationGroup: ReadonlyArray<string>;
  region: ReadonlyArray<string>;
  municipality: ReadonlyArray<string>;
  // Klass 2 (ADR 0067 Fas E, 2026-06-13) — Klass-2-filterpanelens dimensioner.
  // `employmentType` = anställningsform (JobTech `employment-type`, ~8,
  // checkbox-multi). `worktimeExtent` = omfattning (JobTech `worktime-extent`,
  // Heltid/Deltid, radio-single → 0 eller 1 element). Upprepade query-params
  // (samma kontrakt som occupationGroup/region/municipality, ADR 0042 Beslut
  // B). Backend filtrerar på ?employmentType=/?worktimeExtent= (B2/#60).
  // Panel-valda (aldrig text-representabla i hero-fältet — som popover-
  // dimensionerna, CTO VAL 4a; lever bara i URL-state + filter-raden).
  employmentType: ReadonlyArray<string>;
  worktimeExtent: ReadonlyArray<string>;
  // STEG 5 (grade-filter, 2026-06-23) — matchningsgrad-filtret. Bär
  // ENUM-NAMN (`Basic` | `Good` | `Strong`, ALDRIG `Top` — listfiltret är
  // Fast-bandet och kan inte beräkna Toppmatch; backend-validatorn avvisar
  // `Top`). Svenska labels (Grund | Bra | Stark) lever bara i UI, aldrig i
  // URL:en (samma regel som occupationGroup som bär concept-id, inte i18n).
  // Upprepad query-param (?matchGrades=Strong&matchGrades=Good), samma
  // kontrakt som employmentType/worktimeExtent (ADR 0042 Beslut B).
  // Produktmodell (Klas): matchGrades smalnar BARA av VILKA grader som visas
  // när matchningen är PÅ (tom = alla grader). "Av" är inte längre en tom
  // grad-lista (issue #292) utan en EGEN explicit param (`matchningOff`) —
  // matchGrades överlastas aldrig med en off-sentinel (senior-cto-advisor-bind).
  // matchGrades är runtime-view-state, INTE en commit/recent-search-
  // angelägenhet (utelämnas medvetet ur den concern:en).
  matchGrades: ReadonlyArray<string>;
  // issue #292 (Klas + senior-cto-advisor) — matchnings-axelns huvudbrytare.
  // `true` = matchningen är AVSTÄNGD (skriver `?matchning=off` i URL:en); badges
  // + match-sort göms och matchGrades töms. Frånvaro (false) = default PÅ (när
  // användaren angett ett yrke). Persistent, delningsbar URL-state (till skillnad
  // från den transienta `commit`-flaggan). Härleds till `matchActive` i
  // `jobb-results.tsx` (SSOT): `matchActive = hasStatedDesiredOccupation &&
  // !matchningOff`.
  matchningOff?: boolean;
  // #300 PR-5 (ADR 0084) — "Visa relaterade också"-toggle:n. `true` =
  // related-graderade annonser (yrken som LIKNAR de valda) tas med i listan +
  // matchnings-anropen (skriver `?relaterade=on`). Frånvaro (undefined/false) =
  // default AV (ren URL, paritet med matchningOff). Master-switch för
  // includeRelated genom alla tre anropen (lista/batch/detalj). Runtime-view-
  // state (navigerar utan commit-flaggan, paritet matchGrades).
  includeRelated?: boolean;
  // #383 → förenklat 2026-06-30 (Klas: en enda "Dölj ansökta"-toggle i
  // hero-filterraden; "Visa sparade" + "Visa bara ansökta" borttagna — sparade nås
  // via Sparade annonser-dropdownen + /sparade). `hideApplied` = dölj annonser jag
  // redan sökt. Frånvaro (undefined/false) = ingen status-gallring (ren URL, paritet
  // matchningOff/includeRelated). Svensk sentinel-param `?doljAnsokta=on`. ORTOGONAL
  // mot matchningen (renderas även när matchningen är av; gatas bara på inloggad
  // seeker). Runtime-view-state (navigerar utan commit). Backend `JobAdStatusFilter`
  // (#383) behåller savedOnly/appliedOnly-fälten — FE skickar dem bara aldrig längre.
  hideApplied?: boolean;
  // #419 punkt 1 (CTO Approach A, 2026-06-30) — "Visa bara matchade". `true` = visa
  // ENDAST annonser med en positiv matchningsgrad för användaren (skriver
  // `?baraMatchade=on`). Frånvaro (undefined/false) = default AV (ren URL, paritet
  // matchningOff/includeRelated/hideApplied). Runtime-view-state (navigerar utan
  // commit-flaggan). Kontrollen (en kryssruta) bor i Matchning-popovern, gatad på
  // matchnings-axeln PÅ; FE mappar den till API-kontraktets engelska flagga `onlyMatched`.
  onlyMatched?: boolean;
  // #454 PR-0 (ADR 0087 D6 FE-konsumtion; löser C1-flaggan "live silent-drop") —
  // arbetsgivar-filtret: ett org.nr (exakt 10 siffror, validerat i page.tsx).
  // SINGEL-värde v1 (företagskortets "se annonser"-länk bär ETT företag;
  // backend binder string[] — FE skickar ett element). Frånvaro = inget
  // arbetsgivar-filter (ren URL). Aldrig text-representabelt i hero-fältet
  // (som popover-dimensionerna, CTO VAL 4a); syns som avtagbar chip i
  // toolbaren. FE emitterar ALDRIG en pnr-shaped employer-param (länk-
  // producenten gatar på IsProtectedIdentity — ADR 0087 D8(c)).
  employer?: string;
  sortBy: JobAdSortBy;
  pageSize?: string;
}

/**
 * issue #292 — det explicita off-värdet för matchnings-axeln. Param-namnet är
 * svenskt (`matchning`, paritet med rutterna /jobb /ansokningar); värdet `off`
 * är ett stabilt sentinel-ord (inte i18n, samma regel som enum-namnen i
 * matchGrades). Endast `off` skrivs ut — PÅ-läget är paramens FRÅNVARO så
 * default-URL:en förblir ren (`/jobb`).
 */
export const MATCHNING_PARAM = "matchning";
export const MATCHNING_OFF_VALUE = "off";

/**
 * #300 PR-5 (ADR 0084) — "Visa relaterade också"-toggle:ns URL-param. Param-namnet
 * är svenskt (`relaterade`, paritet med rutterna /jobb /ansokningar + `matchning`);
 * värdet `on` är ett stabilt sentinel-ord (inte i18n, samma regel som `matchning=off`
 * och enum-namnen i matchGrades). Endast `on` skrivs ut — AV-läget är paramens
 * FRÅNVARO så default-URL:en förblir ren (`/jobb`). Separat från `matchGrades`/
 * `matchning` (senior-cto-advisor-bind: egen master-switch, ingen överlastning).
 */
export const RELATERADE_PARAM = "relaterade";
export const RELATERADE_ON_VALUE = "on";

/**
 * #383 → förenklat 2026-06-30 — "Dölj ansökta"-togglens URL-param. Svenskt namn
 * (paritet rutterna /jobb /ansokningar + `matchning`/`relaterade`); värdet `on` är
 * ett stabilt sentinel-ord (inte i18n, samma regel som de övriga). Endast `on`
 * skrivs ut — AV-läget är paramens FRÅNVARO så default-URL:en förblir ren. FE mappar
 * den till API-kontraktets engelska flagga `hideApplied`. (`sparade`/`ansokta`
 * borttagna med "Visa sparade"/"Visa bara ansökta" — Klas-förenkling.)
 */
export const DOLJ_ANSOKTA_PARAM = "doljAnsokta";
export const STATUS_ON_VALUE = "on";

/**
 * #419 punkt 1 (CTO Approach A) — "Visa bara matchade"-togglens URL-param. Svenskt namn
 * (paritet rutterna /jobb /ansokningar + `matchning`/`relaterade`/`doljAnsokta`); värdet
 * `on` är ett stabilt sentinel-ord (inte i18n, samma regel som de övriga). Endast `on`
 * skrivs ut — AV-läget är paramens FRÅNVARO så default-URL:en förblir ren. FE mappar den
 * till API-kontraktets engelska flagga `onlyMatched`.
 */
export const BARA_MATCHADE_PARAM = "baraMatchade";

export const DEFAULT_SORT_BY: JobAdSortBy = "PublishedAtDesc";

/**
 * Fas E2j (ADR 0060 amendment 2026-06-12) — commit-intent-signalen.
 * `commit` är en TRANSIENT signal-param, INTE ett tillstånd: den ingår
 * ALDRIG i `JobbUrlState`, `sameUrlState`, `buildJobbHref` eller
 * `serializeSearchText` (annars bryts spegel-fältets own-roundtrip-detektor
 * + förorenar delningsbara URL:er). Den adderas endast som suffix på
 * commit-punkternas navigering (Enter/Sök/förslags-val/toolbar) och strippas
 * efter mount. Backend (`ICapturesRecentSearch.Commit`) gatar auto-capturen
 * på den.
 */
export const COMMIT_PARAM = "commit";

/**
 * Adderar commit-intent-suffixet på en redan byggd href (utanför state).
 * Värdet är `true` (inte `1`) — ASP.NET Core minimal-API:s `bool`-binding
 * använder `bool.TryParse`, som tolkar "true"/"false" men INTE "1"/"0";
 * `?commit=1` skulle 400:a list-queryn. Backend-paramen är `bool commit`.
 */
export const COMMIT_VALUE = "true";

export function withCommitFlag(href: string): string {
  return href.includes("?")
    ? `${href}&${COMMIT_PARAM}=${COMMIT_VALUE}`
    : `${href}?${COMMIT_PARAM}=${COMMIT_VALUE}`;
}

export function buildJobbHref(state: JobbUrlState): string {
  const params = new URLSearchParams();
  for (const v of state.occupationGroup)
    params.append("occupationGroup", v);
  for (const v of state.region) params.append("region", v);
  for (const v of state.municipality) params.append("municipality", v);
  // Klass 2 — upprepade params, samma som dimensionerna ovan (ADR 0042
  // Beslut B). Ordnade efter ort/yrke så delningsbara URL:er får stabil form.
  for (const v of state.employmentType) params.append("employmentType", v);
  for (const v of state.worktimeExtent) params.append("worktimeExtent", v);
  // #454 PR-0 — arbetsgivar-filtret (singel-org.nr). Skrivs BARA ut när satt
  // (frånvaro = ren URL). Placeras efter Klass-2-dimensionerna, före
  // matchGrades (stabil URL-form för delningsbara länkar).
  if (state.employer) params.set("employer", state.employer);
  // STEG 5 — matchningsgrad (enum-namn). Upprepad param efter Klass-2-
  // dimensionerna, före q (stabil URL-form för delningsbara länkar). Tom
  // lista = inget param = alla grader visas (när matchningen är PÅ).
  for (const v of state.matchGrades) params.append("matchGrades", v);
  // issue #292 — matchnings-huvudbrytaren. Skriv BARA ut när off (PÅ = paramens
  // frånvaro, ren URL). Placeras efter matchGrades, före q (stabil URL-form).
  if (state.matchningOff) params.set(MATCHNING_PARAM, MATCHNING_OFF_VALUE);
  // #300 PR-5 — "Visa relaterade också"-toggle:n. Skriv BARA ut när on (AV =
  // paramens frånvaro, ren URL). Placeras direkt efter matchning, före q (stabil
  // URL-form, intill matchnings-axelns övriga params).
  if (state.includeRelated) params.set(RELATERADE_PARAM, RELATERADE_ON_VALUE);
  // #383 → förenklat — "Dölj ansökta". Skriv BARA ut när på (AV = paramens
  // frånvaro, ren URL). Placeras efter matchnings-axelns params, före q.
  if (state.hideApplied) params.set(DOLJ_ANSOKTA_PARAM, STATUS_ON_VALUE);
  // #419 pt1 — "Visa bara matchade". Skriv BARA ut när på (AV = paramens frånvaro, ren
  // URL). Placeras efter "Dölj ansökta", före q (stabil URL-form, intill status-paramen).
  if (state.onlyMatched) params.set(BARA_MATCHADE_PARAM, STATUS_ON_VALUE);
  const q = state.q.trim();
  if (q.length > 0) params.set("q", q);
  if (state.sortBy !== DEFAULT_SORT_BY) params.set("sortBy", state.sortBy);
  if (state.pageSize) params.set("pageSize", state.pageSize);
  const qs = params.toString();
  return qs.length > 0 ? `/jobb?${qs}` : "/jobb";
}

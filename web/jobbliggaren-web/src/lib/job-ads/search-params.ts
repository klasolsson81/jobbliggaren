import { Q_MIN_LENGTH, type JobAdSortBy } from "@/lib/dto/job-ads";

/**
 * Centraliserad searchParams-builder för /jobb (F4). Hero-filter-popovers
 * och result-toolbarens sort-dropdown bygger URL:en HÄR — symmetriskt
 * param-bevarande (samma lärdom som F3 B-FIX: två ytor som skriver samma
 * URL får inte radera varandras params).
 *
 * Kontrakt (ADR 0042 Beslut B; axel-SERIALISERINGEN ändrad 2026-08-01, se
 * {@link JOBB_AXIS_SEPARATOR} — semantiken per axel är oförändrad):
 * - `occupationGroup` / `region` / `municipality` = ETT query-param per axel med
 *   conceptId:na joinade av {@link JOBB_AXIS_SEPARATOR} (UPPREPADE params fram
 *   till 2026-08-01; {@link toStringList} läser fortfarande båda formerna).
 *   `occupationGroup` = ssyk-level-4/yrkesgrupp (ADR 0067 Fas E2a nivå-skifte).
 *   `municipality` = kommun (Fas E2b — backend kombinerar region∪municipality
 *   som union, ADR 0067 impl-notat E2b).
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
  // #551 punkt 4 — distans. Samma ORT-dimension som region/municipality, inte en
  // egen ortogonal axel: backend unionerar den in i geo-predikatet (kommun ∨ län ∨
  // remote, JobAdSearchComposition.ApplyFilter D5), så Distans BREDDAR ort-valet i
  // stället för att skära i det. En BOOLEAN, därför inte en joinad lista.
  //
  // REQUIRED, till skillnad från matchningOff/includeRelated/hideApplied. De är
  // runtime-view-state som vissa byggare medvetet inte bär, och deras frånvaro är
  // ofarlig. Distans är en FACETT som når backend — utelämnad tappas den tyst ur
  // varje href den byggaren producerar. Ett obligatoriskt fält gör det till ett
  // kompileringsfel för varje byggare som KONSTRUERAR en JobbUrlState.
  //
  // Det greppet är inte heltäckande, och att tro det är farligt: en producent som
  // inte konstruerar typen — no-JS-formulärets råa hidden inputs i
  // jobb-hero-search — når kompilatorn inte, och den tappade axeln i just den
  // vägen tills en granskare läste den. Required-fältet stänger konstruktions-
  // platserna; resten kräver ett svep på egenskapen.
  //
  // (`OrtChoice.remote` är optional och betyder något ANNAT: att ytan saknar
  // axeln. Två optionaliteter som ser lika ut på anropsplatsen är precis hur den
  // här buggen uppstod.)
  remote: boolean;
  // Klass 2 (ADR 0067 Fas E, 2026-06-13) — Klass-2-filterpanelens dimensioner.
  // `employmentType` = anställningsform (JobTech `employment-type`, ~8,
  // checkbox-multi). `worktimeExtent` = omfattning (JobTech `worktime-extent`,
  // Heltid/Deltid, radio-single → 0 eller 1 element). Ett param per axel med
  // värdena joinade (samma kontrakt som occupationGroup/region/municipality,
  // ADR 0042 Beslut B + axel-serialiseringen 2026-08-01). Backend filtrerar på ?employmentType=/?worktimeExtent= (B2/#60).
  // Panel-valda (aldrig text-representabla i hero-fältet — som popover-
  // dimensionerna, CTO VAL 4a; lever bara i URL-state + filter-raden).
  employmentType: ReadonlyArray<string>;
  worktimeExtent: ReadonlyArray<string>;
  // STEG 5 (grade-filter, 2026-06-23) — matchningsgrad-filtret. Bär
  // ENUM-NAMN (`Basic` | `Good` | `Strong`, ALDRIG `Top` — listfiltret är
  // Fast-bandet och kan inte beräkna Toppmatch; backend-validatorn avvisar
  // `Top`). Svenska labels (Grund | Bra | Stark) lever bara i UI, aldrig i
  // URL:en (samma regel som occupationGroup som bär concept-id, inte i18n).
  // Ett query-param med enum-namnen joinade (?matchGrades=Strong.Good), samma
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
  // arbetsgivar-filtret: en LISTA av org.nr (vardera exakt 10 siffror, validerade i
  // page.tsx). Backend har bundit `string[]` hela tiden; sedan #1547 bär FE hela listan,
  // eftersom Översiktens summor spänner över varje bevakat företag. Tom lista = inget
  // arbetsgivar-filter (ren URL). Aldrig text-representabelt i hero-fältet
  // (som popover-dimensionerna, CTO VAL 4a); syns som avtagbar chip i
  // toolbaren.
  //
  // ⚠ Här stod "FE emitterar ALDRIG en pnr-shaped employer-param (länk-producenten gatar
  // på IsProtectedIdentity — ADR 0087 D8(c))". Grinden vaktade en tom mängd mellan
  // `aca39970` (`company-lookup.tsx` raderad, #997/#1030) och #1547, som gör
  // `company-jobs-href.ts` till producent igen. Grinden ligger hos ANROPAREN
  // (`company-watch-row.tsx`), inte i byggaren, och vaktar en verklig mängd.
  // Persistens-grinden i `RecentJobSearchCaptureBehavior` (A2) är fortfarande det andra
  // ledet; se `parseEmployerParam` nedan för hela mätningen.
  employer?: ReadonlyArray<string>;
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

/**
 * #551 punkt 4 — Distans-facettens URL-param. Svenskt namn (paritet rutterna
 * /jobb /ansokningar + `matchning`/`relaterade`/`doljAnsokta`); värdet `on` är ett
 * stabilt sentinel-ord (inte i18n, samma regel som de övriga). Endast `on` skrivs
 * ut — AV-läget är paramens FRÅNVARO så default-URL:en förblir ren.
 *
 * FE mappar den till API-kontraktets ENGELSKA flagga `remote`, och gör det med ett
 * annat VÄRDE: endpointen binder en `bool` (`JobAdsEndpoints`, `bool remote = false`),
 * så wire-formen är `?remote=true`. ASP.NET binder INTE "on" till en bool — hade vi
 * skickat vidare sentinel-ordet rakt av hade facetten tystnat i stället för att
 * filtrera. Två namn OCH två värden, en översättning: `?distans=on` → `?remote=true`.
 */
export const DISTANS_PARAM = "distans";
export const DISTANS_ON_VALUE = "on";

export const DEFAULT_SORT_BY: JobAdSortBy = "PublishedAtDesc";

/**
 * The separator that joins the values of ONE axis into ONE query value.
 *
 * **Why one occurrence per key at all.** Next's client router cache keys a route
 * by its URL and collapses REPEATED query keys to the last value, so
 * `?employmentType=A&employmentType=B` and `?employmentType=B` hash to the same
 * entry. Navigating from the first to the second — which is what unticking a
 * non-last value does — targets a URL the cache believes it already holds: no RSC
 * request, no re-render, and the panel snaps back to the state the URL no longer
 * describes. Upstream vercel/next.js#92152 and its fix PR #93368, both open on
 * 2026-08-01; we run 16.2.11. Measured on this surface before the change: the two
 * colliding transitions produced ZERO RSC navigations and left all three
 * checkboxes ticked against a URL carrying two.
 *
 * **Why `.` here when `/foretag/sok` uses `-`.** That surface's axes are SCB
 * codes, which are digits only, so `-` cannot occur in a value. These axes carry
 * JobTech conceptIds, whose grammar this system STATES and enforces:
 * `SearchCriteria.ConceptIdPattern` = `^[A-Za-z0-9_-]{1,32}\z`, applied by
 * `ListJobAdsQueryValidator`, `GetFacetCountsQueryValidator` and
 * `GetRemoteAdCountQueryValidator` at every entry point.
 *
 * That pattern is what decides the separator, and it decides it against `-`:
 * **`-` is INSIDE the charset, so joining on it would be ambiguous by contract,
 * not merely by today's data.** `.` is outside it, so no legal conceptId can ever
 * contain one. `*` was rejected alongside: RFC 3986 makes `*` a reserved
 * sub-delim while `.` is unreserved, so no parser downstream may reassign it.
 * Both survive `URLSearchParams.toString()` unencoded, where `,` becomes `%2C`
 * and would disfigure every shared link.
 *
 * An earlier version of this comment claimed JobTech "publishes no grammar" and
 * rested the choice on a sweep of today's corpus. That was false about this
 * repo — the grammar above is enforced in the domain — and it argued a correct
 * decision from a weaker premise than the one actually available
 * (dotnet-architect, #1144). The guard therefore lives against the PATTERN, not
 * against a snapshot: see the separator test in `search-params.test.ts` and
 * `TaxonomyConceptIdGrammarTests` (in `Jobbliggaren.Application.UnitTests`),
 * which asserts the shipped corpus through the query validator a /jobb search
 * actually hits.
 *
 * The two surfaces are deliberately allowed to differ, and the knowledge is
 * deliberately NOT shared: what they have in common is join/split, which is
 * mechanism; the separator itself is knowledge about two different id spaces.
 * See also {@link toStringList}'s note on the sibling's same-named parser.
 */
export const JOBB_AXIS_SEPARATOR = ".";

/**
 * Serialize ONE axis into ONE query value — the counterpart to the split in
 * {@link toStringList}, and the single place the joined form is produced.
 *
 * Exported because this route has a producer that cannot call a URL builder: the
 * hero search island's no-JS `<form>` serialises its own hidden fields, so a
 * native GET writes whatever shape those fields have. On `/foretag/sok` that form
 * was a FOURTH producer still emitting the repeated shape after the three
 * builders had moved, and on its own it was enough to put the collision back
 * (code-reviewer, #1134). It is the same class of producer here.
 *
 * **The guard, and why it filters rather than throws.** A value containing the
 * separator would serialise to a string that parses back as two values. This
 * cannot happen for a legal conceptId — `ConceptIdPattern` excludes `.` — so it
 * is defence in depth against a value that never should have reached us, not a
 * live hazard. Dropping rather than throwing follows the route's established
 * drop-unknown discipline (parity `matchGrades`, `parseEmployerParam`): this runs
 * inside a Server Component render, a client transition and hidden-input
 * rendering, and a manipulated value must never turn the page into an error
 * boundary.
 *
 * Two consequences of dropping, stated because both are easy to get wrong:
 *
 * - It does **not** always narrow. Dropping the LAST surviving value of an axis
 *   makes `setAxis` omit the param entirely, which removes the filter and
 *   therefore WIDENS the result set (`buildJobbHref({...empty, region: ["a.b"]})`
 *   → `/jobb`, pinned below). Safe here — every axis is a display filter over the
 *   same auth-gated corpus, and none gates access — but "dropping only narrows"
 *   is the wrong sentence to reason from next time (security-auditor, #1144).
 * - A drop breaks the `buildJobbHref(state)` → `state` round-trip, and
 *   `sameUrlState` compares element-wise at three call sites in
 *   `jobb-hero-search.tsx`, so a dropped value would leave committed and parsed
 *   state permanently unequal. That path is dead only because no legal conceptId
 *   can contain the separator — it is the PATTERN that keeps it dead, not this
 *   filter. The EMPTY-value drop below has the same consequence and a different
 *   reason for being dead: `toStringList` filters empties on both entry paths,
 *   so no parsed state can carry one into a build (code-reviewer, #1144).
 *
 * Empty values are filtered for a different reason: a trailing separator (`"a."`)
 * is the classic way a pasted link breaks, because auto-linkers in Slack, Outlook
 * and most clients read a terminal period as sentence punctuation and chop it,
 * handing the recipient a silently truncated URL (design-reviewer, #1144).
 *
 * **Deliberately NOT sorted**, unlike the sibling's `serializeCodeAxis`.
 * `sameList` (`tokenize.ts`) compares element-by-element in order, and
 * `sameUrlState` is load-bearing at three call sites including the hero mirror
 * field's own-roundtrip detector; sorting here would make a set-equal pair compare
 * unequal. Sorting only buys a canonical URL form and is not needed to remove the
 * collision.
 */
export function serializeJobbAxis(values: ReadonlyArray<string>): string {
  return values
    .filter((v) => v.length > 0 && !v.includes(JOBB_AXIS_SEPARATOR))
    .join(JOBB_AXIS_SEPARATOR);
}

/** Write one axis as a single param, omitting it entirely when empty (clean URL). */
function setAxis(
  params: URLSearchParams,
  key: string,
  values: ReadonlyArray<string>
): void {
  const joined = serializeJobbAxis(values);
  if (joined.length > 0) params.set(key, joined);
}

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

/**
 * #454 PR-0 — SPOT-parser för `?employer=`-paramen (delas av page.tsx och
 * buildPageHref — samma gate på båda ställen, annars divergerar de). Endast
 * ett exakt 10-siffrigt värde accepteras (`^\d{10}$` — samma form som
 * backend-validatorns OrganizationNumberPattern); allt annat droppas tyst så
 * en manipulerad URL aldrig 400:ar list-queryn (drop-unknown-disciplinen,
 * paritet matchGrades). Hela listan returneras, dedupad och ordningsbevarande — inte
 * dess första element. OBS: detta är en FORMAT-gate, ingen pnr-diskriminator — ett
 * 10-siffrigt personnummer är formatidentiskt med org.nr.
 *
 * ⚠ SKYDDET SOM STOD HÄR ÄR BORTA, mätt 2026-08-19. Det löd: "det lastbärande skyddet
 * är att FE-producenterna aldrig emitterar en pnr-shaped länk (IsProtectedIdentity-gaten)
 * och backend-maskningen". Den producenten var `company-lookup.tsx:204/:210`, raderad i
 * `aca39970` (#997/#1030) — grinden vaktade en tom mängd fram till #1547. Producenterna har
 * OLIKA grindar: `buildCompanyJobsHref` (`company-jobs-href.ts`),
 * anropad från bevakningsraden, gatar hos ANROPAREN på
 * `!isProtectedIdentity && organizationNumber`; typeaheadens arbetsgivarförslag
 * (`composeSuggestionChip`) gatar i fyra led; en replayad senaste sökning
 * (`recent-search-href.ts`, #1471) gatar på servern (`EmployerAxisGate`).
 * En ny producent måste bära en egen grind — ingen ärvs. Utöver den round-trippar
 * `buildJobbHref`, `buildPageHref` och toolbarens
 * `commit()` round-trippar värdet ur URL:en, så en handskriven param återkommitteras av varje
 * toolbar-handling som bär commit-intent. Vilka de är avgörs av `commit()` mot `navigate()` i
 * `jobb-results-toolbar.tsx`, inte av en lista här. Backend-maskningen (ADR 0087 D8(c)) når inte
 * hit heller.
 *
 * Det som FAKTISKT skyddar sedan 2026-08-19 är persistens-grinden i
 * `RecentJobSearchCaptureBehavior` (A2, Klas-beslut): en pnr-formad employer capturas
 * aldrig till `recent_job_searches`. Format-gaten här är med flit INTE vidgad — en
 * pnr-diskriminator i läsvägen hade brutit ett legitimt filter på en enskild firmas
 * annonser, som är riktiga annonser.
 */
export function parseEmployerParam(
  raw: string | string[] | undefined
): ReadonlyArray<string> {
  // Deduped, order-preserving. Order is load-bearing: `sameList` compares element-wise, and
  // `sameUrlState` is the hero mirror field's own-roundtrip detector, so a set-equal pair in a
  // different order would compare unequal. Producers therefore emit a stable order.
  const seen = new Set<string>();
  for (const value of toStringList(raw)) {
    const trimmed = value.trim();
    if (/^\d{10}$/.test(trimmed)) seen.add(trimmed);
  }
  return [...seen];
}

/**
 * #847 — the boundary parser for `?q=`, shared by `page.tsx` (entry) and
 * {@link buildPageHref} (pagination links).
 *
 * `searchParams` is untrusted external input. Next.js delivers `string[]` for a
 * repeated query param, so hand-typing `q?: string` asserted a guarantee the
 * runtime does not make: `/jobb?q=a&q=b` reached `.trim()` with an array and
 * threw `TypeError: q.trim is not a function` (measured), painting the
 * technical-error card instead of running a search. Reachable through a shared,
 * bookmarked or hand-edited link — no client-side guard covers those, the same
 * class of path #823 exists for. (The hero form cannot produce it: the visible
 * field is nameless after hydration and the hidden `q` only renders when
 * hydrated, so the two are mutually exclusive.)
 *
 * Arity: `q` is semantically single-valued — one search term — so a repeated
 * param collapses to the FIRST value, the coercion `parseEmployerParam` already
 * documents, NOT `toStringList`'s `string[]`. The first element is taken
 * strictly, without skipping blanks: reading element 1 because element 0 was
 * empty would be a guess about intent, and `?q=&q=backend` is a mangled URL,
 * not a stated search.
 *
 * This is the ONLY exported q-parser, deliberately: the sub-minimum clamp below
 * is module-private, so neither of the two /jobb URL paths can consume `q` while
 * skipping the shape guard. One normaliser, one rule — the divergence #823 and
 * #846 each had to close by hand.
 *
 * Scope of that claim, stated so it stays checkable: it covers the page entry and
 * {@link buildPageHref}, not every `q` reader in the app. `app/api/jobb/facet-counts/
 * route.ts` reads `q` off a `URLSearchParams` (`.get()` already returns the first
 * value, so it cannot hit the array crash) and deliberately skips the clamp — a
 * sub-minimum `q` there returns wider facet counts, it does not fail. Pre-existing.
 */
export function parseQParam(
  raw: string | string[] | undefined
): string | undefined {
  return clampSubMinimumQ(Array.isArray(raw) ? raw[0] : raw);
}

export function buildJobbHref(state: JobbUrlState): string {
  const params = new URLSearchParams();
  setAxis(params, "occupationGroup", state.occupationGroup);
  setAxis(params, "region", state.region);
  setAxis(params, "municipality", state.municipality);
  // Klass 2 — one param per axis, same as the dimensions above. Ordered after
  // ort/yrke so shared URLs keep a stable form.
  setAxis(params, "employmentType", state.employmentType);
  setAxis(params, "worktimeExtent", state.worktimeExtent);
  // #454 PR-0 — arbetsgivar-filtret. En LISTA sedan Översiktens summor länkar till
  // annonserna hos varje bevakat företag på en gång; backend har bundit `string[]` hela
  // tiden (ADR 0087 D6). Skrivs BARA ut när icke-tom (frånvaro = ren URL). Placeras efter
  // Klass-2-dimensionerna, före matchGrades (stabil URL-form för delningsbara länkar).
  setAxis(params, "employer", state.employer ?? []);
  // STEG 5 — matchningsgrad (enum-namn). One param, placed after the Klass-2
  // dimensions and before q (stable URL form for shared links). An empty list
  // writes no param = every grade is shown (when matching is ON).
  setAxis(params, "matchGrades", state.matchGrades);
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
  if (state.remote) params.set(DISTANS_PARAM, DISTANS_ON_VALUE);
  const q = state.q.trim();
  if (q.length > 0) params.set("q", q);
  if (state.sortBy !== DEFAULT_SORT_BY) params.set("sortBy", state.sortBy);
  if (state.pageSize) params.set("pageSize", state.pageSize);
  const qs = params.toString();
  return qs.length > 0 ? `/jobb?${qs}` : "/jobb";
}

/**
 * De RÅA searchParams /jobb tar emot, i den form Next.js levererar dem
 * (`string | string[] | undefined` per param — en upprepad query-param blir en
 * array). Används bara för att bygga pagineringslänkar: {@link buildPageHref}
 * bär vidare exakt de params sidan självt läste, så ett sida-2-klick aldrig
 * tappar ett filter.
 *
 * Skild från {@link JobbUrlState}, som är det TOLKADE tillståndet (listor
 * normaliserade, flaggor booleaniserade). Den här typen är avsiktligt otolkad:
 * den beskriver ostrukturerad extern input, inte vår modell av den.
 *
 * #846 — flyttad hit från `jobb-results.tsx` tillsammans med `buildPageHref`.
 *
 * ⚠ OFULLSTÄNDIG mot vad `/jobb` faktiskt läser: `matchning` SAKNAS. `page.tsx`
 * deklarerar `matchning?: string`, läser `params.matchning === MATCHNING_OFF_VALUE`
 * och skickar hela `params` som `rawParams` — så värdet NÅR `buildPageHref` och
 * släpps på golvet. Följd: `/jobb?matchning=off` + "Nästa sida" ⇒ länken saknar
 * `matchning=off` ⇒ matchningen slås PÅ igen på sida 2 (badges tillbaka) medan
 * `matchGrades` bärs vidare. Samma felklass som varje bevarande-rad i
 * `buildPageHref` bär en kommentar om, och samma "en URL vi själva genererar som
 * påstår ett tillstånd sidan inte kör" som #823. **Pre-existerande** — den gamla
 * inline-typen saknade fältet också; #846 flyttade defekten, införde den inte.
 * Bärs som eget steg i epik #1032 (code-reviewer Major, PR #1037).
 */
export interface JobbRawSearchParams {
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
  // #551 punkt 4 — bärs i paginerings-href:en så sida-2-klicket inte tappar
  // Distans-facetten (samma felklass som ovan).
  distans?: string;
  // #454 PR-0 — bärs i paginerings-href:en så sida-2-klicket inte tappar
  // arbetsgivar-filtret (samma felklass som ovan).
  employer?: string | string[];
  // #847 — `string | string[]`, like every other param here: Next.js delivers an
  // array for a repeated query param. Typing this `string` did not make it one;
  // it only hid the crash from `tsc`. Normalised by `parseQParam`.
  q?: string | string[];
}

// Normaliserar string | string[] | undefined → string[] (tomma värden bort).
//
// Accepts BOTH shapes, and that is the whole back-compat story: the joined form
// this module writes from 2026-08-01 (`?municipality=a.b`) and the repeated form
// it wrote before (`?municipality=a&municipality=b`), which every previously
// shared or bookmarked link still carries. Both parse to the same values, so no
// redirect and no migration are needed and a reader cannot tell which form
// produced the state. Splitting is safe on every axis that reaches here: all six
// carry JobTech conceptIds or the matchGrades enum names, and no legal conceptId
// can contain the separator (`SearchCriteria.ConceptIdPattern`, asserted against
// the shipped corpus by `TaxonomyConceptIdGrammarTests`).
//
// #846 — flyttad hit med `buildPageHref`. EXPORTERAD sedan 2026-08-01, och
// `jobb/page.tsx` importerar den nu i stället för att hålla en byte-identisk
// kopia. Kopian var tråkig men säker ända tills den här ändringen gav dem BÅDA
// en split: code-reviewer mätte att om page-kopian tappade sin split så förblev
// hela sviten grön (280 filer, 3146 tester) medan varje filter i den nya formen
// tyst matchade noll annonser, eftersom inget unit-test importerar page-modulen.
// En delad parser kan inte drifta och ärver unit-testerna här. Epik #1032
// behåller tvär-yte-delen; det här paret är smalare än så.
//
// Kvar finns EN namne, med ANNAT beteende:
// `lib/company-search/search-params.ts` `parseCodeAxis` har SAMMA ROLL men en
// annan separator (`-`), för ett annat id-rum. En framtida "dedupe by name"-
// refaktor som pekar /jobb dit byter TYST separator och bryter varje delad
// /jobb-länk. Rör den inte utan att mäta beteendet — och den ligger i en annan
// lane (CLAUDE.md §6.5).
export function toStringList(raw: string | string[] | undefined): string[] {
  if (raw === undefined) return [];
  const arr = Array.isArray(raw) ? raw : [raw];
  return arr
    .flatMap((v) => v.split(JOBB_AXIS_SEPARATOR))
    .map((v) => v.trim())
    .filter((v) => v.length > 0);
}

/**
 * Paginerings-URL-byggaren för /jobb — den ANDRA av sidans två URL-byggare,
 * vid sidan av {@link buildJobbHref}.
 *
 * #846 — flyttad hit ur `jobb-results.tsx`, där den var modul-privat och
 * exporterades ENBART för sitt test (vilket drog hela Server-Component-grafen
 * in i jsdom). De två byggarna har divergerat förut: #823 fixade att den här
 * re-emitterade ett rått under-minimum-`q`, så `/jobb?q=a` genererade
 * `/jobb?page=2&q=a` — en länk VI producerar som påstår ett sök sidan inte kör.
 * Nu bor båda i samma fil, så nästa divergens syns i diffen.
 *
 * Skillnaden mot `buildJobbHref` är avsiktlig och inte redundans:
 * `buildJobbHref` skriver ett NYTT tillstånd och utelämnar därför alltid `page`
 * (ett filterbyte ska tillbaka till sida 1), medan den här BEVARAR tillståndet
 * och byter bara sida.
 */
export function buildPageHref(
  params: JobbRawSearchParams,
  targetPage: number,
  defaultPageSize: number
): string {
  const url = new URLSearchParams();
  if (targetPage !== 1) url.set("page", String(targetPage));
  if (params.pageSize && Number(params.pageSize) !== defaultPageSize) {
    url.set("pageSize", params.pageSize);
  }
  if (params.sortBy && params.sortBy !== DEFAULT_SORT_BY) {
    url.set("sortBy", params.sortBy);
  }
  // Every axis is re-serialised through the SAME writer `buildJobbHref` uses, so
  // the two builders cannot drift on the URL contract. Parsing with
  // `toStringList` first is also what makes a legacy repeated-form arrival heal:
  // a page-2 link built from one comes out in the joined form.
  setAxis(url, "occupationGroup", toStringList(params.occupationGroup));
  setAxis(url, "region", toStringList(params.region));
  // E2b — utan denna rad tappar sida-2-klicket kommun-filtret (samma
  // felklass som F3 B-FIX; buildPageHref är en ANDRA URL-builder vid
  // sidan av buildJobbHref — architect-dom fråga 4.1).
  setAxis(url, "municipality", toStringList(params.municipality));
  // Klass 2 — utan dessa tappar sida-2-klicket anställningsform/omfattning
  // (samma felklass som municipality ovan; buildPageHref är en andra URL-
  // builder vid sidan av buildJobbHref).
  setAxis(url, "employmentType", toStringList(params.employmentType));
  setAxis(url, "worktimeExtent", toStringList(params.worktimeExtent));
  // STEG 5 — utan denna rad tappar sida-2-klicket grad-filtret (samma felklass
  // som municipality/Klass-2 ovan; buildPageHref är en andra URL-builder vid
  // sidan av buildJobbHref). Page-validatorn droppar Top/okänt redan.
  setAxis(url, "matchGrades", toStringList(params.matchGrades));
  // #300 PR-5 — utan denna rad tappar sida-2-klicket "Visa relaterade också"-
  // toggle:n (samma felklass som matchGrades ovan). Bevaras BARA när on (paritet
  // med buildJobbHref); page.tsx parsar bara on-värdet.
  if (params.relaterade === RELATERADE_ON_VALUE)
    url.set(RELATERADE_PARAM, RELATERADE_ON_VALUE);
  // #383 → förenklat — utan denna rad tappar sida-2-klicket "Dölj ansökta" (samma
  // felklass som relaterade ovan). Bevaras BARA när on (paritet buildJobbHref).
  if (params.doljAnsokta === STATUS_ON_VALUE)
    url.set(DOLJ_ANSOKTA_PARAM, STATUS_ON_VALUE);
  // #419 pt1 — utan denna rad tappar sida-2-klicket "Visa bara matchade" (samma felklass
  // som doljAnsokta ovan). Bevaras BARA när on (paritet buildJobbHref).
  if (params.baraMatchade === STATUS_ON_VALUE)
    url.set(BARA_MATCHADE_PARAM, STATUS_ON_VALUE);
  // #551 punkt 4 — utan denna rad tappar sida-2-klicket Distans-facetten (samma
  // felklass som baraMatchade ovan). Bevaras BARA när on (paritet buildJobbHref).
  if (params.distans === DISTANS_ON_VALUE)
    url.set(DISTANS_PARAM, DISTANS_ON_VALUE);
  // #454 PR-0 — utan denna rad tappar sida-2-klicket arbetsgivar-filtret
  // (samma felklass som ovan; buildPageHref är en andra URL-builder vid sidan
  // av buildJobbHref). SPOT-gaten (parseEmployerParam) delas med page-parsern.
  setAxis(url, "employer", parseEmployerParam(params.employer));
  // #823 — KLAMPA. Utan detta re-emitterar sidlänkarna det råa under-minimum-q:t
  // (/jobb?q=a → "Nästa sida" = /jobb?page=2&q=a): en URL vi själva genererar som påstår
  // ett sök sidan inte kör, medan sökfältet står tomt. Samma SPOT-parser som page.tsx.
  // #847 — parseQParam, inte klampen direkt: den bär arity-koerceringen också, så ett
  // upprepat ?q= inte längre kraschar länkbyggaren (`q.trim is not a function`).
  const clampedQ = parseQParam(params.q);
  if (clampedQ) url.set("q", clampedQ);
  const qs = url.toString();
  return qs.length > 0 ? `/jobb?${qs}` : "/jobb";
}

/**
 * #823 — en söktext kortare än backendens minimum behandlas som INGEN söktext.
 * Speglar `SearchQueryParser`, som nollar en residual under `SearchCriteria.QMinLength`
 * och kör vidare på dimensionerna i stället för att vägra frågan; `ListJobAdsQueryValidator`
 * skulle annars 400:a och sidan måla teknisk-fel-kortet.
 *
 * SPOT: BÅDA URL-vägarna på /jobb måste klampa lika. page.tsx klampar vid entry, och
 * `buildPageHref` (den andra URL-byggaren, ovan i denna fil sedan #846) klampar när den
 * bygger pagineringslänkar — annars re-emitterar sidlänkarna ett q som sidan självt
 * ignorerar, dvs. en URL som påstår ett sök som inte körs. Backend förblir SSOT och sista
 * barriär.
 *
 * #847 — module-private. Both call sites reach the clamp through {@link parseQParam}, so
 * the arity guard cannot be bypassed by consuming the clamp on its own. Before #847 it had
 * two out-of-module consumers: `jobb/page.tsx` (which #847 rewires to `parseQParam`) and
 * its own test. With the production caller gone, keeping the export alive for the test
 * alone would be the test-only-export smell #846 just removed from this module — so the
 * test moves to the boundary parser instead.
 */
function clampSubMinimumQ(q: string | undefined): string | undefined {
  if (q === undefined) return undefined;
  // Returnera det TRIMMADE värdet, inte det råa: annars normaliserar de två callerna olika
  // (page.tsx trimmade en gång extra på egen hand, buildPageHref inte alls) och
  // "/jobb?q=%20ab%20" hade kört sökningen "ab" medan sidlänken re-emitterade "+ab+".
  // Samma divergens-form som klampen infördes för att stänga — och sedan #847 är den
  // omöjlig igen: båda callerna går genom parseQParam, som trimmar HÄR och bara här.
  const trimmed = q.trim();
  return trimmed.length < Q_MIN_LENGTH ? undefined : trimmed;
}

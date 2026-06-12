# dotnet-architect — Fas E2j sök-commit-modellen (arkitekturdom)

**Datum:** 2026-06-12
**Agent:** dotnet-architect (INLINE; senior-cto-advisor beslutar multi-approach efter denna; Klas avgör PRODUKTVAL)
**Scope:** Klas rendered-feedback 2026-06-12 på E2i live-spegel (#53, main `438a770`): (1) recent-search "sparas inte som förväntat" — empiriskt bekräftad over-capture i dev-DB; (2) native × rensar text men inte filter; (3) djup-fråga: behövs Sök-knapp + × när resultat visas live?
**Status:** Design-dom (read-only). Inga kodändringar. CC har inte byggt något.

---

## Sammanfattning

Klas empiriska fynd är en **äkta defekt**, inte en preferens: `RecentJobSearchCaptureBehavior` fångar på VARJE lyckad `ListJobAdsQuery`, och E2i:s live-`router.replace` gör att varje committat ord triggar en ny RSC-render → ny list-query → ny capture. Mellanstegen ("Systemutvecklare", +kommun, +yrkesgrupp som separata rader) vräker ut äkta committade sökningar ur cap=20. Detta är dessutom en **data-minimerings-regression (GDPR Art. 5(1)(c))** — vi samlar in fler söktermsvarianter per identifierad användare än ändamålet (UX för återbesök) motiverar.

**Arkitektur-domen:**
- **Capture-trigger:** Rekommenderad **Variant B (commit-flagga på den befintliga list-queryn)**. Den är *materiellt skild* från ADR 0060:s avvisade Variant B — ingen separat command, ingen extra round-trip, ingen ny trust-yta utöver klientens egen historik. Detta är **mekanik-konkretisering inom ADR 0060:s Accepted-mandat**, kräver **ADR 0060-amendment** (inte ny ADR) eftersom Beslut 3:s avvisnings-formulering måste preciseras.
- **Sök-knappen:** BEHÅLLS — den har fyra legitima jobb (finalisera pågående ord, commit-signal, no-JS-submit, a11y). **PRODUKTVAL** (behåll/ta bort) men arkitekturen rekommenderar starkt behåll.
- **×-semantik:** native × MÅSTE suppress:as + ersättas av kontrollerad knapp (cross-browser + interceptbarhet) — **arkitektur-bestämt, inget val**. *Vad* knappen rensar (text / text+claimade filter / ingen knapp) är **PRODUKTVAL**; rekommendation: rensa text + de filter texten gjorde anspråk på (parse(text)-delmängden), lämna popover-dimensioner.
- **security-auditor: JA, måste triggas** — PII-capture-vägen ändras (trigger-villkoret för insamling av söktermer flyttas).

---

## Del 1 — Capture-trigger (KÄRNAN: FE→BE-commit-signalen)

### Problemets arkitektur

Capture-kedjan är, verifierad ände-till-ände:

```
router.replace (live, per ord)
  → page.tsx searchParams ändras
  → <Suspense key> byts → JobbResults re-renderas (server)
  → getJobAds() → GET /api/v1/job-ads?...   (job-ads.ts:73, buildQuery rad 42)
  → ListJobAdsQuery : ICapturesRecentSearch  (ListJobAdsQuery.cs:29)
  → RecentJobSearchCaptureBehavior  (post-UnitOfWork)
  → SearchCriteria.Create → IRecentJobSearchCapturer.CaptureAsync
  → INSERT/Bump i recent_job_searches, evict vid cap=20
```

Den **kritiska arkitektur-sanningen:** backend kan inte skilja en live `router.replace` från en committad `router.push`/Enter/Sök — **båda är `GET /api/v1/job-ads` med identiska parametrar**. C2-utvidgningen av default-browse-guarden (Behavior rad 56–63) stänger bara *tomma* sökningar; den stänger inte *mellanstegs*-sökningar. Varje icke-tom mellanstegs-URL är per definition en capture-kandidat. Detta är roten: **"capture endast vid commit" KRÄVER en explicit commit-intent-signal från FE** — det finns ingen serverside-heuristik som kan rekonstruera den (att gissa vore "Programming by Coincidence", Hunt/Thomas 1999 kap. 6, exakt den anti-grund CTO redan dömt mot i E2i-addendum Beslut 1).

### Variant-analys

**Variant A — status quo (capture varje list-query).**
**Avvisas — empiriskt falsifierad.** ADR 0060 Beslut 3 skrevs (2026-05-20) i en värld där en sökning = ett `router.push` per användarintention. E2i (2026-06-11) införde live-`router.replace` per ord. Premissen "varje lyckad query ≈ en avsiktlig sökning" är därmed riven. Klas har bekräftat over-capture i dev-DB: cap=20 fylls av mellanstegsspam, och **äkta committade sökningar evictas** — funktionen levererar motsatsen till sitt syfte ("snabbåtkomst till sökningar du faktiskt kört"). Dessutom data-minimerings-regression (se GDPR nedan). A är inte ett alternativ; det är defekten.

**Variant B — commit-flagga på den befintliga list-queryn.** ⟵ **REKOMMENDERAD**
`ListJobAdsQuery` får en `bool Commit = false`-property (default false). FE sätter `&commit=1` ENDAST vid avsiktlig commit (Enter/Sök/förslags-val); live-`router.replace` utelämnar den. `RecentJobSearchCaptureBehavior` läser flaggan via markör-interfacet och no-op:ar när den är false.

*Är detta materiellt ADR 0060:s avvisade Variant B?* **Nej.** ADR 0060 Beslut 3 avvisade "explicit `CaptureRecentSearchCommand` från FE" på fyra grunder. Jag prövar var och en mot commit-flaggan:

| ADR 0060-invändning mot avvisad Variant B | Gäller commit-flaggan (denna B)? |
|---|---|
| "Trust-flytt till klient" | **Materiellt nej.** Den enda trust som flyttas är *när användarens EGEN historik fångas*. Worst case: klienten over- eller under-captar sin egen sökhistorik. Ingen cross-tenant-yta (JobSeeker-lookup sker fortfarande server-side via `currentUser.UserId`, Capturer rad 39–47). Klienten kan inte capture:a för någon annan, inte injicera annan seekers data, inte kringgå auth. Trust-ytan är användarens egen bekvämlighets-historik — lägsta tänkbara känslighet. |
| "Dubbla round-trips" | **Eliminerad.** Capture rider på den list-query som ändå körs. Noll extra HTTP. Detta var avvisade Variant B:s tyngsta tekniska argument — och commit-flaggan har det inte. |
| "Race mellan list-render och capture" | **Eliminerad.** Samma query, samma pipeline, samma UoW-ordning som idag (post-UnitOfWork, Mekanik-not 1). Ingen ny ordnings-race införs. |
| "FE måste persistera filter-shape" | **Falskt här.** Filter-shapen ligger redan i URL:en (E2g-arvet — URL är persistent sanning). FE persisterar inget nytt; den sätter en boolean på en query den redan bygger. |

**Slutsats:** commit-flaggan delar bara *namnet* "Variant B" med det avvisade alternativet. Den avvisade var en **separat command med egen round-trip**; detta är **ett predikat på en befintlig query**. De fyra avvisnings-grunderna träffar inte. Detta är arkitektoniskt närmare ADR 0060:s *accepterade* Variant A (post-handler-behavior, markör-driven) än dess avvisade Variant B — vi behåller behaviorn, markör-pattern, best-effort-semantiken och UoW-ordningen; vi adderar endast ett commit-predikat till markör-kontraktet.

*SPOT/Clean Arch:* flaggan binds som vanlig query-param (samma mekanism som `Since`, `Page`), flödar genom `ICapturesRecentSearch`-shapen (record-property matchar interface automatiskt, samma pattern som idag), och behaviorn lägger till **ett villkor** i sin redan existerande no-op-kedja (Behavior rad 39–63). Ingen ny abstraktion, ingen ny port, ingen Domain-påverkan. `SearchCriteria`-VO:t och Capturer-invarianten är orörda.

**Variant C — separat lättviktig capture-endpoint/command.**
Detta ÄR den literala ADR 0060-avvisade Variant B. Avvisas igen, av samma skäl ADR 0060 angav (extra round-trip, race, separat command som duplicerar filter-shape) — och nu *utan* den ursprungliga motiveringen att det skulle ge renare separation, eftersom commit-flaggan (B) ger samma trigger-precision till en bråkdel av kostnaden. C löser inget B inte löser billigare. YAGNI (Beck; Martin 2017).

**Variant D — live-capture med dedup/coalesce (debounce/replace-last-within-N).**
Avvisas. (1) Den löser fel problem: även perfekt coalescing av en "sök-session" lämnar frågan "vad betyder *sparad* för användaren?" obesvarad — Klas mentala modell är "jag tryckte Sök/Enter ⇒ den sparades", inte "systemet gissade när jag var klar via en timer". (2) Den är stateful och heuristisk — kräver sessions-fönster-spårning i Capturern (vilken rad tillhör samma "session"?), vilket inför exakt den sortens tidsberoende, svårtestade logik CTO avvisade i E2i (blur-finalize, debounced-reparse). (3) Den minskar volym men eliminerar inte over-collection: mellanstegs-q-termer fångas fortfarande innan de coalesce:as. Data-minimering kräver att vi **inte samlar in** mellanstegen — inte att vi städar dem efteråt (Art. 5(1)(c) är *insamlings*-minimering, inte *retention*-minimering). D är mer kod, mer state, sämre GDPR-posture, och missar Klas mentala modell. Avvisas hårt.

### ADR 0060-amendment: krävs det?

**JA — amendment krävs, men ingen ny ADR.** Resonemang:

- Beslut 3:s *substans* (capture via post-handler-behavior, markör-driven, best-effort, Variant A) **består oförändrad**. Vi river den inte.
- MEN Beslut 3:s *avvisnings-text* för "Variant B" är nu tvetydig: en framtida läsare ser "explicit FE-signal avvisad" och skulle felaktigt läsa commit-flaggan som ett brott mot en Accepted ADR. Det är exakt den "ADR-mekanik-ordalydelse ≠ miljö/fas-entydig"-situation som memory `feedback_adr_mechanism_vs_env_phase_triage` (TD-13 C1 J3) varnar för: en Accepted-mekaniks ordalydelse kolliderar med en ny fas-verklighet (live-sök fanns inte 2026-05-20).
- Precedensen är E2b/D2-notaten: mekanik-konkretisering *inom* ett Accepted-mandat dokumenteras som amendment, inte ny ADR. Här är trigger-villkoret (NÄR behaviorn fångar) en konkretisering av Beslut 3:s "varje lyckad ICapturesRecentSearch-query" → "varje lyckad ICapturesRecentSearch-query *med commit-intent*".

Amendmentet ska: (a) konstatera att live-sök (E2i) rev Beslut 3:s implicita premiss "en query = en intention"; (b) precisera att commit-flaggan INTE är det avvisade Variant-B-alternativet (citera de fyra grunderna + varför de inte träffar — tabellen ovan); (c) uppdatera Mekanik-not 2 (default-browse-guarden får sällskap av en commit-guard); (d) notera GDPR-data-minimerings-förstärkningen (se Del 4).

**Detta är ett CTO-beslut att verkställa + adr-keeper att författa. Klas-GO på amendment-substansen behövs eftersom det rör en Accepted-ADR:s avvisnings-dom** (CTO flaggar om Klas måste godkänna — min bedömning: ja, ADR-amendment som omtolkar en avvisad variant är en sådan strategisk transition Klas ska se, även om mekaniken är entydig).

---

## Del 2 — Sök-knappens semantik (PRODUKTVAL, arkitektur-rekommendation: BEHÅLL)

**Frågan:** om resultat visas live, behövs Sök-knappen?

**Arkitektur-domen: ja, behåll.** Knappen har fyra distinkta, icke-redundanta jobb — och tre av dem blir *nödvändiga* så fort capture-på-commit (Del 1) införs:

1. **Finalisera pågående ord (CTO VAL 3-arvet).** E2i exkluderar ordet under caret från parse (caret-segment-exkludering, tokenize.ts rad 123–143). Skriver användaren "göteborg" utan avslutande mellanslag och förväntar sig att söka på det — bara Enter/Sök kör `onSubmitText()` som finaliserar HELA texten utan caret-undantag (jobb-hero-search.tsx rad 271–273). **Utan knappen finns ingen väg att committa ett pågående sista ord annat än att skriva ett mellanslag** — en upptäckbarhets-fälla.
2. **Commit-signalen (Del 1).** Sök/Enter är den naturliga platsen att sätta `commit=1`. Tar vi bort knappen finns färre explicita commit-punkter och capture-på-commit blir otydligare för användaren.
3. **No-JS-submit.** `<form action="/jobb" method="get">` (jobb-hero-search.tsx rad 286) submittar via knappen utan JS. Tar vi bort den submit-knappen bryter vi progressive enhancement (§5.2). Enter i ett ensamt textfält submittar visserligen formet, men en synlig submit-kontroll är WCAG-förväntad och no-JS-robust.
4. **A11y (combobox + submit).** WAI-ARIA combobox-mönstret (typeahead) hanterar förslags-val; men "kör sökningen" är en separat affordance. En screenreader-användare som skrivit fritext utan att välja förslag behöver en explicit "Sök"-kontroll. Hjälptexten (rad 329–333) refererar redan beteende; knappen är ankaret.

**Konflikt med E2i-spegelmodellen?** Nej. Enter är redan wired till `onSubmitText` via formets `onSubmit` (rad 290–293) → `runDelta(text, null)`. Sök-knappen är `type="submit"` i samma form → samma väg. Att addera capture-på-commit innebär att `onSubmitText` (och förslags-val `onSelectSuggestion`, rad 233) är de punkter som ska bära commit-intent. Live-`onFieldChange`-delimiter-committen (rad 220–221) ska INTE bära commit-intent. Detta mappar rent mot C′-modellens befintliga commit-punkt-taxonomi — **ingen ny mekanism, en flagga på rätt subset av befintliga commit-punkter.**

**Om Klas ändå vill ta bort knappen:** då måste finalisera-pågående-ord, no-JS-submit och a11y-submit lösas på annat vis (Enter-only + synlig instruktion), och commit-intent måste härledas ur Enter/förslags-val enbart. Görbart men strikt sämre. **Rekommendation: behåll. Detta är PRODUKTVAL — Klas avgör.**

---

## Del 3 — ×-clear-semantik

### Arkitektur-bestämt (inget val): native × MÅSTE bort

`<input type="search">` (job-ad-typeahead.tsx rad 199) renderar WebKit/Blinks `::-webkit-search-cancel-button`. Verifierat beteende: klick rensar `value` och fyrar `onChange("", 0)` → `onFieldChange` (rad 213) — men `onFieldChange` kör bara `runDelta` när tecknet före caret är avgränsare (rad 219–221). Tom sträng ⇒ inget tecken före caret ⇒ ingen delta ⇒ **URL/filter överlever, texten försvinner.** Resultatet: fältet tomt, jobben fortfarande filtrerade. Firefox visar aldrig knappen → cross-browser-inkonsekvens.

**Domen:** suppress native via `-webkit-appearance: none` (eller `[&::-webkit-search-cancel-button]:appearance-none` Tailwind 4.2-syntax) och rendera en **kontrollerad custom clear-knapp** vars onClick går genom en explicit React-väg. Detta är inte ett produktval — det är en korrekthets- och cross-browser-konsekvens. **Oavsett vilken semantik Klas väljer nedan måste den native knappen bort och ersättas med en interceptbar kontroll.**

### PRODUKTVAL: vad rensar ×?

Tre alternativ, mot E2i:s I1-invariant (`parse(text) ⊆ state`):

**(i) Rensa endast text.**
Rensar fältet, lämnar ALLA filter (popover + claimade). Bryter inte I1 (tom text ⇒ parse(∅) ⊆ state trivialt). MEN: reproducerar exakt dagens förvirrande beteende som Klas rapporterade som bugg — "text borta, jobb kvar filtrerade". Avvisas som default; det är problemet, inte lösningen.

**(ii) Rensa text + de filter texten gjorde anspråk på (parse(text)-delmängden), lämna popover-dimensioner.** ⟵ **REKOMMENDERAD**
× kör en delta som tar bort exakt `parse(text)`-claimen ur staten (samma `applyClaimsDelta`-maskineri som E2i redan har, med `next = EMPTY_CLAIMS`), och tömmer texten. Popover-valda dimensioner (som texten aldrig claimade, I1:s "state får bära mer") **överlever** — vilket är konsistent med C′:s grundaxiom: *fältet äger sitt eget bidrag, inte hela staten* (CTO VAL 1, architect M1). Detta är minst förvånande: "× på sökfältet rensar det jag skrev i sökfältet och dess effekt; mina popover-val rör den inte". Det speglar precis vad fältet visar.

*Interaktion med E2i-mekaniken (verifierad):* detta är en **egen commit** (router.replace), inte en extern divergens. Den ska gå genom `commit()` (rad 187) så `recentCommits`-detektorn (rad 145, 148–150) registrerar den som egen roundtrip → texten serialiseras INTE om vid props-retur (annars E2d/E2h-felklassen). Konkret: × sätter `text=""`, kör `runDelta`-liknande logik med tomma claims mot `lastCommitted`, committar resultatet. `prevClaims` nollas till `EMPTY_CLAIMS`. Detta är symmetriskt med hur `onFieldChange` redan fungerar — **ingen ny invariant-risk**, det är delta-vägen med tomt mål.

**(iii) Ta bort × helt.**
Förlitar sig på Backspace/markera-allt-radera. Förenklar men tar bort en Platsbanken-paritets-affordance (Platsbanken-sökfältet har clear-×) och en standard search-input-konvention (Safari/Algolia/GOV.UK search-mönster har clear). Avvisas mot paritets-baseline (memory `project_platsbanken_parity_baseline`).

**Rekommendation: (ii).** Men capture-trigger har produkt+GDPR-vikt och × kopplar till "vad är en sökning" — så **Klas bekräftar.** Notera: (ii) kräver att × går genom samma `commit()`/`recentCommits`-väg som övriga egna commits — annars bryts own-roundtrip-detektorn. Det är en implementations-invariant CTO ska binda, inte ett val.

---

## Del 4 — GDPR / data-minimering (security-auditor-trigger)

ADR 0060 Mekanik-not 5/6 fastställer: `q` är PII (söktermer kan vara person-/företagsnamn), klartext i Postgres, rättslig grund berättigat intresse (Art. 6(1)(f)), Art. 13-disclosure krävd.

**Data-minimerings-analysen (Art. 5(1)(c)):** att fånga varje mellanstegs-keystroke-commit innebär att vi persisterar söktermsvarianter användaren aldrig avsåg som "en sökning" — "system", "system ut", "systemutvecklare", "systemutvecklare göt"... Detta är **over-collection**: vi lagrar fler PII-bärande söktermer än ändamålet (snabbåtkomst till avsiktliga sökningar) kräver. Capture-på-commit (Del 1, Variant B) **stärker data-minimerings-posturen materiellt** — vi samlar in endast de sökningar användaren explicit committade, vilket är den minimala mängd som ändamålet kräver. Detta är inte bara en UX-fix; det är en GDPR-förbättring och bör stå i amendmentet som sådan.

**security-auditor MÅSTE triggas. JA.** Motivering, mot CLAUDE.md §9.2 ("kod som rör PII"): capture-trigger-villkoret är PII-insamlings-vägen. Vi ändrar NÄR söktermer (PII) persisteras. Även om ändringen *minskar* insamling måste auditorn verifiera: (a) att live-`router.replace` bevisligen INTE längre fångar (ingen läcka via en glömd commit-flagga); (b) att commit-flaggan inte kan forgeras till oönskad capture (worst case är benignt — egen historik — men auditorn ska konstatera det); (c) att Art. 13-disclosuren fortfarande är korrekt (om något, nu mer sanningsenlig: "vi sparar sökningar du kör" blir bokstavligt sant). Detta är en obligatorisk invocation, inte en valfri.

---

## Del 5 — No-JS-fallback (uniform commit för JS + no-JS)

**Frågan:** kan ett hidden `commit=1`-input på formet ge capture-på-commit för BÅDE JS- och no-JS-vägar?

**Ja — och det är den rena lösningen.** Verifierad mekanik:

- **No-JS:** `<form action="/jobb" method="get">` (jobb-hero-search.tsx rad 286) submittar med alla hidden inputs (rad 345–370). Ett `<input type="hidden" name="commit" value="1">` i formet skulle åka med vid native submit → landar i `page.tsx` searchParams → `JobbResults` → `getJobAds` → backend. **No-JS submit ÄR per definition en commit** (användaren tryckte Sök), så `commit=1` alltid-på i no-JS-formet är korrekt.
- **JS:** här är det subtilare. Hidden inputs reflekterar `lastCommitted` (URL-sanningen). Live-`router.replace` (onFieldChange-delimiter) bygger sin href via `buildJobbHref(next)` (rad 191) — den ska INTE inkludera `commit=1`. Endast `onSubmitText`/`onSelectSuggestion`-vägarna ska. Så i JS-vägen är `commit` INTE ett statiskt hidden input utan en **parameter som selektivt adderas till href:en vid commit-punkter**.

**Konsekvens för URL-renlighet:** `commit=1` får INTE bli en persistent del av URL-sanningen — annars (a) skulle den delas i shareable länkar och re-trigga capture vid varje besök på den länken, och (b) den skulle förorena `sameUrlState`-jämförelser och own-roundtrip-detektorn. **Arkitektur-bindning:** `commit` är en **transient, fire-and-forget signal-param**, inte en del av `JobbUrlState`. Två rena vägar:

1. **Live-`router.replace` utelämnar `commit`; commit-punkterna kör en `router.push` med `commit=1` adderat OVANPÅ `buildJobbHref(next)`**, och eftersom det är en `replace`-då-`push`-asymmetri (som redan finns: E2h VAL 2, hero=replace/toolbar=push) landar den committade URL:en i historiken med flaggan. Risken: flaggan ligger kvar i adressfältet. Mitigering: efter capture är den harmlös vid reload (capture är idempotent via Bump — `FilterHashCalculator` + UNIQUE-upsert, Capturer rad 56–61 bumpar bara LastViewedAt). Men ren delning av en `?...&commit=1`-länk skulle re-capture:a hos *mottagaren* (deras egen historik) — benignt men oönskat.
2. **Renare:** `commit` exkluderas från `JobbUrlState`/`sameUrlState`/`buildJobbHref` helt, och adderas endast som query-string-suffix på den faktiska `router.push`-strängen vid commit-punkter, OCH `serializeSearchText`/sentinel-logiken ignorerar den. Eftersom `page.tsx` läser `params` direkt (rad 59) och `JobbResults` får `rawParams` — `commit` läses i `getJobAds`-anropet men ingår INTE i `resultsKey` (rad 107–114) eller chip-state. Mottagaren av en delad länk: vi kan låta FE *strippa* `commit` ur URL:en efter första render (en `router.replace` utan flaggan) — men det är E2i-känslig mekanik.

**Domen:** detta är **mekanik som CTO ska binda exakt** (vilken av väg 1/2), eftersom det rör own-roundtrip-detektorn och `sameUrlState` — E2i:s ömtåligaste invarianter. Min arkitektur-rekommendation: **väg 2 med `commit` strikt utanför `JobbUrlState`** (det är en signal, inte ett tillstånd — Separation of Concerns, Martin 2017 kap. 7). No-JS: statiskt hidden `commit=1` (no-JS submit är alltid commit). JS: transient suffix på commit-punkternas push, aldrig på live-replace, aldrig i state-jämförelser. **Backend ser `commit=1` identiskt i båda fallen → uniform behavior-gate.** Uniformiteten Klas efterfrågar uppnås.

*Edge att binda i implementation:* en delad/bokmärkt `?...&commit=1`-länk → mottagarens första list-query bär commit=1 → capture i mottagarens egen historik. Bedömning: benignt (egen historik, ingen cross-tenant), men security-auditor ska kvittera det explicit, och FE bör helst strippa flaggan efter mount (`router.replace` till ren URL) för att undvika det helt.

---

## Interaktion med E2i-mekaniken — invariant-checklista

Inga av de föreslagna ändringarna bryter E2i:s invarianter, FÖRUTSATT dessa bindningar (verifierade mot `jobb-hero-search.tsx` + `tokenize.ts`):

| E2i-mekanism (fil:rad) | Påverkas av E2j? | Bindning |
|---|---|---|
| `recentCommits` own-roundtrip-detektor (hero rad 145, 148–150) | ×-clear (ii) + commit-flagga | × och commit-punkter MÅSTE gå genom `commit()` (rad 187) så de registreras som egna → texten serialiseras ej om. Commit-flaggan får INTE ingå i `sameUrlState` (annars miss-matchar detektorn). |
| `lastCommitted` delta-bas (hero rad 138) | ×-clear | × nollar via delta mot `lastCommitted`, sätter ny `lastCommitted` = resultat. Samma som onFieldChange. |
| `parse(text) ⊆ state` (I1) | ×-clear (ii) | tom text ⇒ parse(∅) — trivialt ⊆. Inga claimade filter kvar i state efter (ii):s delta. Popover-dim kvar = I1 OK (state får bära mer). |
| `serializeSearchText`/`updateTextForStateChange` (tokenize rad 423, 479) | commit-flagga | `commit` får ALDRIG nå dessa — de opererar på `JobbUrlState` som inte innehåller `commit`. Bindning: `commit` utanför `JobbUrlState`. |
| `sameUrlState` (tokenize rad 535) | commit-flagga | Jämför q/occupationGroup/region/municipality — `commit` ingår ej. Korrekt by-design om `commit` hålls utanför state. |
| extern-divergens-synk (hero rad 164–183) | toolbar-"Rensa", recent-nav | Oförändrad. Toolbar-clear/recent-nav är fortsatt externa → text synkas. Commit-flaggan rör inte denna väg (toolbar pushar utan commit=1 — eller MED, se nedan). |

**Öppen mekanik-fråga till CTO:** ska toolbarns egna commits (`removeChip`, `clearAllFilters`, `onSortChange` — jobb-results-toolbar.tsx rad 114–141, alla `router.push`) bära `commit=1`? Argument FÖR: att ta bort ett filter och se färre träffar ÄR en avsiktlig sökning användaren kan vilja återfinna. Argument MOT: filter-justering är inte "en ny sökning" i Platsbankens mentala modell. **Detta är ett PRODUKTVAL med GDPR-vikt** (fler commit-punkter = mer capture). Rekommendation: toolbar-commits bär `commit=1` (de är avsiktliga, diskreta handlingar — till skillnad från live-typing), MEN Klas bekräftar eftersom det dimensionerar insamlingsvolymen.

---

## Sammanställning: arkitektur-bestämt vs PRODUKTVAL

**Arkitektur-bestämt (CTO verkställer, inget Klas-val på OM):**
- Variant A (capture varje query) avvisas — empiriskt + GDPR. Trigger MÅSTE bli commit-baserad.
- Native × MÅSTE suppress:as + ersättas av kontrollerad knapp (cross-browser + interceptbarhet).
- `commit`-signalen hålls utanför `JobbUrlState`/`sameUrlState`/`serialize` (annars bryts E2i-detektorn).
- × och commit-punkter går genom `commit()`-vägen (own-roundtrip-registrering).
- security-auditor triggas (PII-insamlingsväg ändras).
- ADR 0060-amendment författas (preciserar Beslut 3 mot live-sök-premissen).

**PRODUKTVAL (Klas avgör):**
1. **Capture-trigger:** B (commit-flagga) vs A (status quo) vs D (live-dedup). Rek: **B**. *Produkt+GDPR-vikt.*
2. **Sök-knapp:** behåll vs ta bort. Rek: **behåll** (4 jobb).
3. **×-semantik:** (i) text-only / (ii) text + claimade filter / (iii) ingen ×. Rek: **(ii)**.
4. **Toolbar-commits bär commit=1?** Rek: **ja** (avsiktliga). *Dimensionerar insamlingsvolym.*

---

## (a) ADR 0060-amendment krävs?
**JA — amendment, ingen ny ADR.** Beslut 3:s substans (post-handler-behavior, markör, best-effort, Variant A) består. Amendmentet (i) konstaterar att E2i:s live-sök rev premissen "en query = en intention"; (ii) preciserar att commit-flaggan ≠ det avvisade Variant-B (separat command) — med de fyra grunderna avförda; (iii) uppdaterar Mekanik-not 2 (commit-guard utöver default-browse-guard); (iv) noterar GDPR-data-minimerings-förstärkningen. **Klas-GO på amendment-substansen rekommenderas** (omtolkning av en Accepted-ADR:s avvisade variant är en strategisk transition).

## (b) security-auditor måste triggas?
**JA.** PII-insamlingsvägen (när söktermer persisteras) ändras. Auditorn verifierar: live-replace fångar ej längre; commit-flaggan kan inte forgeras till skadlig capture (worst case benignt = egen historik); delad `?commit=1`-länk-edgen; Art. 13-disclosure fortsatt korrekt (nu mer sanningsenlig). Obligatorisk per §9.2.

## (c) Klas-STOPP produktfrågor (konkreta varianter)

1. **Capture-trigger** — **A** capture varje list-query (status quo, spam består) / **B** commit-flagga `&commit=1` endast vid Enter/Sök/förslags-val, live-replace utelämnar (rek.) / **D** live-capture med dedup-fönster. *Produkt+GDPR-vikt — B minimerar insamling.*
2. **Sök-knappen** — **behåll** (finaliserar pågående ord, commit-signal, no-JS-submit, a11y — rek.) / **ta bort** (Enter-only; kräver annan lösning för de fyra jobben).
3. **× i sökfältet** — **(i)** rensar endast texten (filter kvar — dagens förvirring) / **(ii)** rensar texten + de filter texten gjorde anspråk på, popover-val kvar (rek.) / **(iii)** ingen ×-knapp.
4. **Toolbar-handlingar (ta bort chip / Rensa / byt sort)** — **bär commit=1** (sparas som sökning — rek.) / **bär inte** (filter-justering ≠ ny sökning). *Dimensionerar insamlingsvolym.*

---

## Referenser

- ADR 0060 (RecentJobSearches auto-capture) Beslut 3 + Mekanik-not 1/2/5/6 · ADR 0067 Beslut 5 + E2i impl-notat · ADR 0042 Beslut B/D/E · ADR 0045 (perf-hygien) · ADR 0065 (PR-flöde/amendment)
- E2i-domarna: `docs/reviews/2026-06-11-sok-paritet-e2i-architect.md` + `-e2i-cto.md` + addendum 2026-06-12 (I1, C′, recentCommits, delta-vägen)
- Eric Evans, *DDD* (2003), "Aggregates" (invarianter i modellen) · Robert C. Martin, *Clean Architecture* (2017) kap. 7 (SRP/SoC), *Clean Code* (2008, least astonishment) · Hunt/Thomas, *Pragmatic Programmer* (1999) kap. 6 (Programming by Coincidence) · Kent Beck (YAGNI) · GOV.UK / Algolia / Safari search-input clear-mönster (× är standard-affordance)
- GDPR Art. 5(1)(c) (data-minimering), Art. 6(1)(f) (berättigat intresse), Art. 13 (informationsskyldighet)
- CLAUDE.md §2.2, §2.4, §5.2, §9.2, §9.6 · memory `feedback_adr_mechanism_vs_env_phase_triage`, `project_platsbanken_parity_baseline`

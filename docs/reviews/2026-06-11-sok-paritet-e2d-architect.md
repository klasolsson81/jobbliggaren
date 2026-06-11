# dotnet-architect — Fas E2d typeahead-chip-komponist (arkitekturdom)

**Datum:** 2026-06-11
**Agent:** dotnet-architect (INLINE-dom; senior-cto-advisor fattar multi-approach-besluten efter denna)
**Scope:** ADR 0067 Beslut 5b ("tabba-klart → chip = FE-state") + Fas E2-uppdelningens E2d. "Volvo Göteborg Heltid"-visionen.
**Status:** Design-dom. Inga kod-ändringar (read-only).
**Lager-fokus:** RSC↔client-ö-gräns (Next.js 16), URL-som-SPOT, ACL (Evans kap. 14), SRP-på-komponent-nivå (Martin kap. 7). Backend-touch endast om F2 Occupation-utökning väljs.

---

## Sammanfattning

E2d är **till 90% en FE-ö-komposition** ovanpå redan-Accepted kontrakt (Beslut 5a suggest-union levererad i E1b; Beslut 5b chip=FE-state; D2 residual-parser levererad). Den tunga arkitektur-risken är **inte** ny kod utan **ö-topologin**: en live typeahead-ö måste samexistera med (a) GET-formens no-JS-fallback och (b) den befintliga `JobbHeroFilters`-ön, med URL som enda delade sanning (E2g-principen). Fel topologi-val ger antingen dubbel sanning (E2g-buggen återinförd) eller en monolit-ö som bryter SRP.

Tre genuina multi-approach-val överlämnas till CTO: **F1** (ö-topologi: separata öar vs sammanslagen), **F2** (kind→dimension-mappning för OccupationField + Occupation), **F3** (auto-chip-vid-submit = mekanik-konkretisering vs produktval → Klas-STOPP-fråga). F4/F5/F6 är design-domar med entydig riktning (jag dömer dem, CTO behöver bara bekräfta).

Inga Clean Arch-lagerbrott i den primära FE-vägen. Den ENDA vägen som rör backend-lager är F2 Occupation-korpus-utökningen — den triggar security-auditor + test-writer och bör enligt §9.6 vägas som potentiell egen touch.

---

## On-disk-grund (verifierat, bekräftar prompt-fakta)

- `jobb/page.tsx` rad ~161-215: hero-sök = rent server-renderat `<form action="/jobb" method="get">` med `<input name="q">` + hidden inputs (occupationGroup[]/region[]/municipality[]/sortBy/pageSize). `JobAdTypeahead` är INTE wirad här. Bekräftat.
- `JobbHeroFilters` (jobb-hero-filters.tsx): client-ö, `useOptimistic(base=props)` (E2g — URL ENDA sanning), `commit()` → `setOptimisticSelection` + `router.push(buildJobbHref(...))` i SAMMA `startTransition`. Äger Ort/Yrke-pills + popovers + facet-counts + total-count-store-prenumeration. Bekräftat.
- `JobAdTypeahead` (job-ad-typeahead.tsx): combobox mot `/api/jobb/suggest`, konsumerar `SuggestionDto[]`, anropar bara `onSelect(item.label: string)`. `kind`/`conceptId` parsas (zod) men kastas bort i `choose()`. Ej wirad. Bekräftat.
- `SuggestionDto = {kind: SuggestionKind, conceptId: string|null, label}`; `SuggestionKind` = Title|Region|Municipality|OccupationField|OccupationGroup (backend-enum, int-på-wire). Occupation UTESLUTS ur korpusen (`TaxonomyReadModel.LoadAsync` rad ~168-174 `Where(... or OccupationGroup)` — Occupation filtreras bort; `MapKind` kastar fail-fast om Occupation når dit). Bekräftat.
- `buildJobbHref` (search-params.ts): URL-dimensioner = occupationGroup[]/region[]/municipality[] + q/sortBy/pageSize. OccupationField är INGEN URL-dimension. Bekräftat.
- `JobAdFilterCriteria` (backend filter-SPOT): `OccupationGroup, Municipality, Region, Q`. Occupation-name (`Ssyk`) BORTTAGET ur sök-identiteten (C2). occupation-name = recall via q-FTS-synonym. Bekräftat — detta är hård gräns för F2.
- `taxonomy.ts` (FE-ACL): `regions[].municipalities[]`, `occupationFields[].occupationGroups[]`. `occupations` strippas medvetet (zod). OccupationField bär sina barn-OccupationGroups on-FE redan. Bekräftat — kritiskt för F2.
- Toolbar (`jobb-results-toolbar.tsx`): redan kind-agnostisk reverse-lookup conceptId→label/dimension-ikon (MapPin/Briefcase), chip-× via `buildJobbHref`. Etablerat prejudikat för chip-rendering ur URL.

---

## F1 — Island-arkitektur (VARIANT A/B/C → CTO väljer)

**Problem:** En live typeahead som auto-chippar förslag måste skriva samma tre URL-dimensioner (occupationGroup/region/municipality) + q som `JobbHeroFilters` redan skriver. Två client-ytor som skriver samma URL utan delad sanning = exakt E2g-buggen (dubbel sanning, osynkad state). E2g löste detta för pills↔toolbar via `useOptimistic(base=props)` + total-count-store (useSyncExternalStore) eftersom öarna saknar gemensam client-förälder i streaming-arkitekturen.

### Variant A — Separata öar, URL som enda delad sanning (typeahead-ön blir tredje URL-skrivaren)
Typeahead blir en egen client-ö (`JobbHeroSearch`) som ersätter `<input name="q">`. Den läser samma props (occupationGroup/region/municipality/q/sortBy/pageSize) från RSC, och `commit()`:ar via `buildJobbHref` precis som `JobbHeroFilters` och toolbaren. GET-formen bevaras som progressive-enhancement-fallback (ön renderar `<form>` + `<input>` server-side-identiskt; client-JS upgrade:ar med suggest+auto-chip).

- **För:** SRP bevaras (Martin kap. 7) — sökfält-ö, filter-ö, toolbar-ö har var sin change-reason. Följer E2g-mönstret exakt (tre öar, en URL-sanning, ingen delad client-förälder). buildJobbHref är redan SPOT för URL-skrivning — en tredje konsument är symmetriskt korrekt (samma "param-bevarande-disciplin" som F3 B-FIX/E2b). Minsta blast-radius. Progressive enhancement bevaras gratis.
- **Mot:** Tre öar som var och en måste bära HELA URL-staten som props (q + tre dimensioner + sortBy + pageSize) för att inte radera varandras params. Det finns redan idag (JobbHeroFilters bär q "så filter-klick inte raderar q"); en tredje ö adderar en till plats där prop-listan måste hållas i synk. Risk: om en framtida dimension läggs till måste tre öar uppdateras.
- **State-derivering:** chips i sökfältet deriveras ur samma props (URL) som pills — INGEN egen useState-kopia (E2g-lärdom, F4 nedan).

### Variant B — Sammanslagen ö (`JobbHeroSearch` ⊃ typeahead + pills + popovers)
Typeahead-ön och `JobbHeroFilters` slås ihop till EN client-ö som äger sökfält, chips, pills OCH popovers. En `commit(next: FilterSelection & {q})` skriver allt.

- **För:** EN URL-skrivare för hela hero-sökytan → ingen prop-synk mellan typeahead och pills. Auto-chip + pill-toggle delar exakt samma `commit`-väg och samma `useOptimistic`-bas → en enda sanning, garanterat konsistent. Total-count-store-prenumerationen sitter redan i JobbHeroFilters → samlokaliserad.
- **Mot:** **Bryter SRP på komponent-nivå (Martin kap. 7, SWE@Google kap. 9).** Sökfält-input (med suggest/debounce/AbortController/a11y-combobox) och kaskad-popovers (med dual-axis/facet-counts/per-län-normalisering) har OLIKA change-reasons. En sammanslagen ö blir en ~600-raders client-komponent som är svår att granska och testa isolerat. Större client-bundle laddas även när användaren bara vill skriva fritext. Strider mot den medvetna streaming-ö-splitten ("öarna saknar gemensam client-förälder pga streaming-arkitekturen" — E2c-notatet, ett MEDVETET designval).

### Variant C — Typeahead-ön komponerar `JobbHeroFilters` som child (delad commit via lyft state)
Typeahead-ön blir förälder, renderar `JobbHeroFilters` som barn, och lyfter `commit` till sig själv (prop-drilling av commit-callback ner).

- **För:** En commit-väg utan full sammanslagning av render-träden.
- **Mot:** Bryter den medvetna ö-splitten (JobbHeroFilters skulle inte längre vara en självständig ö utan ett barn → den remountas/omrenderas med föräldern, vilket kan återinföra popover-stängning-vid-toggle-buggen E2g varnade för). Prop-drilling av commit + hela URL-staten = tightare koppling än A utan B:s konsistens-vinst. Sämsta av båda.

### Min arkitekt-rekommendation till CTO (CTO beslutar)
**Variant A.** Den konformerar mot tre etablerade, Accepted prejudikat: (1) E2g:s "URL = enda sanning, useOptimistic-bas, ingen client-förälder", (2) buildJobbHref som URL-skriv-SPOT med symmetriskt param-bevarande, (3) den medvetna streaming-ö-splitten. SRP per ö bevaras. GET-form-fallbacken bevaras gratis (ön server-renderar `<form>`-identiskt). Variant B:s enda reella vinst (en commit-väg) löses i A genom att de tre öarna delar `buildJobbHref` — vilket de redan gör. B:s SRP-brott och bundle-kostnad är inte motiverade.

**GET-form-fallback (entydig, ej variant):** BEVARA. `JobbHeroSearch`-ön ska server-rendera exakt samma `<form action="/jobb" method="get">` + `<input name="q">` + hidden inputs som idag, och progressivt upgrade:a med suggest + auto-chip på klienten. Civic-utility/§5.2: sökytan måste fungera utan JS (GOV.UK-doktrin). Detta är ett krav, inte ett val.

**RSC↔client-serialiserings-grind (AGENTS.md):** ön tar serialiserbara props (taxonomy-träd, conceptId string[], q/sortBy/pageSize) — INGA funktioner från RSC. `pnpm build` är obligatorisk pre-push (RSC-payload-generering fångar icke-serialiserbara props som vitest/tsc missar).

---

## F2 — kind→dimension-mappning (två öppna sub-val → CTO)

Givna (entydiga): Region→`region`, Municipality→`municipality`, OccupationGroup→`occupationGroup`, Title→`q` (fritext, ej dimension). Dessa följer direkt av URL-dimensionerna + JobAdFilterCriteria. Ingen variant.

### F2a — OccupationField (suggest-bart, INGEN URL-dimension)

OccupationField finns i korpusen (`TaxonomyReadModel.LoadAsync` emitterar det) men är ingen filter-dimension. Ett OccupationField-förslag MÅSTE därför mappas till något, annars är det en **död förslag-rad** (chip utan mål = återvändsgränd — exakt samma anti-pattern som Beslut 5a/VAL 4 avvisade Occupation med).

**Avgörande on-disk-fakta:** FE-ACL:n bär REDAN OccupationFields barn: `taxonomy.occupationFields[].occupationGroups[]`. Mappningen kräver INGEN backend-touch och INGEN extern hop — den är en ren in-memory lookup i det redan-laddade trädet (samma träd `JobbHeroFilters` redan itererar för popover-grupperna).

- **Variant A — Materialisera till alla barn-OccupationGroups ("hela yrkesområdet"-semantik).** Ett OccupationField-chip expanderar till `occupationGroup += alla dess occupationGroups[].conceptId`. Semantiskt = "Hela {yrkesområdet}" — exakt analogt med Ort-pickerns "Hela länet" (men där togglar EN region-id pga 414-skydd; här finns ingen field-URL-dimension så materialisering är enda vägen).
  - **För:** Bevarar OccupationField som meningsfullt förslag (recall-bevarande — användaren som skriver "Data" får hela Data/IT). Återanvänder OR-inom-dimension-semantiken (ADR 0042 B). Konsistent med toolbar/recent-search "Data/IT"-label-deriveringen (E2g `DeriveLabel`: mängd-likhet "alla grupper i ETT yrkesområde" → områdets namn). **Cap-konsekvens:** ett yrkesområde har ≤~50 yrkesgrupper; `MaxConceptIds=400` håller med marginal även vid flera field-chips. Bounded, säkert.
  - **Mot:** Chip representerar N ids men visas som ETT chip. Om användaren sedan av-markerar EN yrkesgrupp i popovern "spricker" abstraktionen (samma minus-en-materialisering som E2f redan löste för Ort — etablerat mönster, ej ny risk). URL blir längre (~50 ids) — men det är samma som "Välj alla yrkesgrupper" redan producerar idag.
- **Variant B — Släng OccupationField ur chip-mappningen (ignorera förslaget vid val).** Behåll det i listan som icke-valbart, eller filtrera bort vid `onSelect`.
  - **Mot:** Då är det en död rad i korpusen → förvirrande UX (användaren ser "Data/IT" men inget händer). Bryter civic-utility-pålitlighet. Om man INTE vill ha det valbart bör det utelämnas ur korpusen helt (backend-touch) — men det vore en regression av Beslut 5a som medvetet inkluderade OccupationField.
- **Variant C — Mappa OccupationField → q (fritext).** Skicka områdesnamnet som FTS-term.
  - **Mot:** Semantiskt fel — OccupationField ÄR strukturerad taxonomi med exakta barn-ids; att degradera till fritext-FTS kastar bort precisionen och ger sämre recall än A. Bryter "disambiguering vid input"-andan (D2).

**Min rekommendation:** **Variant A** (materialisera till barn-OccupationGroups). Det är den enda varianten som gör förslaget meningsfullt, är bounded (cap-säkert), kräver ingen backend-touch, och återanvänder två etablerade mönster (Ort "Hela länet"-materialisering + E2g `DeriveLabel`-mängdlikhet för chip-labeln "Data/IT"). CTO bekräftar.

### F2b — Occupation (occupation-name, EJ i korpus idag)

Detta är det enda valet i E2d som potentiellt rör **backend-lager**.

- **Variant A — Förbli ute (status quo).** Occupation når som recall via q-FTS-synonym (`IOccupationSynonymExpander`). Användaren som skriver "systemutvecklare" får ingen strukturerad chip men FTS+synonym-grenen fångar annonserna.
  - **För:** Noll backend-touch. Konformerar exakt mot Beslut 5a/VAL 4 (Variant A): "occupation-name ingår INTE — saknar filter-dimension; nås som recall via q-FTS-synonym." `SuggestionKind` behöver ingen `Occupation`-medlem. Den medvetna designen står. Kraschsäkerheten (Beslut 5 residual-FTS) bär detta fall by design.
  - **Mot:** "Volvo **systemutvecklare** Göteborg"-visionen ger inget yrkes-CHIP för systemutvecklare — bara en fritext-q. Men det är medvetet: occupation-name har ingen URL-dimension (JobAdFilterCriteria har inget Ssyk-fält efter C2), så ett "chip" vore ett chip utan mål.
- **Variant B — Utöka korpusen med Occupation, mappa till förälder-OccupationGroup via broader-relationen.** Lägg till Occupation i `suggestable`-bygget + ny `SuggestionKind.Occupation`; vid val mappa occupation-name → parent ssyk-level-4 (samma `broader` single-parent som C2 reverse-lookup, live-verifierad 2179/2179 → exakt 1 parent) → `occupationGroup`-dimensionen.
  - **För:** "systemutvecklare" → chip "Systemutvecklare m.fl." (yrkesgruppen). Mer Platsbanken-likt (Platsbanken suggest:ar occupation-names). Återanvänder den verifierade broader-determinismen.
  - **Mot (TUNGT):** **Backend-touch** — `suggestable`-bygget i `TaxonomyReadModel.LoadAsync` (lägg till Occupation + parent-lookup), ny `SuggestionKind`-medlem (+ FE `SUGGESTION_KIND_ORDER`-synk, int-på-wire-ordinal), parent-mappnings-data on-FE eller på wire. Triggar **security-auditor** (ny suggest-källa, ~2179 nya korpus-rader → DoS/prefix-scan-perf) + **test-writer** (mappnings-determinism). **Korpus-storlek ×~6** (400→2579 suggestable) → prefix-scan-perf (in-memory `StartsWith` rad ~65, OrdinalIgnoreCase) mot ADR 0045. Risk: occupation-name+occupationGroup ger DUBBLA förslag för samma yrkesgrupp ("Systemutvecklare" occupation-name + "Mjukvaru- och systemutvecklare m.fl." occupationGroup) → förvirrande/redundant lista. **Fas-fråga (§9.6):** Beslut 5a Accepted-texten utesluter Occupation EXPLICIT ("ingår INTE"). Att inkludera det nu är inte "mekanik-konkretisering" — det är en **omtolkning av ett Accepted beslut** → kräver ADR-amendment + Klas-GO, inte CC-omdöme (`feedback_adr_mechanism_vs_env_phase_triage`).

**Min rekommendation:** **Variant A (förbli ute).** Tre skäl: (1) Beslut 5a/VAL 4 dömde detta EXPLICIT — att ändra är ADR-amendment-territorium, inte E2d-mekanik. (2) Backend-touch + security-auditor + test-writer + ×6 korpus-perf-risk är oproportionerligt mot vinsten (en fritext-q fångar redan annonserna via synonym). (3) Kraschsäkerheten bär fallet by design. **OM Klas vill ha occupation-name-chips ÄR det ett separat, eget-touch-beslut** (§9.6 — annan mekanik, ADR-amendment) som CTO bör flagga för Klas-STOPP, inte folda in i E2d tyst. Jag designar E2d **Occupation-uteslutande** och låter visionens "Volvo systemutvecklare Göteborg" lösa systemutvecklare-delen via residual-q (recall-bevarande) — vilket är exakt vad Beslut 5 lovar.

### F2c — EmploymentType/WorktimeExtent ("Heltid") — GATED, designa men WIRA INTE

NULL-data tills re-ingest Klass 2 (B2, KÖRS ALDRIG autonomt). Falsk-klar förbjuden (samma gate som D1/D2/FacetDimension).

**Design utan döda kodvägar (entydig, ej variant):**
- `SuggestionKind` har INGEN EmploymentType/WorktimeExtent-medlem idag (verifierat) → de KAN inte komma ur suggest-korpusen → typeahead-ön kan aldrig auto-chippa dem. Detta är redan korrekt — ingen död kodväg existerar.
- E2d ska INTE lägga till en tredje Filter-pill ("Omfattning"/"Anställningsform") och INTE en EmploymentType-gren i `buildJobbHref`/`JobAdFilterCriteria` (NULL-data → tyst-noll-filter = falsk klar).
- **"Re-ingest-redo design" = dokumentera mappnings-INTENTIONEN, inte koden:** när Klass 2-data finns (B2) blir mappningen `SuggestionKind.EmploymentType → employmentType`-dimension (analogt med OccupationGroup→occupationGroup). Den följer sin query-wiring-touch (samma sekvensering som C2-CTO (a): "VO-fält följer sin query-wiring-touch, post re-ingest"). **Ingen kod i E2d.** Att designa "re-ingest-redo" betyder att F2:s kind→dimension-mappningstabell ska ha en kommentar/struktur som gör tillägget av EmploymentType till en ren additiv rad — INTE att skriva den raden nu.
- **Konkret:** kind→dimension-mappningen i typeahead-ön ska vara en `switch (kind)` (eller mappnings-objekt) som idag har Title/Region/Municipality/OccupationField/OccupationGroup. När korpusen + dimensionen finns adderas EmploymentType som en case. Switchen ska vara exhaustiv mot `SuggestionKind` (TS exhaustiveness) så att en framtida ny kind-medlem ger COMPILE-fel om mappningen glöms — det är "re-ingest-redo" gjort rätt (kompilatorn tvingar fram tillägget, ingen tyst död väg).

---

## F3 — auto-chip-vid-submit ("volvo göteborg heltid" + Enter, utan tabb)

**Backend-token-gissning är AVVISAD (D2 VAL 1, Variant A+A):** parsern extraherar INGA dimensioner; "disambiguering vid input snarare än via gissande backend". Detta är hårt låst.

**Frågan:** FE-side auto-chip vid EXAKT taxonomi-label-match vid submit (case-insensitivt), annars fritext→q. Är detta mekanik-konkretisering inom Accepted ADR 0067, eller produktval → Klas-STOPP?

### Arkitekt-analys

Beslut 5b säger: "Ett tabbat förslag blir ett strukturerat filter-chip (FE känner {kind, conceptId}) → ingen parsning." Visionen (Beslut 5 brödtext) säger: användaren skriver "systemu" → typeahead → **tabbar klart** → chip. Modellen är **explicit tabb-komplettering** — användarstyrd disambiguering vid input.

Auto-chip-vid-submit-UTAN-tabb är en **annan interaktionsmodell**: användaren skriver "göteborg heltid" och trycker Enter UTAN att ha valt ur listan, och FE försöker matcha varje token mot taxonomi-labels. Detta är:

1. **En FE-side token-parser.** Även om matchningen är "exakt label, case-insensitivt" (inte fuzzy backend-gissning), är mekaniken **att splitta submit-strängen i tokens och matcha varje mot taxonomi** — det är en parser. D2 VAL 1 avvisade token-lyft EXPLICIT ("Variant B: 1:1 token-lyft, dubblerar FE-chip-ansvar + tyst dimensions-AND mot recall-andan — avvisat"). Att flytta den parsern från backend till FE ändrar lagret men inte att det ÄR token-gissning. "göteborg" matchar exakt mot kommunen Göteborg — men "heltid" matchar (när Klass 2 finns) mot WorktimeExtent, och flertoken-matchning mot flera dimensioner samtidigt ÄR den tysta dimensions-AND-komposition som recall-andan varnar för.
2. **En semantik-ändring av submit.** Idag: Enter på fritext = q-FTS (recall-bevarande). Med auto-chip: Enter kan tyst konvertera delar av q till hårda dimensions-AND-filter. "göteborg" som exakt kommun-match blir `municipality=`-AND i stället för q-FTS-OR. Det KAN minska recall (en annons som nämner Göteborg i texten men har municipality=null faller bort). Det är precis den "hård AND vs recall-bevarande"-gränsen Beslut 5/D2 vaktar.

**Min dom:** Detta är ett **PRODUKTVAL som kräver Klas-STOPP**, inte mekanik-konkretisering. Skäl: (a) det inför en FE-token-parser som D2 VAL 1 avvisade för backend — att lagret är FE ändrar inte att besluts-substansen (token→dimension utan explicit användarval) är den avvisade vägen; (b) det ändrar recall-semantiken för submit (q-FTS-OR → potentiell dimensions-AND) vilket är exakt den Klas-STOPP-gated kombinationssemantiken (D2 VAL 4: "chip/residual-kombinationen presenteras för Klas-GO"); (c) `feedback_adr_mechanism_vs_env_phase_triage` — agent-flaggad semantik-avvikelse mot Accepted-ADR-mekanik triggar CTO-triage/Klas, ej CC-omdöme.

### Varianter Klas ska välja mellan (om CTO eskalerar)

- **Variant A — Endast explicit tabb/klick chippar (ingen auto-chip-vid-submit).** "volvo göteborg heltid" + Enter utan tabb → HELA strängen blir q-FTS (residual-parser, D2). Användaren MÅSTE tabba/klicka ett förslag för att få ett chip. Konformerar 100% med Beslut 5b ("tabbat förslag → chip") + D2 ("disambiguering vid input"). Kraschsäkert by design. **Min arkitekt-preferens** — minst risk, ingen ny parser, ingen semantik-ändring.
- **Variant B — Auto-chip vid exakt singel-token-label-match endast (konservativ).** Endast om HELA submit-strängen (trimmad) exakt matchar EN taxonomi-label → chip; annars hela strängen → q. "göteborg" ensamt → kommun-chip; "volvo göteborg heltid" → q (ingen flertoken-splitt). Undviker flertoken-AND-risken men inför ändå en match-vid-submit-mekanik (mildare).
- **Variant C — Full flertoken auto-chip (visionens "Volvo Göteborg Heltid" → tre chips + Volvo-q).** Splitta, matcha varje token, resten → q. Maximal "magisk" UX men maximal recall/semantik-risk + FE-parser. Detta är vad D2 VAL 1 avvisade på backend-sidan.

CTO bör presentera A/B/C för Klas. Jag avråder C på arkitektur-grund (recall-semantik + parser D2 avvisade). A är den rena Accepted-vägen; B är en mellanväg om Klas vill ha viss magi.

**Not:** Visionen "Volvo Göteborg Heltid" är fullt uppnåelig med Variant A via explicit tabb-komplettering — användaren skriver "göte" → tabbar Göteborg-chip → "heltid" → (när B2 finns) tabbar Heltid-chip → "volvo" blir residual-q. Visionen kräver INTE auto-chip-vid-submit; den kräver smidig tabb-komplettering (F5 a11y).

---

## F4 — chip-state-derivering (entydig dom, CTO bekräftar)

**Bekräftat:** chip-state ska deriveras ur URL+props (E2g-principen), ALDRIG egen useState-kopia. E2g-buggen (`useState`-kopia synkar ej vid externa URL-ändringar) är direkt prejudikat. Chips i sökfält-ön = `useOptimistic(base=props)` precis som pills, eller — om Variant A med separata öar — chips deriveras ur samma RSC-props (occupationGroup/region/municipality conceptId[] → reverse-lookup-label via taxonomy-trädet, exakt som toolbaren redan gör).

**Chip-rendering = återanvänd toolbar-prejudikatet:** `jobb-results-toolbar.tsx` mappar redan conceptId→label+dimension-ikon kind-agnostiskt ur URL. Sökfält-öns chips bör återanvända SAMMA reverse-lookup (DRY — Hunt/Thomas 1999). Om en delad `useSelectedChips(taxonomy, occupationGroup, region, municipality)`-hook extraheras bör den vara SPOT för "URL-dimensioner → chip-modeller" och konsumeras av både toolbar och sökfält-ö. (CTO: väg om extraktionen är in-scope E2d eller en separat DRY-touch — jag lutar åt in-scope eftersom E2d annars duplicerar logiken.)

**Dedupe (förslag redan valt):** vid auto-chip/tabb-val, kontrollera `conceptId` mot befintlig dimensions-lista FÖRE append (samma `selected.includes(conceptId)` som `toggle()` i popovern, rad 130-140). Idempotent — att tabba Göteborg två gånger ger ETT chip.

**OR-inom-dimension vid upprepade förslag samma kind:** två Region-förslag (t.ex. Stockholm + Skåne) → `region=[sthlm, skane]` = OR-inom-dimension (ADR 0042 B, redan låst). Detta är korrekt och kräver ingen ny logik — append till rätt dimensions-lista, distinct-normaliserat. Geo-unionen (E2b) hanterar region∪municipality korrekt backend-side.

---

## F5 — A11y (WAI-ARIA combobox + tab-till-chip; entydig dom, CTO bekräftar)

Nuvarande `JobAdTypeahead` har `role="combobox"`, `aria-expanded`, `aria-controls`, `aria-autocomplete="list"`, `aria-live` status, Escape-stäng. Saknas för full WAI-ARIA 1.2 combobox + chip-komponist:

1. **`aria-activedescendant` + pil-navigering i listan.** Idag finns BARA Escape (rad 147-149) — ingen ArrowDown/ArrowUp/Enter-navigering i förslagslistan. För "tabba-klart"-modellen (Beslut 5b) är keyboard-navigering i listan ett KRAV. Lägg till: ArrowDown/Up flyttar `aria-activedescendant` mellan `<li>`-options (varje option behöver `id` + `role="option"` + `aria-selected`), Enter/Tab väljer aktivt option → chip. Detta är den största a11y-luckan.
2. **Tab-till-chip-mönster.** När förslag valts → chip. Chips måste vara tangentbords-nåbara (fokuserbara med `×`-knapp, samma som toolbar-chips). WAI-ARIA: chips som en `role="list"` av `role="listitem"` med borttagnings-knapp, eller återanvänd toolbarens chip-mönster (redan a11y-granskat).
3. **Combobox-listan: `role="listbox"` på `<ul>`** (idag `aria-label="Sökförslag"` men ingen listbox-roll) + `role="option"` på varje rad (idag `<button>` i `<li>` — bör bli `role="option"` med `id` för activedescendant).
4. **Konsultera `jobbpilot-design-a11y`-skill** (WCAG 2.1 AA) vid implementation — combobox + chip-input är ett känt mönster med exakta ARIA-krav. nextjs-ui-engineer bör ladda den skillen.

Detta är design-/a11y-domar, inte arkitektur-varianter — CTO behöver bara bekräfta att de är i E2d-scope (det är de; en chip-komponist utan keyboard-nav vore inte DoD-klar per CLAUDE.md §8 punkt 6).

---

## F6 — Ärvda E2d-Minors (bekräfta scope/läge, ej design)

Alla tre rör `JobbFilterPopover`-kontraktet och är **legitim in-scope E2d-polish** (samma fas, samma komponent, ingen saknad dependency → §9.6 default = fixa in-block, EJ TD):

- **(a) selectAllLabel "Hela {länsnamn}" per-grupp.** Idag är `selectAllLabel` EN statisk prop ("Hela länet"). För per-län-text krävs att raden kan visa aktiv grupps namn. **Scope:** in-block. **Mekanik:** antingen gör `selectAllLabel` till en `(group: PopoverGroup) => string`-funktion, eller derivera i komponenten ur `activeGroup.label` när `groupAxis` finns ("Hela " + activeGroup.label). Funktions-prop är renast (SPOT, föräldern äger copy). Verifiera att Yrke ("Välj alla yrkesgrupper") fortsatt fungerar — Yrke saknar groupAxis så fallback till statisk sträng behövs. **Inte design — kontrakts-justering.**
- **(b) `dialogLabel`-prop så popoverns `aria-label` ≈ triggerns namn.** Idag `aria-label={leftTitle}` (rad 262) = "Län"/"Yrkesområde". Triggern säger "Ort"/"Yrke". A11y-mismatch (screenreader säger "Län dialog" när knappen var "Ort"). **Scope:** in-block, ren a11y-fix. Lägg `dialogLabel?: string`-prop (default `leftTitle` för bakåtkompat), `JobbHeroFilters` skickar "Ort"/"Yrke". WCAG 2.1 AA (jobbpilot-design-a11y).
- **(c) tri-state `aria-checked="mixed"` på select-all-raden vid partiellt val.** Idag `CheckRow` `aria-checked={checked}` (boolean, rad 176). Vid partiellt val (några men inte alla kommuner valda) ska select-all-raden vara `aria-checked="mixed"` (WAI-ARIA tri-state checkbox). **Scope:** in-block. **Mekanik:** `CheckRow` tar `checked: boolean | "mixed"`; select-all beräknar mixed = `rightAnySelected && !selectAllChecked` (i enaxel-fallet; dual-axis "Hela länet" är binärt så mixed gäller primärt Yrke-pickern). Verifiera mot E2f minus-en-materialiseringen (dual-axis "hela länet minus en kommun" → materialiserade ids → select-all blir tekniskt icke-mixed eftersom region-id:t är borta; ok).

Alla tre är < liten yta, samma komponent, rätt fas → in-block per §9.6. INGEN TD.

---

## Lager- och SPOT-sammanfattning (var varje ny bit bor)

| Bit | Lager/plats | Motiv |
|---|---|---|
| `JobbHeroSearch`-ö (typeahead + chips + GET-form-fallback) | FE `components/job-ads/` (ny client-ö) | F1 Variant A — separat ö, URL-sanning |
| kind→dimension-mappning (exhaustiv switch mot SuggestionKind) | FE, i `JobbHeroSearch` (eller `lib/job-ads/suggestion-mapping.ts` om delad) | F2 — ren FE-logik, TS-exhaustiveness tvingar fram framtida EmploymentType-tillägg |
| OccupationField → barn-OccupationGroups materialisering | FE, in-memory lookup i taxonomy-trädet (redan laddat) | F2a Variant A — ingen backend/extern hop |
| chip-derivering ur URL (reverse-lookup conceptId→label/dimension) | FE, helst delad `useSelectedChips`-hook (DRY med toolbar) | F4 — E2g-princip, URL = sanning |
| URL-skrivning (auto-chip + fritext → href) | FE, via befintlig `buildJobbHref` (SPOT) | F1/F4 — symmetriskt param-bevarande, ingen ny URL-builder |
| residual-q-parsning | Backend Application `ISearchQueryParser` (REDAN levererad D2) | Beslut 5c — ingen FE-parser; submit-q skickas rå, backend parsar |
| EmploymentType/WorktimeExtent | INGENSTANS i E2d (gated NULL-data) | F2c — designa intention, wira EJ |
| Occupation-korpus-utökning | INGENSTANS (Variant A) ELLER backend `TaxonomyReadModel` (Variant B → eget touch + security-auditor + test-writer) | F2b — rekommenderad A; B = ADR-amendment-territorium |

**Inga Clean Arch-lagerbrott** i primär FE-väg. Backend orört (F2b Variant A). Filter-SPOT (`JobAdFilterCriteria`) orört. URL-skriv-SPOT (`buildJobbHref`) återanvänds, ej duplicerad. Residual-parser-SPOT (D2) bär submit-fritexten — FE bygger INGEN konkurrerande parser (F3 Variant A).

## §9.6-triage (TD ja/nej)

- F2b Variant B (Occupation-korpus) — OM vald: inte TD utan **ADR-amendment + eget touch** (annan mekanik mot Accepted Beslut 5a, backend-touch, security-auditor). CTO/Klas-beslut, ej tyst.
- F2c EmploymentType — INTE TD: det är korrekt gated (B2-data-dependency, saknad funktion-dependency). Designas intentions-redo (exhaustiv switch), wiras vid B2. Ingen TD-lyftning behövs — den naturliga additiva raden tillkommer vid B2-touch.
- F6 (a)/(b)/(c) — INTE TD: in-block-polish, samma fas/komponent (§9.6 default).
- Inga andra fynd motiverar TD.

## Referenser
- Robert C. Martin, *Clean Architecture* (2017) — kap. 7 (SRP/komponentgränser, F1), kap. 22 (lager, residual-parser i Application)
- Eric Evans, *DDD* (2003) — kap. 14 (ACL — F2a/F2b mappning, taxonomy-ACL bär barn on-FE)
- Martin Fowler, *Refactoring* (2018) — kap. 3 (Speculative Generality — F2b/F3 avvisning), Flag Argument (groupAxis-prejudikatet)
- Hunt/Thomas, *Pragmatic Programmer* (1999) — DRY/SPOT (buildJobbHref, useSelectedChips, exhaustiv switch)
- SWE@Google kap. 9 — granskbar diff (F1 SRP-på-ö)
- ADR 0067 Beslut 5a/5b/5c + notat D2 (VAL 1 Variant A+A), E2b (geo-union, dual-axis), E2c (facet/total-count-store), E2f (minus-en-materialisering), E2g (useOptimistic URL-sanning)
- ADR 0042 Beslut B (OR-inom/AND-mellan, multi-värde-invarianter), ADR 0062 (FTS-hybrid OR-recall), ADR 0043 (taxonomi-ACL/snapshot)
- CLAUDE.md §2.1 (lager), §2.3 (CQRS/SPOT), §4.3/§5.2 (RSC-default, no-JS-fallback, civic-utility), §9.6 (in-block vs TD)
- jobbpilot-design-a11y-skill (WCAG 2.1 AA combobox — F5)

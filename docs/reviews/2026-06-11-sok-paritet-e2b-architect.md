# dotnet-architect — Platsbanken sök-paritet Fas E2b (Län→Kommun-kaskad + ?municipality= atomiskt)

**Datum:** 2026-06-11
**Scope:** Arkitekt-dom inför E2b — FE-only-leverans per ADR 0067 Beslut 7 rad 104/109 + ADR 0043-amendment 2026-06-08. Variant-analys till senior-cto-advisor (decision-maker); ingen CC-/architect-rekommendation där valet är genuint multi-approach.
**Discovery-verifiering:** huvud-CC:s discovery 2026-06-11 är läst mot on-disk och stämmer på samtliga punkter jag kontrollerat (ApplyCriteria-grenarna, `TaxonomyTreeDto.Municipalities`, `RecentJobSearchDto`-fältordningen, zod-strip i `taxonomy.ts`/`recent-searches.ts`, two-column-mekaniken i `jobb-filter-popover.tsx`).

---

## Sammanfattning

Behöver CTO-triage — 1 kritiskt arkitektur-fynd som styr variantvalet i fråga 1: **backend-AND mellan `Region` och `Municipality` är inte Platsbankens semantik.** Web-verifierat 2026-06-11 mot JobTechs egen JobSearch-dokumentation: när både region- och kommun-filter anges kombineras de **inkluderande** ("the most local one will be promoted" — Haparanda-kommun + Norrbottens-län ger Haparanda-träffar FÖRST i ett unionerat resultat, inte ett AND-snitt). JobbPilots on-disk `ApplyCriteria` (`src/JobbPilot.Infrastructure/JobAds/JobAdSearchQuery.cs:177-187`) ger i samma kombination noll träffar. Det betyder att ingen ren FE-invariant kan ge 100% paritet (TD-100) för alla legitima kombinationer — analysen per variant nedan. Frågorna 2–5 är entydiga och besvaras med dom direkt.

Källa (§9.5): [GettingStartedJobSearchEN.md, JobTech/Arbetsförmedlingen GitLab](https://gitlab.com/arbetsformedlingen/job-ads/jobsearch-apis/-/blob/main/docs/GettingStartedJobSearchEN.md) (raw-läst 2026-06-11): *"If I make a query which includes 2 different geographical filters the most local one will be promoted"* + Haparanda/Norrbotten-exemplet.

---

## Fråga 1 — Ort-picker-semantik (KÄRNFRÅGAN, CTO avgör)

### Bärande data-deduktion (ersätter SQL-körningen — Bash ligger utanför architect-agentens tool-access)

Ur den DB-verifierade discoveryn 2026-06-11: 42 795 aktiva annonser, 41 502 har `municipality_concept_id` → 1 293 saknar kommun. Exakt 1 293 saknar **både** region och kommun. Alltså: **annonser med region satt men kommun NULL = 0 i nuvarande korpus** (42 795 − 41 502 = 1 293 = mängden utan båda). Viktigt förbehåll: detta är en egenskap hos nuvarande snapshot, **inte en schema-invariant** — STORED-kolumnerna är oberoende jsonb-extraktioner och JobTech-payloaden tillåter län-annons utan kommun. En framtida snapshot-cron kan introducera region-only-annonser utan att något larmar.

### Variant A — per-län ömsesidig uteslutning ("hela länet" = region-chip, kommun-val ersätter region-valet)

- **Styrkor:** löser intra-län-AND-fällan helt FE-side; en chip per helt län; recall-säker mot framtida region-only-annonser; URL-state speglar användarens mentala modell.
- **Kritiskt korrekthetshål:** invarianten är per län och stänger **inte** cross-län-mixen. `?region=<Stockholms-län>&municipality=<Mölndal>` (helt län X + kommun i län Y — en legitim, vardaglig Platsbanken-kombination) passerar invarianten oförändrad och ger AND-∅ backend-side. Att täppa hålet med en global invariant ("region-lista och kommun-lista aldrig samtidigt icke-tomma") förbjuder kombinationen i UI:t = paritetsbrott mot TD-100:s 100%-krav.
- **Arkitekt-dom:** Variant A som beskriven är **inte korrekthets-komplett** och kan inte levereras ensam utan att antingen (a) hålet accepteras dokumenterat, eller (b) kombineras med B′/D nedan.

### Variant B — municipality-only ("hela länet" materialiseras till länets kommun-ids)

- **Styrkor:** enklast state-modell; all geografi blir OR-inom-en-dimension → korrekt under befintlig backend-semantik utan invariant-logik.
- **Chips-invändningen är lösbar presentationellt:** FE äger taxonomy-trädet → om selection ⊇ alla kommuner i län X renderas EN chip "Stockholms län" (grupperings-logik i toolbaren), URL förblir kommun-vis. 26 chips-problemet är alltså inte avgörande.
- **Recall-risk:** region-only-annonser missas. Noll idag (deduktionen ovan) men ogaranterat → latent falsk-klar mot TD-100, exakt den riskklass Klas-prompten flaggar.
- **URL-längd-corner (konkret):** "Välj alla län" materialiserat = 290 ids ≈ 290 × ~25 tecken ≈ 7,3 KB query-string. Kestrels `MaxRequestLineSize`-default är 8 192 bytes → 414-risk på den server-side RSC-fetchen mot backend. Mitigeras delvis av E2a-prejudikatet (architect-dom 5: "markera allt = tom lista backend-side"), men 2–3 stora län (VG 49 + Sthlm 26 + Skåne 33) ger ~2,7 KB och Norrlands-län-kombos kan växa. En semantik vars korrekthet beror på att användaren inte väljer för mycket är skör.
- **Region-param blir död FE-yta** — dock inte bruten: recent searches med `regionList` (befintliga rader) fortsätter fungera mot backend-grenen.

### Variant B′ — hybrid A/B (lazy materialisering; FE-only, korrekthets-komplett)

"Hela länet" = region-id **så länge kommun-listan är tom** (ren A-upplevelse). Första kommun-valet, oavsett län, materialiserar alla valda regioner till deras kommun-ids → URL blir kommun-homogen → OR-inom-dimension → korrekt under backend-AND även cross-län.

- **Styrkor:** enda FE-only-varianten som är korrekt för alla kombinationer; vanligaste fallen (bara län, eller bara kommuner) får ren A- resp. B-form.
- **Svagheter:** state-komplexitet (URL ≠ mental modell i mixed mode); samma latenta region-only-recall-lucka som B, men **endast** i mixed mode; URL-längd-cornern ärvs i mixed mode; och principiellt: FE-invariant-logik som kompenserar för en backend-semantik som inte är paritets-korrekt är **ACL upp-och-ner** — presentation-lagret skyddar query-lagret i stället för tvärtom (Evans 2003 kap. 14; jfr ADR 0043:s ACL-riktning).

### Variant C — fritt valbara axlar utan invariant

**Avvisas med arkitektur-motivering** (per uppdraget): användaren kan skapa `region=Stockholm + municipality=Mölndal` → "Inga träffar" trots att annonserna bevisligen finns. Systemet ljuger tyst om korpusen — för en jobbsökare är en tyst missad annons den värsta felklassen (CLAUDE.md §1: seriöst och pålitligt; ADR 0067:s civic-utility-linje "pålitlighet > täckning"). Dessutom vilar C på en felläsning av ADR 0042 Beslut B: AND-mellan-dimensioner förutsätter **ortogonala** dimensioner (yrke × ort). Län ⊃ kommun är inte ortogonala — de är samma dimension i två granulariteter. C kodifierar geometriskt omöjliga snitt som giltigt UI-state.

### Variant D — backend geografi-OR (en ort-dimension, två granulariteter) — flaggas trots FE-only-ramen

`ApplyCriteria`: när **båda** listorna är icke-tomma → en kombinerad gren:

    if (criteria.Region.Count > 0 && criteria.Municipality.Count > 0)
        source = source.Where(j =>
            regionValues.Contains(EF.Property<string?>(j, "RegionConceptId"))
            || municipalityValues.Contains(EF.Property<string?>(j, "MunicipalityConceptId")));
    // annars: befintliga separata grenar oförändrade

~10 rader + Testcontainers-tester. **URL-/API-kontraktet är orört** — Klas-direktivets "Region FÖRBLIR egen ?region=-param" handlar om param-ytan och bevaras exakt; kommun förblir additiv dimension i kontraktet. ADR 0042 Beslut B bryts inte i anden (ort = EN dimension; AND mot yrke/q består), men ordalydelsen "AND mellan dimensioner" berörs → per memory-regeln `feedback_adr_mechanism_vs_env_phase_triage` är detta **CTO-triage mot Accepted-ADR-mekanik, inte CC-omdöme**, och sannolikt ett ADR 0067-implementeringsnotat eller ADR 0042-amendment.

- **Styrkor:** enda varianten som är både korrekthets-komplett, recall-komplett (region-only-annonser nås) och semantiskt identisk med den web-verifierade Platsbanken/JobTech-baselinen = TD-100:s definition. Kombinerad med Variant A:s per-län-städning (som UX-normalisering, inte korrekthets-bärare) ger den renaste totalen.
- **Kostnader/risker:** bryter E2b:s deklarerade FE-only-ram → kräver scope-omförhandling med Klas-GO (E1b-entanglement-prejudikatet, CTO Approach A). `FacetCountsAsync`/`ExcludeDimension` (E2c) måste få specad ort-semantik: ska kommun-facetten exkludera hela ort-dimensionen eller bara municipality-listan? Den frågan uppstår i E2c **oavsett** variantval — att avgöra ort-semantiken nu, före E2c, undviker omarbete (sekvens-argument CTO bör väga).

### Arkitektonisk slutsats till CTO (ingen rekommendation — beslutsunderlag)

Endast B, B′ och D är korrekthets-kompletta mot dagens backend-AND. Av dem är D ensam recall-komplett och paritets-exakt mot verifierad JobTech-semantik, men kräver Klas-GO för ram-brott. B′ är bästa rena FE-only-kandidaten men bär latent recall-lucka + invariant-komplexitet. A som specad har ett dokumenterbart cross-län-hål. C avvisas. Om CTO väljer FE-only (A/B/B′) bör cross-län-hålet respektive recall-luckan **explicit dokumenteras i E2b:s STOPP-rapport** så Klas-acceptansen av TD-100-avvikelsen är medveten.

---

## Fråga 2 — "Obestämd ort/Utomlands": utanför E2b, defer med trigger

**Dom: utanför E2b-scope.** Klas-villkoret "om payload stödjer" är **inte** uppfyllt:

1. Snapshotten saknar noderna (DB-verifierat: exakt 21 riktiga län, inga pseudo-noder) — `TaxonomyConceptKind` har ingen representation och `taxonomy-snapshot.json`/seedern skulle behöva utökas (backend).
2. Filter-semantiken är en **ny klass**: "annons utan kommun i län X" är `IS NULL`-logik, inte concept-id-IN-lista → ny `ApplyCriteria`-gren + ny param-form (flagga, inte id-lista) → ADR 0042 Beslut B-fråga (backend).
3. De 1 293 annonserna saknar **båda** dimensionerna — de är globalt ortlösa, inte "obestämd ort i län X". En per-län-Obestämd-rad skulle alltså rendera 0 för alla län idag = död UI-yta (ADR 0042 Beslut F-disciplin).
4. "Utomlands" kräver payload-discovery (`country_concept_id`-stabilitet overifierad).

**Disposition:** defer med **payload-verifierings-trigger** — exakt ADR 0043 Beslut E:s instrument (overifierad extern data-dependency = trigger, ej TD, per CLAUDE.md §9.6-precedensen i ADR 0067 Beslut 3/distans). Trigger: riktad raw_payload-discovery av country/ortlösa annonser + observation av region-only-annonser i snapshot-cron. Dokumenteras i E2b:s STOPP-rapport + ADR 0067-implementeringsnotat. Recall-not: de 1 293 förblir nåbara via sök utan ort-filter — ingen tyst förlust i mellantiden.

---

## Fråga 3 — FE-DTO-form: bekräftas, med tre preciseringar

`taxonomyMunicipalitySchema = z.object({ conceptId: conceptIdSchema, label: z.string().min(1) })` + `municipalities: z.array(taxonomyMunicipalitySchema)` på `taxonomyRegionSchema` — **bekräftat**, speglar `TaxonomyRegionDto`/`TaxonomyMunicipalityDto` 1:1.

1. **REQUIRED, inte `.default([])`** — backend (C1) skickar alltid arrayen (tom lista för län utan kommuner är redan backend-garanterad i `TaxonomyReadModel.LoadAsync`); tolerant default skulle maskera kontraktsdrift. Behåll `conceptIdSchema`-regexen (defense-in-depth, E2a-security-precedent).
2. **Payload-storleken ändras INTE av E2b** — wire-svaret bär redan 290 kommuner + 2 323 occupation-namn sedan C1; zod-strip slänger dem efter parse. Det som växer är RSC→client-prop-payloaden till `JobbHeroFilters` (+290 rader ≈ tiotals KB — acceptabelt). Viktigt: **fortsätt strippa `occupations`** (2 323 rader) — addera inte fältet av misstag när region-schemat ändras.
3. **Revalidate-cachen (3600 s) behöver ingen bust** — Next cachear response-bytes per URL, inte zod-output; ny parse-form läser samma cachade bytes korrekt.

---

## Fråga 4 — Atomisk batch-yta: bekräftad + fyra tillägg

Discovery-listan är korrekt. Tillägg/skärpningar (allt i EN commit — `feedback_di_with_handlers_same_commit`, E2a-mönstret):

1. **`jobb-results.tsx` `buildPageHref` + `rawParams`-typen** (rad 166–192) — pagineringen är en ANDRA URL-builder vid sidan av `buildJobbHref`; utan municipality tappar sida-2-klicket kommun-filtret (samma felklass som F3 B-FIX, lätt att missa eftersom den inte syns i `buildJobbHref`-greppen).
2. **`jobb-results.tsx` `selectedConceptIds`** (rad 71) måste inkludera municipality — annars renderas kommun-chips som "Okänd kod (id)" trots fungerande filter.
3. **`jobb-filter-popover.tsx` själv ingår i batchen vid Variant A/B′/D:** dagens two-column-kontrakt har EN `selected`-lista och `toggleAll` skriver höger-kolumnens ids i samma lista. För Ort måste "Hela länet"-raden skriva en **annan axel** (region) än kommun-raderna (municipality). Tvinga inte in region-ids i kommun-listan — utöka kontraktet (dual-axis-props `selectedRegions`/`selectedMunicipalities` + axel-medveten onChange, alternativt en tredje mode). Vid ren Variant B räcker befintligt kontrakt (en lista), vilket CTO bör väga in som komplexitets-delta. Ort-pickern behöver också Yrke-pickerns key-remount-mönster för `activeLeft`-reset.
4. **Suspense-key i `page.tsx`** — bekräftat: `municipalityKey` måste in i key-strängen (rad 229), annars stale resultat-yta vid kommun-byte. Även: `JobbSearchParams`-typen, `toStringList`-parse, hidden inputs i GET-formuläret, `JobbHeroFilters`-/`JobbResults`-props.
5. **Tester i samma commit** (CLAUDE.md §7): `jobb-hero-filters.test.tsx`, `jobb-results-toolbar.test.tsx` + recent-search-fixtures; invariant-logiken (vald variant) ska ha egna unit-tester — den är E2b:s enda riktiga logik.
6. **Residual UTANFÖR E2b (flaggas, fixas ej här):** backend `RecentJobSearchDto.SsykList/SsykLabels`-shimmet skulle "tas bort i Fas E" (C2-notat F5, `src/JobbPilot.Application/RecentJobSearches/Queries/RecentJobSearchDto.cs:25-27`) — det är en backend-touch och kan inte ingå i FE-only-E2b. FE-zod refererar inte längre `ssykList` → borttagningen är oblockerad; egen liten backend-städ-touch med Klas-GO (eller del av D om CTO väljer den).

---

## Fråga 5 — Chip-axel: samma MapPin; reverse-lookup verifierad kind-agnostisk

- **Samma `MapPin` för municipality-chips.** Chipen representerar dimensionen Ort; län/kommun är granulariteter av samma sak. Egen ikon = ikon-zoo utan informationsvärde (civic-utility: stram visuell vokabulär; jfr design-reviewers E2a-dom om dimension vs taxonomi-nivå). Chips-ordning: region → municipality → occupationGroup (geografi samlad).
- **`resolveTaxonomyLabels` täcker kommun-ids automatiskt — verifierat on-disk:** `TaxonomyReadModel.LoadAsync` bygger `labelByConceptId` över **alla** concepts oavsett Kind (`src/JobbPilot.Infrastructure/Taxonomy/TaxonomyReadModel.cs:160-162`), och `ResolveLabelsAsync` slår i den kind-agnostiskt med "Okänd kod (id)"-fallback. Ingen backend-ändring behövs.
- **Cap-aritmetik, värd en kommentar:** `ResolveTaxonomyLabelsQueryValidator`-cap = `MaxConceptIds × 2` = 800. Värsta teoretiska selection = 400 (occupationGroup) + 21 (region) + 290 (municipality) = 711 < 800 — täcker, men marginalen är smal; om en fjärde dimension (employmentType, B2) någonsin chip-resolvas spricker den. Notera i kod-kommentar nu, åtgärda när dimensionen faktiskt tillkommer.

---

## Referenser

- ADR 0067 Beslut 7 rad 104/109 + implementeringsnotat E2a (`docs/decisions/0067-platsbanken-search-parity.md`)
- ADR 0043-amendment 2026-06-08 §1 (`docs/decisions/0043-taxonomy-acl-for-search-surface.md:165-205`)
- ADR 0042 Beslut B (AND-mellan/OR-inom — ortogonalitets-tolkningen, fråga 1C/1D)
- JobTech JobSearch geografi-semantik: [GettingStartedJobSearchEN.md](https://gitlab.com/arbetsformedlingen/job-ads/jobsearch-apis/-/blob/main/docs/GettingStartedJobSearchEN.md) (raw-läst 2026-06-11 — "most local promoted", Haparanda+Norrbotten-exemplet)
- Evans 2003 kap. 14 (ACL-riktning, Variant B′-invändningen); Fowler 2018 (Rename/atomicitet); CLAUDE.md §1/§4/§5.2/§7/§9.5/§9.6
- E2a-format-prejudikat: `docs/reviews/2026-06-10-sok-paritet-e2a-reviews.md`

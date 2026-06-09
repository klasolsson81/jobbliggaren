# ADR 0043 — Taxonomi-ACL för sök-ytan

**Datum:** 2026-05-17
**Status:** Accepted — beslutsinnehållet är låst (senior-cto-advisor + Klas-GO 2026-05-17); Accepted-flip utförd 2026-05-17 på Klas review-GO efter rapport-granskning
**Beslutsfattare:** Klas Olsson
**Relaterad:** ADR 0042 (sök-yta-informationsarkitektur — **Beslut B-domänkontrakt OFÖRÄNDRAT, Beslut C-datakälla utvidgas; rad 21-constraint uppfyllt, inte brutet**), ADR 0039 (SavedSearch-aggregat — `SearchCriteria`-VO + jsonb-dedupe **orört**), ADR 0032 (JobTech-integration — sync-skrivlast-precedens som stöder MAP-1=Variant A), ADR 0009 (ingen Repository; `IAppDbContext` aggregate-per-DbSet-invariant som stöder MAP-2), ADR 0008 (pipeline behavior order — validator-yta för reverse-lookup-cap), CLAUDE.md §1 (civic-utility), §2.1 (lager), §2.3 (CQRS), §3.3 (records/DTO), §5.1 (anti-patterns), §9.6 (in-block vs TD/fas-regeln), §10.3 (rak svenska), jobbpilot-design-principles regel 3/7

---

## Kontext

ADR 0042 löste sök-ytans **layout** (Beslut A — resultat-först, kollaps-filter) men inte dess **vokabulär**. Vid en live-jämförelse 2026-05-17 mellan JobbPilots `/jobb` och `arbetsformedlingen.se/platsbanken/annonser` observerade Klas att sök-ytan exponerar **JobTech-domänens interna identifierare** direkt i användarytan: concept-id (t.ex. `MVqp_eS8_kDZ`), font-mono-chips och taxonomi-jargong.

Detta är ett **läckande bounded context** (Evans 2003, *Domain-Driven Design* kap. 14 — Anticorruption Layer). JobTech-taxonomins identifierare är *deras* ubiquitous language — inte JobbPilots, och definitivt inte slutanvändarens. `JobAdSearch.cs` rad 23-25 erkänner redan i query-vägen att JobTech-taxonomi inte är JobbPilots ubiquitous language; UI-ytan bryter samma princip som koden respekterar. CLAUDE.md §1 (civic-utility — tänk 1177/Digg, inte power-tool) och §10.3 (rak, konkret svenska) är direkt brutna. Detta är inte kosmetisk polish — det är ett informationsarkitektur-fel en Mastercard-nivå-granskare markerar direkt.

Krafter som spelar in:

- **Civic-utility-tonen** (CLAUDE.md §1/§10.3; jobbpilot-design-principles regel 3/7): en användare som vill söka "systemutvecklare i Stockholm" ska välja **svenska namn i hierarkiska väljare**, inte klistra in en concept-id-sträng. En etikett "Yrke" som ändå kräver `MVqp_eS8_kDZ` är *mer* vilseledande än ingen etikett — den löser symptomet, inte sjukdomen.
- **ADR 0042 rad 21-constraint:** "inget externt taxonomi-API **på sök-vägen**". En lokal taxonomi-snapshot är per definition inte på sök-vägen (samma resonemang som ADR 0042:s C2-avvisning) → constraintet är **uppfyllt, inte brutet**.
- **Taxonomi är referensdata** som ändras månads-/kvartalsvis (web-verifierat 2026-05-17). Det är inte volatil transaktionsdata — persistens-mekanismen ska väljas mot den kadensen, inte mot en hypotetisk realtidsfärskhet.
- **Fas 2 är formellt stängd.** Denna redesign är en post-closure kvalitetsåtgärd på en befintlig Fas 2-yta (ej ny feature). Klas har gett strategisk GO på fas-reopening + ACL-arkitektur + ny ADR 0043 (ej ADR 0042-amendment) explicit (docs/reviews/2026-05-17-soktyta-platsbanken-cto.md FRÅGA 1 + FRÅGA 3).

Besluten är **redan fattade** (senior-cto-advisor Approach A + MAP-1/2/3, Klas-GO på Approach A + ADR 0043-granularitet). Denna ADR strukturerar låsta beslut; den introducerar inga nya multi-approach-val. Verbatim beslutsunderlag: docs/reviews/2026-05-17-soktyta-platsbanken-cto.md, docs/reviews/2026-05-17-fynd2-taxonomi-acl-architect.md, docs/reviews/2026-05-17-fynd2-taxonomi-acl-cto.md.

## Beslut

### Beslut A — Approach A: lokal taxonomi-snapshot + Anticorruption Layer

Sök-ytan visar **svenska namn i hierarkiska väljare** — Region: Län → Kommun; Yrke: Yrkesområde → Yrke. Användaren ser och väljer **aldrig** concept-id. Namn↔concept-id-mappning sker via en lokalt persisterad taxonomi-snapshot i en Anticorruption Layer (Evans 2003 kap. 14), aldrig på sök-vägen per tangenttryck. JobTech-taxonomins jargong försvinner ur UI:t.

Avvisat: B (frontend-konstant — DRY-brott, två sanningskällor), C (de-jargonisera endast copy — snabblösning, löser symptom ej sjukdom), C2/live-API (extern hop på interaktiv sök-väg — redan avvisat ADR 0042 Beslut C). Se "Alternativ som övervägdes".

### Beslut B — Persistens (MAP-1): committad JSON-snapshot + idempotent seeder

Taxonomi-snapshotten persisteras som en committad artefakt `taxonomy-snapshot.json` i Infrastructure, laddad i en fristående DB-tabell via **idempotent seeder** (`IHostedService`-mönstret, jfr `IdempotentAdminRoleSeeder`) — **inte** EF `HasData`. Ingen runtime-extern-hop någonsin. Ingen cron. Ingen admin-trigger.

- **Seeder framför `HasData`:** `HasData` materialiserar all seed-data i varje migration-snapshot och tvingar en ny migration vid varje regenerering (~600 rader region/kommun/yrkesområde/yrke + relationer blåser upp model-snapshot och diffar tungt). En idempotent seeder (upsert mot natural key = concept-id) håller migrationen ren (endast `CREATE TABLE`) och gör snapshot-uppdatering till en ren data-operation, inte en schema-operation (REP/CCP, Martin 2017 kap. 13 — återanvänd befintlig seeder-mekanism).
- **Färskhets-kadens:** manuell regenerering + commit. En utvecklare kör ett off-repo engångs-script (eller manuell fetch) som producerar `taxonomy-snapshot.json`, committar filen, deployar; seedern upsertar idempotent vid app-start. **Inte** build-tids-fetch (bryter hermetisk build — Software Engineering at Google 2020 kap. 18 — och återinför den externa-yta-på-kritisk-väg Approach A finns för att eliminera). Snapshot-filen är versionerad referensdata under granskning som vilken annan committad artefakt.
- **Graceful degradation vid stale snapshot:** okänt concept-id i en sparad sökning → reverse-lookup-fallback `"Okänd kod (<id>)"`. Ingen krasch, ingen data-migration — medveten resiliens, inte en brist.
- **Snapshot-kanonisering (defekt-triage 2026-05-17, CTO Defekt #1 Variant C):** JobTech-taxonomin är en GRAF — en occupation-name kan tillhöra flera occupation-fields (359 av 2724 par i råsnapshoten). `TaxonomyConcept.ConceptId` är PK med single `ParentConceptId`. Den committade `taxonomy-snapshot.json` ska därför vara **kanoniskt dedupliserad vid commit**: varje occupation-name förekommer under exakt ett (primärt/första, deterministiskt valt — occupation-fält sorterade på conceptId) occupation-field. Dedup sker i den off-repo snapshot-genereringen, INTE i runtime-`MapRows` (artefakten kanonisk vid commit; `MapRows` trivial 1:1-projektion). Motiv: Evans kap. 14 (ACL ska isolera från extern modell-komplexitet, ej replikera den troget) + YAGNI/KISS (multi-membership är osynligt — filtrering på `ssyk_concept_id` shadow-prop per Beslut E, ej pickerns grenval; samma concept-id → samma sökresultat). Trade-off: multi-membership-fidelitet förloras i lokala modellen (yrke visas endast under primärt yrkesområde i pickern) — NOLL sökresultatpåverkan; samma klass av medveten ACL-förenkling som Beslut E (kommun bort). Generatorn måste ha en deterministisk primär-fält-regel (annars snapshot-diff-brus).
- **Prod-startup-fixtur-paritet (defekt-triage 2026-05-17, CTO Defekt #3 Variant A):** `TaxonomySnapshotSeeder` speglar `IdempotentAdminRoleSeeder` även i test-kontraktet: prod-lika startup-fixturer som triggar host-start före/utan `AppDbContext`-migration plockar bort seeder-descriptorn ur DI (samma N-2-hardening-mekanism som admin-seedern). Seederns 42P01-grace förblir gated Dev/Test → fail-loud i Production (CLAUDE.md §3.4) — kontraktet ändras EJ för testfixturer.

### Beslut C — ACL-inkapsling (MAP-2): `ITaxonomyReadModel`-port; `IAppDbContext` växer EJ

ACL:n materialiseras som en Infrastructure-intern snapshot-entity (`TaxonomyConcept`) bakom Application-porten `ITaxonomyReadModel`. `IAppDbContext` utökas **inte** med `DbSet<TaxonomyConcept>`.

- **Motiv:** ADR 0009:s aggregate-per-DbSet-invariant (`IAppDbContext` = "DbSet&lt;T&gt; per aggregate root"; `TaxonomyConcept` är **inget aggregate root** — inga invarianter, ingen state-övergång, ingen domän-identitet) + CLAUDE.md §5.1 (ingen EF-entity över Application-gränsen) + Evans 2003 kap. 14 (ACL-data ska vara inkapslad bakom en explicit översättnings-port, inte exponerad rått) + ISP (Martin 2017 kap. 10 — aggregate-handlers ska inte tvingas se en read-model de aldrig rör) + IJobSource-precedens (Application-port, `internal` Infrastructure-impl, DTO över gränsen).
- Porten exponerar två operationer: **hierarki-hämtning** (regioner→kommuner, yrkesområden→yrken som Application-DTOs, `record class`) och **reverse-lookup** (concept-id-lista → namn; okänt id → `"Okänd kod (<id>)"`-fallback, aldrig null/throw).
- ACL:n lever **utanför** query-vägen. `JobAdSearch.ApplyCriteria`-shadow-prop-filtreringen (`SsykConceptId`/`RegionConceptId`) är **orörd** — den opererar på rå concept-id och förblir namn-omedveten.

### Beslut D — DoS/cache-disciplin (MAP-3)

- **Rate-limit:** ny dedikerad policy `TaxonomyReadPolicy`, partitionerad per UserId (claim `sub`), `NoLimiter` för anonym (RequireAuthorization → 401 före endpoint). Parametrar `IOptions`-bundna i `RateLimitingOptions` (CLAUDE.md §5.1 — ingen hårdkodning). Riktvärde **`PermitLimit = 20`, `WindowSeconds = 60`** (security-auditor verifierar/justerar — BLOCKING kvarstår). Egen policy framför återanvänd `ListReadPolicy`/`SuggestPolicy`: least common mechanism (Saltzer/Schroeder 1975 — samma princip som redan motiverade `SuggestPolicy` att inte återanvända `ListReadPolicy`); picker-trädet har en strukturellt lägre legitim-frekvensprofil än både list/search (60/min) och per-keystroke-typeahead (30/10s).
- **Reverse-lookup-cap:** cappas i `GetTaxonomyTreeQueryValidator` (FluentValidation, FÖRE handler — speglar `SuggestJobAdTermsQueryValidator`) till **`SearchCriteria.MaxConceptIds` (=10) per dimension**, refererad som konstant — **inte** hårdkodad `10`. Motiv: domän-konsekvens/DRY (en sparad sökning kan per domän-invariant aldrig bära fler concept-id än `MaxConceptIds` per dimension; om domänen höjer caps följer query-cap automatiskt), OWASP API4:2023 / CWE-400, complete mediation (Application-gränsen litar inte på klienten). Hela-trädet-hämtningen har ingen användarstyrd `Take`/`Skip` (fast storlek ~21 län + ~290 kommuner + ~30 yrkesområden + några hundra yrken); `kind`-diskriminator är sluten värdemängd, ingen blowup-vektor.
- **Cache:** **både** HTTP `ETag` + `Cache-Control: private, max-age=<kort>` på endpointen (ETag = hash/version av snapshotten; `private` — endpointen är auth-gated, `public`/shared-proxy på auth-gated svar är en cache-poisoning/cross-user-läckage-vektor; ingen `Vary: Authorization`-fälla) **och** in-memory-cache (`IMemoryCache`) i Infrastructure bakom porten (snapshot läses en gång per process-livstid; invalidering = process-restart vid deploy, eftersom Variant A gör att snapshotten bara ändras vid deploy). Tre billiga, oberoende lager mot resurskonsumtion (ETag = en header, in-memory = en `GetOrCreate`, rate-limit = befintlig mekanism) — proportionerligt, inte over-engineering. security-auditor verifierar de konkreta talen innan commit.

### Beslut E — Sök-yta-granularitet i denna leverans (scope-fork 2026-05-17, CTO Variant A)

Sök-yta-granulariteten i denna leverans är **Län (`region_concept_id`) + Yrke (`ssyk_concept_id` på occupation-name-nivå via Yrkesområde→Yrke GraphQL-hierarki)**. Implementation-discovery (JobAdConfiguration.cs rad 74-80 + F2P9-migration) verifierade att `ssyk_concept_id` är occupation-name-nivå och `region_concept_id` region/län-nivå; ingen `municipality_concept_id`-kolumn finns.

Shadow-prop-filtreringen (`JobAdConfiguration.cs` rad 74-80, `JobAdSearch.ApplyCriteria`) är **oförändrad**. ACL:n är en namn↔concept-id-**översättning ovanpå befintliga shadow-props**, INTE en ny filtrerings-dimension. ADR 0042 rad 21-constraintet och "shadow-prop ORÖRD"-garantin är **uppfyllda och uttryckligen ej brutna**. Ingen municipality/kommun-dimension ingår i denna leverans — kommun-granularitet är ett payload-verifierings-trigger-beslut (se "Konsekvenser / Framtida revision"), inte uppskjutet arbete.

CTO-beslut Variant A (leverera inom låst design nu) — kärnmålet (concept-id ur UI + ACL materialiserad) är fullt uppfyllt på länsnivå; Kommun-vs-Län är en granularitets-axel, inte ett ACL-mål. Verbatim: docs/reviews/2026-05-17-fynd2-taxonomi-acl-cto.md "Scope-fork 2026-05-17 (Kommun-nivå)".

## Konsekvenser

**Positiva:**
- Sök-ytan följer civic-utility-tonen (CLAUDE.md §1/§10.3) — svenska namn-väljare speglar Platsbanken/1177, inte ett power-tool. JobTech-jargong försvinner ur UI:t.
- Det läckande bounded context (Evans kap. 14) tätas: ACL:n inkapslar JobTechs ubiquitous language bakom en explicit översättnings-port — UI-ytan respekterar nu samma princip som query-vägen redan gör.
- ADR 0042 rad 21-constraintet ("inget externt taxonomi-API på sök-vägen") är **uppfyllt** — lokal snapshot är per definition inte på sök-vägen, ingen extern hop per tangenttryck, ADR 0032:s resilience-stack belastas inte.
- Domänen är orörd: `SearchCriteria` VO oförändrad, ingen ny Domain-typ, ingen migration på `saved_searches`. Backend-filtrerings- och persistens-vägen är intakt — den enda nya backend-ytan är picker-läs-vägen.
- `IAppDbContext` hålls smal (ADR 0009-invarianten bevaras) — snapshot är read-model bakom port, ingen lager-läcka.
- Tre billiga cache-/DoS-lager på en deterministisk statisk endpoint → nära-noll resurskostnad på varma vägen.

**Negativa + mitigering:**
- Snapshot-färskhet är en commit-disciplin, inte automatiserad — stale data om ingen regenererar. *Mitigering:* taxonomin är extremt stabil (kvartals-/månadskadens, web-verifierat 2026-05-17); okänt concept-id i en sparad sökning degraderar gracefully till `"Okänd kod (<id>)"` reverse-lookup-fallback, ingen krasch, ingen data-migration.
- En extra port-fil + impl-fil jämfört med rå `DbSet<TaxonomyConcept>`. *Mitigering:* accepterad trade-off — fil-antal är ingen design-axel; inkapsling och kontrakts-renhet är design-värde.
- Ny läs-/inmatnings-väg (endpoint) öppnar DoS-yta. *Mitigering:* `TaxonomyReadPolicy` + reverse-lookup-cap (= `SearchCriteria.MaxConceptIds`) + ETag + in-memory; security-auditor BLOCKING verifierar konkreta tal innan commit.
- Lokala taxonomi-modellen kollapsar JobTechs multi-field-graf till ett primärt yrkesområde per yrke (defekt-triage 2026-05-17). *Mitigering:* medveten ACL-förenkling (Evans kap. 14) — sökresultat opåverkat (filtrering på shadow-prop per Beslut E, ej pickerns gren); yrke ej hittbart under sekundärt yrkesområde i hierarki-pickern men ADR 0042 Beslut C-typeahead är primär upptäcktsväg och reverse-lookup-label är parent-oberoende; deterministisk primär-fält-regel i generatorn undviker snapshot-diff-brus.

**Framtida revision — skala-trigger för Variant B (synkad cron-tabell), ej TD:**

Variant B revisitas **endast** vid en av dessa signaler (skala-trigger, ej tidsbunden TD, ej tracked dump — CLAUDE.md §9.6 anti-pattern):

1. **Färskhets-incident:** dokumenterad användar-rapporterad mismatch mellan JobbPilot-taxonomi och Platsbanken pga stale snapshot (degradationen blir faktiskt synlig, inte hypotetisk).
2. **Kadens-skifte:** JobTech ändrar taxonomi-publiceringskadens från kvartal/månad till vecka/dag (web-verifierbart — taxonomy.api.jobtechdev.se release-kadens).
3. **Operationell börda:** manuell regenerering visar sig kräva > en regenerering/månad i praktiken (KISS-antagandet falsifieras empiriskt).

Ingen av dessa skrivs som TD nu (CLAUDE.md §9.6 — ingen saknad funktion-dependency, ingen annan fas; det hör till sök-ytan = Fas 2:s domän och är ett medvetet uppskjutet skala-beslut, inte uppskjutet arbete). Detta är en "övervägt alternativ"-post, samma disciplin som ADR 0042 Beslut D:s tsvector-skala-trigger.

**Payload-verifierings-trigger för Kommun-granularitet (ej TD):**

Platsbankens Län→Kommun-hierarki är **ej levererad** i denna batch eftersom `municipality_concept_id` (a) inte finns som filtrerbar shadow-kolumn och (b) dess existens i Platsbanken `raw_payload->'workplace_address'` är **overifierad**. Kommun-granularitet revisitas **endast** vid båda dessa trigger-villkor:

1. **Payload-bekräftelse:** verifierat via raw_payload-prov-discovery att `municipality_concept_id`-fältet faktiskt finns i Platsbanken-payloaden, OCH
2. **Användarsignal:** dokumenterad användarsignal om att länsnivå-granularitet är otillräcklig.

Vid trigger: **separat förhandlad batch** (ny `STORED` generated column + partial index + `JobAdSearch.ApplyCriteria`-utökning + migration + ADR 0043-amendment) — **ej autonom** (rör ADR 0032-migrationsyta + ADR 0042 rad 21 + bryter "shadow-prop ORÖRD"-garantin, kräver explicit Klas-GO + diff-granskning). Ej TD (CLAUDE.md §9.6 — overifierad extern datakälle-dependency, ej spårbart kod-arbete; payload-verifierings-trigger i ADR-brödtext är rätt instrument, samma klass som MAP-1 skala-triggern ovan). Verbatim: docs/reviews/2026-05-17-fynd2-taxonomi-acl-cto.md "Scope-fork 2026-05-17 (Kommun-nivå)".

## Alternativ som övervägdes

### Beslut A: B — frontend-konstant med namn↔id-tabell (avvisat)
**Emot:** DRY-brott (Hunt/Thomas 1999) — två sanningskällor (frontend-konstant + JobTech-taxonomi). Mappnings-logiken får ingen SPOT; drift garanterad.

### Beslut A: C — de-jargonisera endast copy (avvisat)
**Emot:** snabblösning förklädd. En etikett "Yrke" som ändå kräver `MVqp_eS8_kDZ` i fältet är *mer* vilseledande än ingen etikett — den löser symptomet, inte sjukdomen (det läckande bounded context).

### Beslut A: C2 — live JobTech taxonomy-API per tangenttryck (avvisat — redan ADR 0042 Beslut C)
**Emot:** externt API på interaktiv sök-väg → latens + resilience-yta som ADR 0032:s stack inte är dimensionerad för per-keystroke. Redan avvisat i ADR 0042 Beslut C; samma resonemang gäller. Lokal snapshot ger samma nytta utan extern hop.

### Beslut B (MAP-1): Variant B — synkad cron-tabell via JobTech-väg (avvisat)
**Emot:** auto-färskhet är den enda vinsten och adresserar ett icke-existerande problem vid kvartalskadens. Maximal blast-radius (ny Refit-klient + resilience-pipeline + recurring-job + Worker-host + DI-utvidgning + ADR 0032-sync-skrivlast-koordinering) mot noll bevisat behov. Bryter YAGNI/KISS (Beck; Fowler 2018 kap. 3 — Speculative Generality) + intern motsägelse mot Approach A:s egen "referensdata, kvartalstakt"-motivering + ADR 0032:s precedens mot spekulativ extern skrivlast-yta.

### Beslut B (MAP-1): Variant C — hybrid seed + opt-in admin-refresh-knapp (avvisat)
**Emot:** lägger till auth-gated admin-endpoint + test-yta + säkerhetsyta för en knapp ingen trycker på kvartalsdata. Kombinerar A:s enkelhet med B:s ytkostnad utan att lösa något A inte redan löser — "speculative generality med disclaimer".

### Beslut C (MAP-2): `DbSet<TaxonomyConcept>` på `IAppDbContext` (avvisat)
**Emot:** snabbare (ingen port-fil) men bryter CLAUDE.md §5.1, urvattnar ADR 0009:s aggregate-per-DbSet-invariant, bryter ISP, och bryter den ACL-inkapslings-princip hela Approach A vilar på. En utomstående arkitekt som ser en rå referensdata-tabell-DbSet bredvid `JobAds`/`Resumes`-aggregaten noterar det direkt som en lager-läcka (Mastercard-testet, CLAUDE.md §1).

### Beslut D (MAP-3): återanvänd `ListReadPolicy`/`SuggestPolicy` (avvisat)
**Emot:** kodbasens egen `SuggestPolicy`-precedens fastslog redan att least-common-mechanism vinner över YAGNI när frekvensprofilerna skiljer. Picker-trädet är varken scrollande filtrering (`ListRead` 60/min) eller per-keystroke-typeahead (`SuggestPolicy` 30/10s) — fel frekvensmodell. Återanvändning vore intern inkonsekvens mot 2026-05-16-beslutet.

### Beslut D (MAP-3): ingen cache / endast ETag / endast in-memory (avvisat)
**Emot:** ingen cache — onödig DB-rundtur per picker-render, ingen `304`-snabbväg. Endast ETag — varje cache-miss/ny-klient träffar DB. Endast in-memory — hela trädet skickas i body även när klienten redan har det. Lagren adresserar olika miss-scenarier; komplementära, inte alternativ.

### Beslut E (scope-fork): Variant B — utöka batchen med kommun-shadow-column (avvisat)
**Emot:** bygger en hel filtrerings-dimension (ny `STORED` generated column + index + `JobAdSearch.ApplyCriteria`-utökning + migration) ovanpå ett fält (`municipality_concept_id`) vars existens i Platsbanken-payloaden är **overifierad** — spekulativ generalitet på obekräftad datagrund (YAGNI/KISS, Beck; Fowler 2018 kap. 3). Maximal blast-radius mot tre avgränsade ADR-ytor (ADR 0042 rad 21 + ADR 0032-migrationsyta + bryter "shadow-prop ORÖRD"-garantin) i en autonom batch. Förgrenad verifiera-sedan-villkorligt-bygg-väg kan ej diff-granskas rent (CLAUDE.md §6.3 punkt 4). Scope creep förklädd som fullständighet. Variant C (stub/blockera) likaledes avvisad — blockering straffar verifierat-färdig civic-vinst för en axel utanför kärnmålet; stub utan data = död persistens-yta. Full motivering: docs/reviews/2026-05-17-fynd2-taxonomi-acl-cto.md "Scope-fork 2026-05-17 (Kommun-nivå)".

## Relation till andra ADR

- **ADR 0042 (Beslut B — domänkontrakt):** `SearchCriteria.Ssyk/Region` förblir `IReadOnlyList<string>` concept-id. **SearchCriteria VO-kontrakt ändras EJ.** Endast inmatnings-/presentationsytan ändras (namn-väljare istället för concept-id-fritext); `onChange` emitterar fortfarande concept-id `string[]` — backend-kontraktet är oförändrat hela vägen (URL-query → `ListJobAdsQuery` → `JobAdSearch.ApplyCriteria` → shadow-props → `SearchCriteria.Create`). ADR 0042 Beslut B-invarianterna (normalisering, cap, tom-invariant, jsonb-bakåtkompat) är orörda.
- **ADR 0042 (Beslut C — typeahead-arkitektur):** typeahead-designen (C1 lokal `job_ads`-prefix, `SuggestPolicy`, self-contained debounce-hook) är **oförändrad**. ADR 0042 rad 21-constraintet ("inget externt taxonomi-API på sök-vägen") är **uppfyllt, inte brutet** — lokal snapshot är per definition inte på sök-vägen. ADR 0043 utvidgar Beslut C:s *datakälla för inmatningsytan* (namn-väljare matas av lokal taxonomi-snapshot) utan att röra Beslut C:s typeahead-väg.
- **ADR 0039 (SavedSearch):** `saved_searches.criteria` jsonb-converter, comparer, jsonb-shape och dedupe-invarianter (Beslut B.1) är **orörda** — VO-formen (`IReadOnlyList<string>` concept-id) är oförändrad; dedupe vilar på concept-id-sekvenslikhet, namn ingår aldrig i VO:t. Ingen ny migration på `saved_searches`. Redan-sparade sökningar renderas via reverse-lookup; okänt id → `"Okänd kod (<id>)"`-fallback (graceful degradation, ingen invariant-risk).
- **ADR 0032 (JobTech-integration):** sync-skrivlast-precedensen (ny dynamisk extern skrivlast-yta måste motiveras mot konkret nytta, inte införas spekulativt) stöder MAP-1=Variant A — den taxonomi-snapshot-tabellen är en fristående tabell utan FK till `job_ads`/`saved_searches`, ingen ny extern HTTP-yta, ingen sync-cron-konflikt.
- **ADR 0009 (ingen Repository):** aggregate-per-DbSet-invarianten stöder MAP-2 — `ITaxonomyReadModel`-porten håller `IAppDbContext` smal; `TaxonomyConcept` är Infrastructure-intern read-model, inget aggregate.

## Implementationsstatus

Ej påbörjad. Besluten är fattade (senior-cto-advisor Approach A — docs/reviews/2026-05-17-soktyta-platsbanken-cto.md; MAP-1/2/3 — docs/reviews/2026-05-17-fynd2-taxonomi-acl-cto.md; dotnet-architect Clean Arch-design — docs/reviews/2026-05-17-fynd2-taxonomi-acl-architect.md; Klas-GO på Approach A + ADR 0043-granularitet + autonomt non-stop 2026-05-17). Implementations-sekvens (dotnet-architect-skiss §5, CTO-godkänd oförändrad):

1. **db-migration-writer:** fristående snapshot-tabell-migration (`CREATE TABLE`, ingen FK, ingen `saved_searches`/`job_ads`-påverkan).
2. **test-writer FÖRST/TDD:** ACL reverse-lookup (okänt id → fallback, ej throw), hierarki (parent-child korrekt), `GetTaxonomyTreeQueryHandler` (happy path + validator-failure), bakåtkompat (sparad `SearchCriteria` renderar namn + okänt id → fallback), arkitektur-test (Application refererar inte Npgsql/EF-entities; Domain orörd).
3. **Backend-impl** (test-grön): Application-port + query/handler/validator/DTO; Infrastructure snapshot-entity + EF-config + `TaxonomyReadModel`-impl + seed/JSON; DI-registrering **i samma commit** som handler/port-impl; Api MapGet + `TaxonomyReadPolicy` + ETag.
4. **security-auditor — BLOCKING:** verifierar rate-limit-tal (20/60s riktvärde), reverse-lookup-cap, `private`-cache-direktiv, ETag-derivering, ingen oavsiktlig logg-yta.
5. **nextjs-ui-engineer** (separat batch): ersätt concept-id-fritext med hierarkiska väljare (Län→Kommun, Yrkesområde→Yrke); behåll ADR 0042 Beslut A-disclosure + Beslut B URL-multi-kontrakt; `onChange` emitterar fortfarande concept-id `string[]`.
6. **design-reviewer VETO + visual-verify** (Klas godkänner skärmbilder).

Status-flip till Accepted bekräftas av Klas-GO efter att han granskat reviews-rapporterna (ADR-accept = Klas-STOPP, samma konvention som ADR 0039/0042 header).

## Krav på Klas-GO

| Punkt | Kräver Klas-GO? |
|---|---|
| Beslut A (Approach A) + ADR 0043-granularitet (ny ADR, ej amendment) | Nej — redan GO:at 2026-05-17 (CTO FRÅGA 1 + Klas) |
| Beslut B/C/D (MAP-1/2/3 — persistens, port, DoS/cache) | Nej — design-delbeslut inom redan GO:at mandat (CTO) |
| ADR 0043 Accepted-flip | **JA** — Klas review-GO efter rapport-granskning (sessionens förbud: ingen ADR Accepted-flip utan Klas-GO) |
| security-auditor konkreta rate-limit-/cache-tal | BLOCKING — verifieras innan commit (input, ej kringgående av veto) |

---

*Referenser: Eric Evans, Domain-Driven Design (2003) kap. 5, 14 (Anticorruption Layer / read-model); Robert C. Martin, Clean Architecture (2017) kap. 7, 10 (ISP), 13 (REP/CCP); Beck (XP, YAGNI); Fowler, Refactoring 2nd ed (2018) kap. 3 (Speculative Generality); Ford/Parsons/Kua, Building Evolutionary Architectures (2017) kap. 4 (blast-radius); Winters/Manshreck/Wright, Software Engineering at Google (2020) kap. 18 (hermetiska builds); Hunt/Thomas, Pragmatic Programmer (1999) DRY/SPOT; Saltzer/Schroeder (1975) least common mechanism / complete mediation / defense in depth; OWASP API4:2023 (Unrestricted Resource Consumption), CWE-400, OWASP Web Cache Deception; Nygard, Documenting Architecture Decisions (2011). ADR 0008, 0009, 0032, 0039, 0042; CLAUDE.md §1, §2.1, §2.3, §3.3, §5.1, §9.6, §10.3; jobbpilot-design-principles regel 3/7. Beslutsunderlag: docs/reviews/2026-05-17-soktyta-platsbanken-cto.md, docs/reviews/2026-05-17-fynd2-taxonomi-acl-architect.md, docs/reviews/2026-05-17-fynd2-taxonomi-acl-cto.md.*

---

## Amendment 2026-06-08 — Kommun-dimension + yrkesgrupp-nivå (per ADR 0067)

**Datum:** 2026-06-08
**Källa:** Platsbanken-sök-paritets-initiativet ([ADR 0067](./0067-platsbanken-search-parity.md)), design-grind 2026-06-08. Additivt amendment-notat — ADR-immutabilitet: all brödtext ovan (inkl. Beslut A–E) är medvetet orörd.
**Beslutsfattare:** Klas Olsson + senior-cto-advisor (decision-maker) + dotnet-architect 2026-06-08.
**Status:** ADR 0043 förblir **Accepted** — den superseras **inte**. ACL-kärnan (lokal snapshot, concept-id aldrig i UI, `ITaxonomyReadModel`-port, `IAppDbContext` växer ej, snapshot-dedup) **består oförändrad**. Detta amendment utvidgar **granularitet + dimensioner**; det häver två avgränsningar Beslut E gjorde medvetet temporära.

ADR 0043 Beslut E sköt upp **Kommun-granularitet** pending en payload-verifierings-trigger (två villkor: payload-bekräftelse + användarsignal) och fastställde att yrke-pickern går **occupation-field → occupation-name**. Bägge revideras nu per ADR 0067:

### 1. Kommun-dimensionen levereras (Beslut E payload-trigger uppfylld)

Payload-verifierings-triggern är uppfylld: `municipalityconceptid` är verifierat närvarande i JobTech-annons-payloaden (live-läsning 2026-06-08, `workplace_address.municipality_concept_id`; POCO:n `JobTechWorkplaceAddress` deserialiserar det redan), och Klas-direktivet 2026-06-08 är användarsignalen. Per Beslut E:s eget protokoll ("separat förhandlad batch + ADR 0043-amendment, ej autonom, Klas-GO") levereras kommun nu:

- Ny STORED generated column `municipality_concept_id ← raw_payload->'workplace_address'->>'municipality_concept_id'` (Klass 1 — payload finns, ingen re-ingest). Partial B-tree-index `WHERE municipality_concept_id IS NOT NULL`, exakt region-mönstret.
- `TaxonomyConceptKind += Municipality` (parent = Region). Kommun→län är 1:1 (`municipality broader→region`) → **ingen multi-parent-dedup** (till skillnad från occupation-name-grafen).
- `TaxonomyTreeDto`/`TaxonomyRegionDto` växer additivt: `IReadOnlyList<TaxonomyMunicipalityDto> Municipalities` under varje region (speglar `OccupationField → Occupations`-mönstret). Port-signaturen (`ITaxonomyReadModel.GetTreeAsync`) är **oförändrad** — rikare DTO, inget nytt kontrakt. `LoadAsync`-grupperingen blir `Kind`-medveten (impl-ändring, ej modell-ändring). Single `ParentConceptId` räcker oavsett djup — `Kind` ÄR nivå-diskriminatorn (ingen redundant `Level`-int, DRY).
- "Shadow-prop ORÖRD"-garantin (Beslut E) **häves medvetet** — kommun blir en ny filtrerbar shadow-dimension. Detta är amendment-ets uttryckliga syfte, ej en oavsiktlig regression.

### 2. Yrke-pickern byter nivå occupation-field → ssyk-level-4 (yrkesgrupp)

Beslut E:s yrke-granularitet (occupation-field → **occupation-name**) ändras till occupation-field → **ssyk-level-4 (yrkesgrupp)** för Platsbanken-paritet (Platsbanken filtrerar yrke på ssyk-level-4, ej occupation-name — discovery 2026-06-08). Detaljer i [ADR 0067 Beslut 1](./0067-platsbanken-search-parity.md):

- Ny STORED column `occupation_group_concept_id ← raw_payload->'occupation_group'->>'concept_id'` (Klass 1) blir primärt yrke-filter.
- `TaxonomyConceptKind += OccupationGroup` (parent = OccupationField). Snapshot-trädet krymper occupation-name (~2179) → ssyk-level-4 (~400) → **ADR 0043 Beslut B:s snapshot-kanoniserings-/dedup-skuld (359/2724 multi-field-par) krymper mot noll** (ssyk-level-4→occupation-field är en renare single-parent-relation). Amendment-et betalar ner Beslut B:s dedup-komplexitet.
- `ssyk_concept_id` (occupation-name) + index **bevaras** — degraderas från primärt filter till synonym-/recall-input + queryable substrat för CV-matchning (TD-93/ADR 0040). Beslut E:s shadow-prop raderas alltså inte; den byter roll.
- Reverse-lookup-migration av gamla sparade `Ssyk`-sökningar (occupation-name → parent ssyk-level-4) — ADR 0067 Beslut 1 + Fas C2.

### Konsekvenser

- ACL-principen (ADR 0043 Beslut A–D) består: svenska namn i UI, concept-id aldrig exponerat, lokal in-memory-snapshot, ingen extern hop på sök-vägen. Kommun + yrkesgrupp är nya **dimensioner inom samma ACL**, inte en ny arkitektur.
- Taxonomi-snapshot (`taxonomy-snapshot.json` + seeder) regenereras med kommun-noder (parent=region) + ssyk-level-4-noder (parent=occupation-field). Seeder-mekanismen (idempotent upsert, Beslut B) är oförändrad.
- Snapshot-dedup-disciplinen (Beslut B kanonisering) **lättar** — kommun 1:1, ssyk-level-4 nästan 1:1.

### Implementations-trail (ADR 0067-faser)
- Fas B1: `municipality_concept_id` + `occupation_group_concept_id` STORED + EF-config + migration (Testcontainers) + snapshot-utökning + seeder.
- Fas C1: `ITaxonomyReadModel`-DTO + `ApplyCriteria`-utökning + validators.
- Fas C2: reverse-lookup-migration (sparade sökningar).
- Fas E: FE Län→Kommun-kaskad + Yrkesområde→Yrkesgrupp-picker.

### Referenser
- [ADR 0067](./0067-platsbanken-search-parity.md) (Platsbanken-sök-paritet — Beslut 1/2 + ADR 0043-amendment-källa)
- Discovery + agent-domar: `docs/research/2026-06-08-platsbanken-sok-paritet-discovery.md`, `docs/reviews/2026-06-08-sok-paritet-{architect,cto,cto-followup}.md`
- ADR 0043 Beslut E (payload-trigger-protokoll — följt exakt), Beslut B (snapshot-dedup — skuld krymper)
- Evans DDD (2003) kap. 14 (ACL utvidgning); Hunt/Thomas (1999) DRY (Kind-diskriminator ej Level-int); Nygard (2011) additivt amendment ej supersession

### Implementerings-notat 2026-06-09 (Fas C1) — Beslut D reverse-lookup-cap-multiplikator 2 → 4

ADR 0043 Beslut D:s reverse-lookup-cap (`ResolveTaxonomyLabelsQueryValidator.MaxConceptIdsPerCall`) är härledd ur `SearchCriteria.MaxConceptIds` (DRY single-source). Multiplikatorn antog **2 dimensioner** (Ssyk + Region). Fas C1 inför OccupationGroup + Municipality → upp till **4 filterbara dimensioner** en chip-render kan materialisera i den platta `ConceptIds`-listan (legacy-Ssyk inkluderat — gamla sparade sökningar bär occupation-name-ids som måste label-resolvas tills Fas C2 reverse-lookup-migrerar dem). Multiplikatorn justeras **2 → 4** (= 1600 med MaxConceptIds=400). Beslut D-mekaniken (cap = MaxConceptIds × dimensioner, deriverad konstant) **består** — detta är en mekanik-konkretisering, inte ett amendment. Säkert: O(n) in-memory dict-lookup, auth + rate-limited (TaxonomyReadPolicy), per-element MaximumLength(32). senior-cto-advisor-dom 2026-06-09 (`docs/reviews/2026-06-09-sok-paritet-c1-cto.md`).

**Fas C2-uppdatering 2026-06-09 (CTO-dom (e)):** legacy-Ssyk-skälet för den fjärde dimensionen utgick med C2-reverse-lookup-migrationen (occupation-name-ids finns inte längre i sparade sökningar), men **×4 består** — dims = OccupationGroup + Municipality + Region + headroom för B2-dimensionerna (employment_type/worktime_extent, resolverbara post re-ingest). Capen är ett tak, inte en exakt summa; churn 4→3→5 vore poänglös (`docs/reviews/2026-06-09-sok-paritet-c2-cto.md`).

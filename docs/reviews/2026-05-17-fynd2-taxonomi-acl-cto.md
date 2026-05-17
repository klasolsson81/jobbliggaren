# CTO-beslut — Fynd 2: Taxonomi-ACL MAP-1/2/3

**Datum:** 2026-05-17
**Decision-maker:** senior-cto-advisor (read-only — persisteras av CC)
**Status:** Entydiga beslut. Implementation kör non-stop direkt på dessa.
**Kontext:** Klas övergripande GO redan givet på CTO Approach A (docs/reviews/2026-05-17-soktyta-platsbanken-cto.md FRÅGA 1) + ADR 0043 (ny, ej amendment) + fullt autonomt non-stop. Denna rond avgör endast de tre öppna design-punkterna i dotnet-architect-skissen (docs/reviews/2026-05-17-fynd2-taxonomi-acl-architect.md MAP-1/2/3).
**Källa-bekräftelse:** ACL-placering (skiss §2), picker-query-mönster (skiss §3), bakåtkompat (skiss §4) och implementations-sekvens (skiss §5) är ren design mot Clean Arch och **godkänns oförändrade** — ingen multi-approach där, ingen CTO-ändring. Endast MAP-1/2/3 avgörs nedan.

---

## MAP-1 — Snapshot-persistens

### Beslut: Variant A — seedad tabell (committad JSON-snapshot → idempotent seeder)

Taxonomi-snapshotten persisteras som en committad artefakt `taxonomy-snapshot.json` i Infrastructure, laddad i en fristående DB-tabell via **idempotent seeder** (`IHostedService`-mönstret, jfr `IdempotentAdminRoleSeeder`), **inte** EF `HasData`. Ingen runtime-extern-hop, någonsin. Ingen cron. Ingen admin-trigger.

**Seeder framför `HasData`:** EF `HasData` materialiserar all seed-data i varje migration-snapshot och tvingar en ny migration vid varje snapshot-regenerering (~600 rader region+kommun+yrkesområde+yrke + relationer blåser upp model-snapshot och diffar tungt). En idempotent seeder (upsert mot natural key = concept-id) håller migrationen ren (endast `CREATE TABLE`) och gör snapshot-uppdatering till en ren data-operation, inte en schema-operation. Detta speglar redan etablerat mönster i kodbasen (`IdempotentAdminRoleSeeder`) → REP/CCP (Martin 2017 kap. 13): återanvänd den befintliga seeder-mekanismen, inför inte en andra seed-väg.

### Motivering mot principer

- **YAGNI / KISS (Beck; Fowler 2018, *Refactoring* kap. 3 "Speculative Generality"):** Variant B löser auto-färskhet — ett problem som inte existerar vid kvartals-/månadskadens. Att bygga en Refit-klient + resilience-pipeline + recurring-job + sync-orchestrator för data som ändras 4 gånger/år är spekulativ generalitet i ren form. KISS väljer den enklaste mekanismen som faktiskt löser det faktiska problemet.
- **Egen precedens (docs/reviews/2026-05-17-soktyta-platsbanken-cto.md rad 22):** Mitt eget Approach A-beslut motiverade explicit "taxonomi är referensdata, ändras månads-/kvartalsvis". Variant A är den persistens-form som är konsekvent med den motiveringen. Att nu välja B vore intern motsägelse mot det redan GO:ade beslutet.
- **Blast-radius (Ford/Parsons/Kua 2017, *Building Evolutionary Architectures* kap. 4):** Variant A = en migration (CREATE TABLE) + en seeder + en JSON-fil. Variant B = ny Refit-klient + ny resilience-pipeline + ny `RecurringJobRegistrar`-rad med padding-slot-disciplin + ny Worker-host + DI-utvidgning + ADR 0032-sync-skrivlast-koordinerings-resonemang. B:s blast-radius är ojämförligt större för noll-värde vid given kadens.
- **ADR 0032-precedens (sync-skrivlast vs pg_trgm-avvisning):** ADR 0032 etablerade att ny dynamisk extern skrivlast-yta måste motiveras mot konkret nytta, inte införas spekulativt. Variant B introducerar exakt den typ av extern skrivlast-automation precedensen är skeptisk mot, utan färskhets-signal som motiverar den.
- **DRY-spänning (Hunt/Thomas 1999):** En andra dynamisk extern JobTech-integration (utöver `IJobSource`-sök-snapshot-vägen) för marginell nytta speglar exakt det DRY/YAGNI-skäl jag redan avvisade B-frontend-konstanten på i FRÅGA 1.

### Avvisade alternativ

**Variant B (synkad cron-tabell):** Avvisad. Auto-färskhet är den enda vinsten och den adresserar ett icke-existerande problem vid kvartalskadens. Maximal blast-radius mot noll bevisat behov. Detta är en snabblösning förklädd som "robusthet" — den löser inte ett problem, den lägger till en underhållsyta. Bryter YAGNI, KISS och egen FRÅGA 1-precedens.

**Variant C (hybrid: seed + opt-in admin-refresh-knapp):** Avvisad. Lägger till en auth-gated admin-endpoint + test-yta + säkerhetsyta för en knapp ingen trycker på kvartalsdata. C kombinerar A:s enkelhet med B:s ytkostnad utan att lösa något A inte redan löser. Detta är "speculative generality med disclaimer" — fortfarande speculative generality.

### Snapshot-uppdaterings-mekanism (eftersom A vald)

**Manuell regenerering + commit.** En utvecklare kör ett off-repo engångs-script (eller manuell fetch) som producerar `taxonomy-snapshot.json`, committar filen, deployar. Seedern upsertar idempotent vid app-start. **Inte build-tids-fetch:** build-tids-extern-anrop bryter hermetisk build (Software Engineering at Google 2020, kap. 18 "Build Systems" — builds ska vara deterministiska och hermetiska; nätverksberoende i build är en känd anti-pattern) och återinför precis den externa-yta-på-en-kritisk-väg som Approach A finns för att eliminera. Snapshot-filen är en versionerad artefakt under granskning som vilken annan committad referensdata som helst.

**Graceful degradation vid stale snapshot:** okänt concept-id i en sparad sökning → reverse-lookup-fallback `"Okänd kod (<id>)"` (skiss §2 port-skiss, §4). Ingen krasch, ingen data-migration. Detta är medveten resiliens, inte en brist.

### Skala-trigger för framtida Variant B (ej TD — §9.6)

Variant B revisitas **endast** vid en av dessa signaler (skala-trigger, ej tidsbunden TD, ej tracked dump — CLAUDE.md §9.6 anti-pattern):

1. **Färskhets-incident:** dokumenterad användar-rapporterad mismatch mellan JobbPilot-taxonomi och Platsbanken pga stale snapshot (dvs. degradationen blir faktiskt synlig, inte hypotetisk).
2. **Kadens-skifte:** JobTech ändrar taxonomi-publiceringskadens från kvartal/månad till vecka/dag (web-verifierbart — taxonomy.api.jobtechdev.se release-kadens).
3. **Operationell börda:** manuell regenerering visar sig kräva > en regenerering/månad i praktiken (dvs. KISS-antagandet falsifieras empiriskt).

Ingen av dessa skrivs som TD nu (CLAUDE.md §9.6: ingen saknad funktion-dependency, ingen annan fas — det hör till sök-ytan = Fas 2:s domän och är ett medvetet uppskjutet skala-beslut, inte uppskjutet arbete). Dokumenteras som skala-trigger i ADR 0043-brödtexten under "Konsekvenser / Framtida revision".

### Klas strategisk GO krävs: NEJ

Variant A ligger inom det redan GO:ade Approach A + ADR 0043-mandatet. Persistens-formen är ett rent design-delbeslut, ingen ny strategisk yta, ingen ny extern integration, ingen fas-transition. CC kör direkt.

---

## MAP-2 — Port (`ITaxonomyReadModel`) vs `DbSet<TaxonomyConcept>` på `IAppDbContext`

> ADR-granularitet (ny ADR 0043 vs ADR 0042-amendment) är **redan låst av Klas = ny ADR 0043**. CTO avgör EJ den delen. Nedan avgörs endast port-vs-DbSet.

### Beslut: `ITaxonomyReadModel`-Application-port som äger EF internt i Infrastructure. `IAppDbContext` växer INTE med `DbSet<TaxonomyConcept>`.

### Motivering mot principer

- **CLAUDE.md §5.1 + §2.1 (ingen EF-entity över Application-gränsen; Application definierar interfaces Infrastructure implementerar):** `TaxonomyConcept` är en Infrastructure-intern snapshot-entity. Att exponera den som `DbSet<TaxonomyConcept>` på `IAppDbContext` lyfter en ren persistens-replika av extern referensdata över Application-gränsen. Porten returnerar Application-DTOs (`record class`, §3.3); EF-querien lever i `TaxonomyReadModel : ITaxonomyReadModel` i Infrastructure.
- **`IAppDbContext`-kontraktets uttalade syfte (IAppDbContext.cs rad 12-16 + ADR 0009):** `IAppDbContext` är dokumenterat som "DbSet&lt;T&gt; **per aggregate root**" — en medveten, avgränsad kompromiss (ADR 0009) som ersätter repository-pattern *för aggregat*. `TaxonomyConcept` är **inget aggregate root** — det har inga invarianter, ingen state-övergång, ingen domän-identitet. Att lägga in det i `IAppDbContext` urvattnar kontraktets uttalade invariant (en DbSet = ett aggregat) och öppnar dörren för att vilken Infrastructure-tabell som helst smyger in över gränsen. Det vore en regression mot ADR 0009:s avgränsning.
- **ISP (Martin 2017, kap. 10 "Interface Segregation Principle"):** Aggregate-handlers som injicerar `IAppDbContext` ska inte tvingas se en read-model-tabell de aldrig rör. Porten håller `IAppDbContext` smal och fokuserad.
- **Evans 2003 (read-model / *Domain-Driven Design* kap. 14 Anticorruption Layer):** Snapshotten ÄR ACL:n materialiserad — JobTechs ubiquitous language replikerad lokalt. ACL-data ska per definition vara inkapslad bakom en explicit översättnings-port, inte exponerad rått som DbSet. Detta är den kanoniska ACL-formen och speglar exakt det redan etablerade `IJobSource`-mönstret (Application-port, `internal` Infrastructure-impl, DTO över gränsen — IJobSource.cs rad 8-15).
- **SPOT (Hunt/Thomas 1999, DRY):** Namn↔concept-id-översättnings-logiken (inkl. reverse-lookup-fallback `"Okänd kod"`) får en single point of truth i `TaxonomyReadModel`. Spridd ut över handlers via rå DbSet vore duplicerad översättnings-kunskap.

### Avvisat alternativ

**`DbSet<TaxonomyConcept>` på `IAppDbContext`:** Avvisad. Snabbare (ingen port-fil) men bryter §5.1, urvattnar ADR 0009:s aggregate-per-DbSet-invariant, bryter ISP, och bryter den ACL-inkapslings-princip som hela Approach A vilar på. Mastercard-testet (CLAUDE.md §1): en utomstående arkitekt som ser en rå referensdata-tabell-DbSet bredvid `JobAds`/`Resumes`-aggregaten i kontraktet noterar det direkt som en lager-läcka. Port-varianten är den en granskare blir imponerad av.

### Trade-offs accepterade

En extra fil (`ITaxonomyReadModel.cs` i Application/JobAds/Abstractions) + en impl-fil i Infrastructure jämfört med rå DbSet. Acceptabelt — fil-antal är ingen design-axel. Inkapsling och kontrakts-renhet är design-värde; fil-count är det inte.

### ADR 0043-konsekvens

ADR 0043 ska i brödtexten fastslå: ACL:n materialiseras som Infrastructure-intern snapshot-entity (`TaxonomyConcept`) bakom Application-porten `ITaxonomyReadModel`. `IAppDbContext` utökas **inte**. Motiv: ADR 0009 aggregate-per-DbSet-invariant + §5.1 + Evans kap. 14 ACL-inkapsling + IJobSource-precedens. Detta är ett ACL-arkitekturbeslut och hör i ADR-brödtexten.

### Klas strategisk GO krävs: NEJ

Rent lager-design inom redan GO:at ACL-mandat. Bekräftar dotnet-architect-skissens egen "hellre inte DbSet"-lutning (skiss §5 rad 314-318) — ingen strategisk ny yta.

---

## MAP-3 — DoS-disciplin + cache (security-auditor BLOCKING-input)

### Beslut 3a — Rate-limit-policy: ny dedikerad policy `TaxonomyReadPolicy`

**Ny dedikerad policy**, partitionerad per UserId (claim `sub`), `NoLimiter` för anonym (RequireAuthorization returnerar 401 före endpoint — samma mönster som `SuggestPolicy`/`ListReadPolicy`). Parametrar `IOptions`-bundna i `RateLimitingOptions` (CLAUDE.md §5.1 — ingen hårdkodning).

**Riktvärde (security-auditor verifierar/justerar — BLOCKING):** `PermitLimit = 20`, `WindowSeconds = 60` (20/min/user). Motivering: picker-trädet hämtas vid sid-laddning + ev. en reverse-lookup-batch per sparad-sökning-render. Med ETag (Beslut 3c) blir de flesta anrop `304 Not Modified`. 20/min ger generöst headroom över legitim användning (typiskt 1-3 hämtningar/sid-besök) utan att öppna ett scan-fönster mot snapshot-tabellen. Lägre än `ListRead` (60/min) eftersom legitim-frekvensprofilen är strukturellt lägre (statiskt träd, inte scrollande filtrering).

**Motivering mot principer:**

- **Least common mechanism (Saltzer/Schroeder 1975, "The Protection of Information in Computer Systems", princip 8):** Detta är den exakt princip som redan motiverade `SuggestPolicy` att INTE återanvända `ListReadPolicy` (RateLimitingOptions.cs rad 85-94). Picker-trädet och reverse-lookup har en **annan legitim-frekvensprofil** än både list/search (scrollande filtrering, 60/min) och typeahead (per-keystroke, 30/10s). Att dela skyddsbudget mellan ytor med olika frekvensprofil betyder att strypning av en svälter de andra. Konsekvent tillämpning av redan-etablerad precedens i samma kodbas — inte ny princip.
- **REP (Martin 2017, kap. 13):** Policyn är generisk nog att återanvändas av framtida statisk-referensdata-läs-endpoints (samma resonemang `ListReadPolicy` dokumenterar för list/search-familjen). Namnges `TaxonomyReadPolicy` men kategorin är "bounded static reference read".

**Avvisat — återanvänd `ListReadPolicy`:** YAGNI skulle argumentera för återanvändning, men kodbasens egen `SuggestPolicy`-precedens fastslog redan att least-common-mechanism vinner över YAGNI när frekvensprofilerna skiljer. Att nu återanvända `ListRead` vore intern inkonsekvens mot 2026-05-16-beslutet. Avvisad.

**Avvisat — återanvänd `SuggestPolicy`:** `SuggestPolicy` (30/10s) är kalibrerad för per-keystroke-typeahead. Picker-trädet är inte per-keystroke. Fel frekvensmodell. Avvisad.

### Beslut 3b — Reverse-lookup concept-id-cap: JA, cap = `SearchCriteria.MaxConceptIds` (= 10), per kind

Reverse-lookup-operationen (concept-id-lista → namn) **cappas i `GetTaxonomyTreeQueryValidator`** (FluentValidation, FÖRE handler — speglar `SuggestJobAdTermsQueryValidator`-mönstret exakt). Cap = **`SearchCriteria.MaxConceptIds` (=10) per dimension** (ssyk-lista ≤10, region-lista ≤10), refererad som konstant — **inte** hårdkodad `10`.

**Motivering mot principer:**

- **Domän-konsekvens (DRY, Hunt/Thomas 1999 — ett knowledge piece):** En sparad sökning kan per domän-invariant (SearchCriteria.cs rad 39, 68-76) aldrig bära fler än `MaxConceptIds` concept-id per dimension. Reverse-lookup ska aldrig ombes lösa fler än domänen tillåter att existera. Att referera konstanten (inte duplicera siffran 10) håller domän-cap och query-cap synkade till en sanningskälla. Om domänen någonsin höjer `MaxConceptIds` följer query-cap automatiskt.
- **OWASP API4:2023 "Unrestricted Resource Consumption" + CWE-400:** Samma DoS-disciplin-familj som ADR 0042 Beslut C / `SuggestJobAdTermsQueryValidator`. Cap i Validation-pipeline = query körs aldrig med över-cap input (fail-fast 400, ingen DB-touch).
- **Defense in depth (Saltzer/Schroeder, princip 5 — "complete mediation"):** Caps i validatorn även om frontend "borde" aldrig skicka mer — Application-gränsen litar inte på klienten.

**Hela-trädet-hämtningen:** ingen användarstyrd `Take`/`Skip` (skiss §3 — trädet är fast storlek ~21 län + ~290 kommuner + ~30 yrkesområden + några hundra yrken). Valfri `kind`-diskriminator (region-träd vs yrkes-träd) är en enum/sluten värdemängd, inte fritext → ingen blowup-vektor. Om fritext-filter på pickern införs senare: cap längd analogt `SuggestJobAdTermsQueryValidator` (MinimumLength/MaximumLength) — men det är inte i detta scope (YAGNI; lägg inte filter-parametern förrän pickern faktiskt behöver den).

### Beslut 3c — Cache: ETag + `Cache-Control: private` PÅ endpointen, in-memory bakom porten. Båda.

- **HTTP `ETag` + `Cache-Control: private, max-age=<kort>`** på taxonomi-endpointen. ETag = hash/version av snapshotten (t.ex. snapshot-fil-hash eller seedad version-kolumn). Svaret är deterministiskt och identiskt för alla användare → stark validator. Frontend `If-None-Match` → `304` utan body. **`private`, inte `public`:** endpointen är auth-gated (JobAdsEndpoints.cs rad 18-20 — gruppen har `.RequireAuthorization()`). `public`/shared-proxy-cache på auth-gated svar är en cache-poisoning/cross-user-läckage-vektor (OWASP — Web Cache Deception/Poisoning). `private` instruerar att endast användarens egen browser-cache lagrar. Trädet är visserligen icke-känsligt (publik referensdata), men `private` är rätt default-disciplin på allt bakom auth-gate; vi förlitar oss inte på "datat är ändå inte hemligt".
  - **Ingen `Vary`-fälla:** svaret är användar-oberoende men endpointen är auth-gated. `Cache-Control: private` är tillräckligt; lägg inte `public` + `Vary: Authorization` (skör mot proxy-implementationer, onödig komplexitet — KISS).
- **In-memory-cache i Infrastructure (`IMemoryCache`) bakom `ITaxonomyReadModel`:** snapshotten läses från DB en gång per process-livstid och hålls i minne (~600 rader, trivial). Invalidering: process-restart efter deploy (Variant A: ny deploy = ny seed = ny process = ny cache; ingen runtime-invalidering behövs eftersom snapshotten bara ändras via deploy). Detta eliminerar DB-touch helt på den varma vägen → även om rate-limit-fönstret skulle nås är resurskostnaden per anrop nära noll.

**Motivering mot principer:**

- **KISS (Beck):** Eftersom Variant A valdes (MAP-1) ändras snapshotten **endast vid deploy**. Det gör cache-invalidering trivial (process-livstid = cache-livstid) — ingen invaliderings-pubsub, ingen TTL-tuning, ingen cache-koherens-komplexitet. Variant B hade krävt cache-invalidering vid sync-job → ytterligare ett argument för att MAP-1=A var rätt; besluten förstärker varandra.
- **Defense in depth + resurs-DoS-mitigering (OWASP API4:2023):** ETag (nätverks-/klient-nivå) + in-memory (server-/DB-nivå) är två oberoende lager. Rate-limit (Beslut 3a) är det tredje. Tre lager mot resurskonsumtion på en statisk-data-endpoint är proportionerligt, inte over-engineering, eftersom varje lager är billigt (ETag = en header, in-memory = en `IMemoryCache.GetOrCreate`, rate-limit = befintlig mekanism).
- **Cache-säkerhet (Saltzer/Schroeder complete mediation; OWASP Web Cache Deception):** `private` på auth-gated endpoint är icke-förhandlingsbar disciplin oavsett datakänslighet.

**Avvisat — ingen cache:** Avvisad. ~600 rader är litet men en DB-rundtur per picker-render utan ETag är onödig last och saknar den `304`-snabbväg frontend bör få. "Datat är litet" är inget skäl att hoppa över gratis caching på deterministisk statisk data.

**Avvisat — endast ETag (ingen in-memory):** Avvisad. ETag sparar bandbredd men varje icke-`304`-anrop (cache-miss, ny klient, cleared cache) träffar DB. In-memory eliminerar DB-touchen helt för trivial kostnad. Båda lagren adresserar olika miss-scenarier.

**Avvisat — endast in-memory (ingen ETag):** Avvisad. Utan ETag skickas hela trädet i body vid varje anrop även när klienten redan har det. ETag ger `304`-snabbvägen. Komplementära, inte alternativ.

### Klas strategisk GO krävs: NEJ

MAP-3 är DoS/cache-disciplin inom redan GO:at mandat och speglar exakt etablerade precedens (`SuggestPolicy` least-common-mechanism, `SuggestJobAdTermsQueryValidator` cap-i-pipeline, ADR 0042 Beslut C). **security-auditor BLOCKING gäller fortfarande** — auditorn verifierar/justerar de konkreta rate-limit-talen (20/60s riktvärde), `private`-cache-direktivet, ETag-deriveringen och reverse-lookup-cap:en innan commit (skiss §5 steg 5, oförändrat). Detta är input till auditorn, inte ett kringgående av auditorns veto.

---

## ADR 0043-konsekvens — vad som ska in i brödtexten

ADR 0043 "Taxonomi-ACL för sök-ytan" (ny ADR — Klas-låst granularitet, ej amendment) ska i **Beslut**-sektionen fastslå:

1. **Persistens (MAP-1):** Lokal committad snapshot (`taxonomy-snapshot.json`) seedad via idempotent seeder till fristående tabell. Ingen runtime-extern. Ingen cron. Manuell regenerering + commit som färskhets-kadens. Motiv: YAGNI/KISS + kvartalskadens + blast-radius (Ford/Parsons/Kua) + ADR 0032 sync-skrivlast-precedens. **Skala-trigger** för framtida Variant B (färskhets-incident / kadens-skifte / operationell börda) dokumenteras under "Konsekvenser / Framtida revision" — ej TD.
2. **ACL-inkapsling (MAP-2):** `ITaxonomyReadModel`-Application-port; `TaxonomyConcept` Infrastructure-intern; `IAppDbContext` utökas EJ. Motiv: ADR 0009 aggregate-per-DbSet-invariant + §5.1 + Evans kap. 14 ACL + IJobSource-precedens.
3. **DoS/cache-disciplin (MAP-3):** Dedikerad `TaxonomyReadPolicy` (least common mechanism, Saltzer/Schroeder); reverse-lookup-cap = `SearchCriteria.MaxConceptIds`; ETag + `Cache-Control: private` + in-memory bakom porten. security-auditor verifierar konkreta tal.
4. **Relation till ADR 0042:** Beslut B-domänkontrakt (`IReadOnlyList<string>` concept-id) **oförändrat**; Beslut C-typeahead-arkitektur **oförändrad**; ADR 0042 rad 21-constraint ("inget externt taxonomi-API på sök-vägen") **uppfyllt, inte brutet** (lokal snapshot är per definition inte på sök-vägen). Cross-ref ADR 0032 (sync-skrivlast-precedens som stöder MAP-1=A) + ADR 0009 (aggregate-per-DbSet, stöder MAP-2).
5. **Domain orörd:** `SearchCriteria` VO oförändrad; ingen ny Domain-typ; ACL medvetet utanför Domain (taxonomi ≠ JobbPilots ubiquitous language, Evans kap. 14). Ingen migration på `saved_searches`.

---

## TL;DR

| MAP | Beslut | Klas strategisk GO? |
|---|---|---|
| **MAP-1** persistens | **Variant A** — committad JSON-snapshot + idempotent seeder, manuell regenerering. Ej HasData. Skala-trigger (ej TD) för framtida B. | NEJ |
| **MAP-2** port-vs-DbSet | **`ITaxonomyReadModel`-port**, `TaxonomyConcept` Infrastructure-intern, `IAppDbContext` utökas EJ | NEJ |
| **MAP-3** DoS+cache | **Ny `TaxonomyReadPolicy`** (20/60s riktvärde) + reverse-lookup-cap = `SearchCriteria.MaxConceptIds` + **ETag + `Cache-Control: private` + in-memory** (båda). security-auditor verifierar tal (BLOCKING kvarstår). | NEJ |

Inga TD-lyft (CLAUDE.md §9.6 — hör till sök-ytan = Fas 2:s domän, ingen saknad funktion-dependency). Skala-trigger ≠ TD. Implementation kör non-stop direkt på dessa beslut per redan givet Klas-GO på Approach A + ADR 0043 + autonomt non-stop. Inget delbeslut överraskar med separat Klas-GO-krav.

**Referenser:** Beck (XP, YAGNI); Fowler 2018 *Refactoring* kap. 3 (Speculative Generality); Ford/Parsons/Kua 2017 *Building Evolutionary Architectures* kap. 4 (blast-radius); Winters/Manshreck/Wright 2020 *Software Engineering at Google* kap. 18 (hermetiska builds); Martin 2017 *Clean Architecture* kap. 10 (ISP), kap. 13 (REP/CCP); Evans 2003 *DDD* kap. 14 (Anticorruption Layer / read-model); Hunt/Thomas 1999 (DRY/SPOT); Saltzer/Schroeder 1975 (least common mechanism, complete mediation, defense in depth); OWASP API4:2023 (Unrestricted Resource Consumption), CWE-400, OWASP Web Cache Deception. CLAUDE.md §2.1/§3.3/§5.1/§9.6; ADR 0009 (aggregate-per-DbSet), ADR 0032 (sync-skrivlast), ADR 0042 (rad 21, Beslut B/C); docs/reviews/2026-05-17-soktyta-platsbanken-cto.md (Approach A-precedens); docs/reviews/2026-05-17-fynd2-taxonomi-acl-architect.md (skiss MAP-1/2/3). Kod-precedens: `IJobSource.cs`, `SuggestJobAdTermsQueryHandler/Validator.cs`, `RateLimitingExtensions.cs` (SuggestPolicy rad 170-190), `RateLimitingOptions.cs` (rad 85-99), `IAppDbContext.cs` (rad 12-16), `SearchCriteria.cs` (rad 39, 68-76), `JobAdsEndpoints.cs` (rad 18-59).

---

## Scope-fork 2026-05-17 (Kommun-nivå)

**Kontext:** Implementation-discovery verifierade JobAdConfiguration.cs rad 74-80 + F2P9-migration: `ssyk_concept_id` = occupation-name-nivå (ej ssyk-4, ej occupation-field), `region_concept_id` = region/län-nivå. Ingen `municipality_concept_id`-kolumn. JobAdSearch.ApplyCriteria filtrerar enbart dessa två shadow-props. Låst design garanterade "shadow-prop ORÖRD". Platsbankens Län→Kommun-hierarki kan ej levereras inom låst design utan ny STORED generated column + index + ApplyCriteria-utökning + migration + ADR-justering — och `municipality_concept_id`:s existens i Platsbanken-payloaden är overifierad.

### Beslut: Variant A — leverera inom låst design nu

Ort = **Län**-namn-väljare (enkelnivå). Yrke = **Yrkesområde→Yrke** hierarkisk namn-väljare. concept-id försvinner ur UI (kärnmålet uppfyllt). Shadow-prop-filtrering ORÖRD (garantin bevarad). Kommun-granularitet = payload-verifierings-trigger i ADR 0043-brödtexten, ej TD, ej Variant B nu.

### Motivering mot principer

- **YAGNI/KISS (Beck; Fowler 2018, Refactoring kap. 3 Speculative Generality):** Variant B bygger en hel filtrerings-dimension ovanpå ett fält vars existens i payloaden är overifierad. Spekulativ generalitet på obekräftad datagrund.
- **Kärnmål uppfyllt:** Fynd 2/ADR 0043-målet = concept-id ur UI + ACL materialiserad. Variant A levererar det fullt. Kommun-vs-Län är granularitets-axel, ej ACL-mål.
- **Blast-radius (Ford/Parsons/Kua 2017 kap. 4):** Variant B rör ADR 0042 rad 21 + ADR 0032-migrationsyta + bryter "shadow-prop ORÖRD"-garantin — oberäknad expansion av tre avgränsade ADR-ytor i autonom batch på overifierad datagrund. Variant A bevarar varje fixpunkt orörd.
- **Mastercard-test (CLAUDE.md §1):** Granskare ser ogrundad schema-mutation i B; ser disciplin i A (verifierat levererat, overifierat dokumenterat som trigger).
- **§9.6 fas-regel:** Ej TD — overifierad extern datakälle-dependency, ej spårbart kod-arbete. Payload-verifierings-trigger i ADR-brödtext är rätt instrument (samma klass som MAP-1 skala-trigger).

### Avvisade alternativ

**Variant B:** Avvisad. Overifierad datagrund (KISS-brott) + maximal blast-radius mot tre avgränsade ADR-ytor i autonom batch + bryter "shadow-prop ORÖRD"-garantin. Scope creep förklädd som fullständighet. Förgrenad verifiera-sedan-villkorligt-bygg-väg kan ej diff-granskas rent (§6.3 punkt 4).

**Variant C (stub/blockera):** Avvisad. Blockering straffar verifierat-färdig civic-vinst för axel utanför kärnmål; stub utan data = död persistens-yta.

### Trade-offs accepterade

Ort = länsnivå, ej Län→Kommun-hierarki, i denna leverans. Platsbanken-paritet på länsnivå; civic-vinst (namn ej concept-id) 100% levererad; Kommun ej förlorad utan verifieringsgated uppföljning.

### Klas strategisk GO krävs: NEJ

Variant A inskränker scope till det redan GO:ade/låsta (shadow-prop ORÖRD bevaras exakt). Forken hade krävt Klas-GO endast om Variant B valts. A vald → CC kör direkt. Klas-override möjlig men medveten.

### ADR 0043-brödtext-konsekvens

1. **Beslut, punkt 4-tillägg:** Sök-yta-granularitet i denna leverans = Län (region_concept_id) + Yrke (ssyk_concept_id occupation-name-nivå via Yrkesområde→Yrke). Shadow-prop-filtrering (JobAdConfiguration.cs rad 74-80, JobAdSearch.ApplyCriteria) oförändrad — ACL = namn↔concept-id-översättning ovanpå befintliga shadow-props, ej ny filtrerings-dimension. ADR 0042 rad 21 + "shadow-prop ORÖRD"-garanti uppfyllda, ej brutna.
2. **Konsekvenser / Framtida revision-tillägg:** Payload-verifierings-trigger för Kommun-granularitet. Län→Kommun ej levererad: `municipality_concept_id` finns ej som shadow-kolumn + existens i Platsbanken-payload overifierad. Revisitas endast vid (i) verifierat att fältet finns via raw_payload-prov-discovery OCH (ii) användarsignal om länsnivå otillräcklig. Vid trigger: separat förhandlad batch — ej autonom (bryter orörd-garanti + rör ADR 0032/0042). Ej TD (§9.6).

### Justerad implementation-sekvens

Architect-skissens sekvens (skiss §5) oförändrad. Förtydligande: snapshot + ITaxonomyReadModel-port bär dimensionerna region (län) + occupation-field→occupation-name (yrkesområde→yrke). Ingen municipality-nod i snapshot-trädet, ingen kommun-kind-gren. Reverse-lookup-fallback ("Okänd kod (<id>)") per dimension. Inga nya filer/migrationer utöver MAP-1/2/3.

---

## Defekt-triage 2026-05-17 (#1 datamodell, #3 startup-orkestrering)

**Kontext:** test-writer TDD-Red avslöjade tre prod-defekter. #2 (validator-NRE) in-block-fixad av CC (FluentValidation Cascade.Stop, entydigt §9.6). #1 + #3 eskalerade till senior-cto-advisor.

### Defekt #1 (Major) — taxonomin är en graf, inte ett träd
**Beslut: Variant C i snapshot-GENERATORN.** Committad taxonomy-snapshot.json kanoniskt dedupliserad (varje occupation-name under exakt ett primärt/första occupation-field, deterministisk regel = fält sorterade på conceptId). TaxonomyConcept (ConceptId PK), MapRows, TaxonomyReadModel, EF-config, migration ORÖRDA. Noll committad kodändring. Motiv: Evans kap.14 (ACL isolerar från extern modell-komplexitet, ej replikerar) + YAGNI/KISS (multi-membership osynligt — filtrering på shadow-prop per Beslut E, ej pickerns gren) + blast-radius (noll kod vs Variant A river migration+entity+readmodel) + DRY (LoadAsync rad 86-88 har redan ConceptId-dedupe). Avvisat: A (edge-tabell, ACL-missförstånd, max blast-radius), B (composite PK Kind-beroende + sentinel = modell-grumlighet Beslut C valde bort), C-i-MapRows (sprider dedupe). Trade-off: yrke visas endast under primärt yrkesområde i pickern — NOLL sökresultatpåverkan (shadow-prop-filtrering), typeahead primär upptäcktsväg, reverse-lookup parent-oberoende. Klas-GO: NEJ (inom Approach A + Beslut B-mandat).

### Defekt #3 (Major) — TaxonomySnapshotSeeder bryter 9 cold-start-fixturer
**Beslut: Variant A — prod-startup-fixturer plockar bort TaxonomySnapshotSeeder ur DI, speglat mot IdempotentAdminRoleSeeder. Ingen prod-kodsändring. Ansvarig CC.** Motiv: konsekvent etablerad precedens (ProductionStartupFactory/UseHttpsRedirectionGate har exakt removal-mekanism för admin-seedern); fail-loud-prod-kontraktet (42P01-grace gated Dev/Test) är KORREKT (CLAUDE.md §3.4/§5.1) — testet anpassas till prod, ej tvärtom; rotorsak = fixtur-paritetsskuld ej prod-defekt. Avvisat: B (bredda grace till prod = tyst seed-miss, picker permanent tom utan larm — snabblösning fel riktning), C (migrera AppDbContext ändrar vad fixturerna testar). CC-rek in-block-refaktor: delad RemoveStartupSeeders()-extension (DRY, ≥4 duplikat). Bevara IdempotentAdminRoleSeederProdBubbleTests ORÖRD. Ny TaxonomySnapshotSeederProdBubbleTests = test-writer-scope samma batch. Klas-GO: NEJ.

Båda in-block i Fynd 2-batchen (§9.6). Inga TD-lyft. Ingen ADR Accepted-flip.

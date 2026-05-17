# Backend-arkitektur-design — Fynd 2: Taxonomi-ACL för sök-ytan

**Datum:** 2026-05-17
**Agent:** dotnet-architect (read-only, design-skiss — INGEN kod)
**Uppdrag:** Designa Taxonomi-ACL (kommande ADR 0043) per CTO Approach A.
Klas produktägar-GO på Approach A finns (docs/reviews/2026-05-17-soktyta-platsbanken-cto.md).
**Status:** Design-skiss + multi-approach-identifiering för senior-cto-advisor-rond.
**Constraint (icke-förhandlingsbart):** Domänform OFÖRÄNDRAD —
`SearchCriteria.Ssyk/Region` förblir `IReadOnlyList<string>` concept-id.
ALDRIG extern API på sök-vägen (ADR 0042 rad 21 + ADR 0042 Beslut C).

---

## Arkitektur-analys

### Sammanfattning

Designen är ren mot Clean Arch om snabb-snapshot-persistensen hålls i
Infrastructure bakom en Application-port, picker-hierarki-querien följer
det befintliga `SuggestJobAdTerms`-mönstret (Mediator query + validator +
rate-limit-policy), och Domain inte rörs alls. Den befintliga
shadow-prop-filtreringen i `JobAdSearch.ApplyCriteria` är **orörd** — den
opererar redan på concept-id och vet inget om namn. Tre arkitekturella
beslutspunkter kräver senior-cto-advisor-avgörande (markerade **MAP-1/2/3**).

---

## 1. Snapshot-persistens — MULTI-APPROACH (MAP-1, CTO decision-maker)

CTO Approach A låser "lokal taxonomi-snapshot + ACL". Kvarvarande öppen fråga:
**hur** snapshotten persisteras och fylls. CC ger ingen egen rekommendation —
detta är ett multi-approach-val (memory `feedback_cto_decides_multi_approach`).
Jag presenterar varianterna med Clean Arch-konsekvens och blast-radius;
**senior-cto-advisor är decision-maker**.

### Variant A — Seedad tabell (committad JSON-snapshot → EF-seed/migration)

- En committad `taxonomy-snapshot.json` i Infrastructure (region/municipality/
  occupation-field/occupation + relationer), laddas via EF `HasData` eller
  idempotent seeder (`IHostedService`-mönster, jfr `IdempotentAdminRoleSeeder`).
- **Ingen runtime-extern-hop någonsin** — referensdata är committad artefakt.
- **Clean Arch:** ren. JSON-fil + seeder ligger i Infrastructure. Application
  ser bara `ITaxonomyReadModel`-porten.
- **Färskhet:** uppdateras genom att en utvecklare regenererar JSON-filen
  (manuellt script / engångs-fetch off-repo) och committar. Taxonomi ändras
  månads-/kvartalsvis (web-verifierat 2026-05-17) → mänsklig kadens räcker.
- **Blast-radius:** minimal. En migration (tabell + seed) eller seeder-host.
  Ingen ny extern-yta, ingen ADR 0032-sync-konflikt, ingen ny HTTP-klient.
- **YAGNI/KISS (Beck; Fowler 2018):** bounded, långsamt föränderlig
  referensdata → simplaste mekanism som fungerar. Speglar CTO-motiveringen
  rad 22 ("taxonomi är referensdata, ändras månads-/kvartalsvis").
- **Nackdel:** snapshot-färskhet är en commit-disciplin, inte automatiserad.
  Stale data om ingen regenererar (mitigeras: taxonomin är extremt stabil;
  okänt concept-id i en sparad sökning degraderar grace­fullt till
  "Okänd kod" reverse-lookup-fallback, ingen krasch).

### Variant B — Synkad tabell (cron-refresh via JobTech-väg)

- Ny `IJobTechTaxonomyClient` (Refit, Infrastructure), ny Hangfire-cron
  (t.ex. månatlig) som hämtar taxonomi och upsertar tabellen.
- **Clean Arch:** ren OM klienten hålls intern i Infrastructure (jfr
  `IJobTechSearchClient` rad 11 — `internal`). Sync-jobbet blir
  Application-orchestrator (jfr `SyncPlatsbankenSnapshotJob`-mönstret).
- **Constraint-status:** uppfyller ADR 0042 rad 21 — fetch är off-path/batch,
  aldrig på sök-vägen. Samma resonemang som CTO C2-avvisning rad 89-90.
- **Blast-radius:** stor. Ny Refit-klient + ny resilience-pipeline + ny
  recurring-job + DI-registrering + ADR 0032-sync-skrivlast-koordinering
  (rate-limiter `_streamRateLimiter` är process-wide 1 req/min — ny
  taxonomi-fetch mot taxonomy.api.jobtechdev.se är ANNAN host, så ingen
  delad limiter-konflikt, MEN ny cron i `RecurringJobRegistrar` med
  padding-slot-disciplin krävs).
- **Färskhet:** automatisk. Overkill för data som ändras kvartalsvis.
- **DRY-not:** introducerar en andra dynamisk extern-integration för marginell
  färskhetsnytta — CTO avvisade redan B-frontend-konstant av DRY-skäl;
  samma YAGNI-spänning gäller cron-automation av kvartalsdata.

### Variant C — Hybrid: seedad baseline + opt-in manuell refresh-job

- Variant A som default (committad seed) + en **on-demand** admin-trigger
  (inte cron) som kan regenerera tabellen från JobTech vid behov.
- Mer rörlig yta än A för låg faktisk nytta (admin trycker sällan en knapp
  för kvartalsdata). Ökar test-/säkerhetsyta (admin-endpoint, auth-gate).

### dotnet-architect observation (ej rekommendation — CTO avgör MAP-1)

Variant A har lägst blast-radius och starkast YAGNI/KISS-linje mot CTO:s egen
motivering (referensdata, kvartalstakt). Variant B:s enda vinst (auto-färskhet)
adresserar ett problem som inte finns vid kvartals-kadens. Constraint
("aldrig extern på sök-vägen") uppfylls av **alla tre** — det är inte en
diskriminator. **Beslut till senior-cto-advisor.**

---

## 2. ACL-placering (Clean Arch) — fast design

Namn↔concept-id-mappning + hierarki är JobTech-domänens ubiquitous language
mappad till JobbPilots presentationsbehov. Detta är **exakt** Evans 2003
kap. 14 Anticorruption Layer — samma princip `JobAdSearch.cs` rad 23-25 redan
respekterar i query-vägen.

### Lager-fördelning

| Artefakt | Lager | Motiv |
|---|---|---|
| `ITaxonomyReadModel` (port) | **Application** (`Application/JobAds/Abstractions/`) | Application definierar interface Infrastructure implementerar (CLAUDE.md §2.1). Speglar `IJobSource`-mönstret. |
| Snapshot-entity + EF-config + tabell/seed | **Infrastructure** (`Infrastructure/Taxonomy/`) | EF Core, Npgsql, JSON-fil = Infrastructure (CLAUDE.md §2.2). |
| `TaxonomyReadModel : ITaxonomyReadModel` | **Infrastructure** | Implementerar porten. EF-query mot snapshot-tabell. |
| Picker-query + handler + validator | **Application** (`Application/JobAds/Queries/GetTaxonomyTree/`) | CQRS-query (CLAUDE.md §2.3). Mediator. |
| Endpoint | **Api** (`JobAdsEndpoints.cs`) | Composition root. |
| **Domain** | **RÖRS INTE** | `SearchCriteria` oförändrad. Ingen ny Domain-typ. ACL är medvetet utanför Domain (taxonomi är inte JobbPilots ubiquitous language — Evans kap. 14). |

### Hur väljarval (concept-id) matar befintliga `SearchCriteria.Create`/`ListJobAdsQuery` UTAN VO-ändring

**Ingen backend-ändring krävs i flödet.** Datakontraktet är redan concept-id
hela vägen:

1. Picker-komponenten (frontend) visar namn, men `onChange` emitterar
   **concept-id** (samma `string[]` som dagens `JobAdMultiSelect` redan
   skickar — se `job-ad-multi-select.tsx` rad 62).
2. `JobAdFilters` pushar concept-id i URL-query (`?ssyk=<id>&region=<id>`) —
   **oförändrat** (`job-ad-filters.tsx` rad 86-87).
3. `JobAdsEndpoints` MapGet binder `string[] ssyk/region` → `ListJobAdsQuery`
   — **oförändrat** (rad 33-41).
4. `ListJobAdsQueryHandler` → `JobAdSearch.ApplyCriteria` filtrerar via
   shadow-props `SsykConceptId`/`RegionConceptId` — **oförändrat**.
5. `SearchCriteria.Create` validerar concept-id-format (regex
   `^[A-Za-z0-9_-]{1,32}$`) — **oförändrat**.

Den enda nya backend-ytan är **läs-vägen för pickern** (hierarki +
reverse-lookup namn↔id). Filtrerings- och persistens-vägen är intakt.

### Bekräftelse: shadow-prop-filtreringen är ORÖRD

`JobAdSearch.ApplyCriteria` (rad 33-68) opererar på `EF.Property<string?>(j,
"SsykConceptId")` mot inkommande concept-id-lista. Den vet inget om namn och
ska fortsätta vara namn-omedveten. ACL:n lever **utanför** query-vägen — den
översätter bara namn↔id i presentations-/inmatningsskiktet (picker-query) och
reverse-lookup för redan-sparade sökningar. **Ingen ändring i `JobAdSearch.cs`,
`JobAdConfiguration.cs` shadow-props, eller F2P9-migrationens generated
columns.**

### Port-skiss (konceptuell — INGEN kod, signatur-form för CTO/impl)

`ITaxonomyReadModel` i Application exponerar två operationer:

- **Hierarki-hämtning:** returnerar regioner med underordnade kommuner, och
  yrkesområden med underordnade yrken, som rena Application-DTOs
  (namn + concept-id + parent-relation). Inga EF-entities över
  Application-gränsen (CLAUDE.md §5.1 — projektion till DTO).
- **Reverse-lookup:** givet en lista concept-id → namn (för att rendera
  redan-sparade sökningar och valda chips). Okänt id → fallback-DTO
  (`Label = "Okänd kod (<id>)"`), aldrig null/throw — graceful degradation
  vid stale snapshot.

DTO:erna är `record class` (CLAUDE.md §3.3), lever i
`Application/JobAds/Queries/GetTaxonomyTree/` eller delad
`Application/JobAds/Abstractions/`.

---

## 3. Picker-data-query — fast design

Ny Mediator-query, **speglar `SuggestJobAdTerms`-mönstret exakt** (tunn
adapter, validator, rate-limit-policy, auth-gated).

### Komponenter

| Fil (ny) | Lager | Mönster-spegling |
|---|---|---|
| `GetTaxonomyTreeQuery.cs` | Application | `SuggestJobAdTermsQuery` (record : IQuery<T>) |
| `GetTaxonomyTreeQueryHandler.cs` | Application | `SuggestJobAdTermsQueryHandler` (tunn adapter mot port) |
| `GetTaxonomyTreeQueryValidator.cs` | Application | `SuggestJobAdTermsQueryValidator` (param-cap) |
| `TaxonomyTreeDto.cs` | Application | `JobAdDto` (record class) |
| Endpoint-rad i `JobAdsEndpoints.cs` | Api | `/suggest`-raden (MapGet + RequireRateLimiting) |

Handlern injicerar `ITaxonomyReadModel` (inte `IAppDbContext` direkt — ACL:n
äger snapshot-läsningen så namn↔id-logiken har en SPOT, Hunt/Thomas DRY;
samma resonemang som `IJobSource`-port-inkapsling). Ingen Npgsql/EF i
Application.

### DoS-/rate-limit-disciplin (som CTO Beslut C — parametriserat, cap, auth-gated)

- **Auth-gated:** endpointen läggs i samma `group` som redan har
  `.RequireAuthorization()` (`JobAdsEndpoints.cs` rad 20).
- **Rate-limit:** dedikerad policy. Datat är litet och statiskt → en
  `ListReadPolicy`-klass-policy räcker; om typeahead-stil per-keystroke-
  filtrering på pickern införs senare → överväg `SuggestPolicy`. **Öppen
  fråga MAP-3 nedan** (vilken policy).
- **Parametrisering/cap:** querien tar antingen ingen parameter (hela trädet,
  ~21 län + ~290 kommuner + ~30 yrkesområden + några hundra yrken = bounded,
  statiskt) eller en `kind`-diskriminator (region-träd vs yrkes-träd).
  Validator cap:ar ev. fritext-filter-längd analogt
  `SuggestJobAdTermsQueryValidator`. Ingen användarstyrd `Take`/`Skip` som
  kan blåsa upp queryn — trädet är fast storlek.
- **Reverse-lookup-query:** cap antal concept-id per anrop (spegla
  `SearchCriteria.MaxConceptIds = 10`-disciplinen så en sparad sökning aldrig
  ber om fler än domänen tillåter).

### Cache/ETag — JA, detta är statiskt nog (MAP-3 delfråga)

Taxonomi-trädet är read-only, ändras kvartalsvis, identiskt för alla
användare. Starka cache-kandidater:

- **HTTP `ETag` + `Cache-Control`** på endpointen (svaret är deterministiskt;
  ETag kan vara snapshot-version/hash). Frontend slipper re-hämta per render.
- **In-memory-cache i Infrastructure** (`IMemoryCache`) bakom porten —
  snapshot läses en gång, hålls i process. Invalideras vid seed-byte
  (Variant A: app-restart efter deploy; Variant B: efter sync-job).

Konkret cache-mekanism (ETag vs server-memory vs båda) + auth-interaktion
(privat cache, ingen shared-proxy-cache eftersom endpointen är auth-gated)
är **MAP-3 för CTO/security-auditor**.

---

## 4. Bakåtkompatibilitet — verifierad, ingen migrations-påverkan på SavedSearch

- `saved_searches.criteria` är jsonb via `SearchCriteriaConversion.Converter`
  (`SavedSearchConfiguration.cs` rad 37-42). VO-formen
  (`IReadOnlyList<string>` concept-id) är **oförändrad** → converter,
  comparer, jsonb-shape, dedupe-invarianter är alla intakta.
- **Ingen ny migration på `saved_searches`.** Den enda nya migrationen rör
  taxonomi-snapshot-tabellen (Variant A/B) — en helt fristående tabell utan
  FK till job_ads eller saved_searches (concept-id är en lös referens, inte
  en DB-relation — medvetet, ACL-data är replika av extern taxonomi).
- **Frontend reverse-lookup:** redan-sparade sökningar bär concept-id. För
  att rendera namn anropar frontend reverse-lookup-operationen på
  `ITaxonomyReadModel` (via picker-query/endpoint). Okänt id (taxonomi-drift,
  borttagen kod) → `"Okänd kod (<id>)"`-fallback. Sökningen FUNGERAR fortfarande
  (filtrering sker på rå concept-id mot shadow-props — namnet är ren
  presentation). Detta är medveten graceful degradation, ingen data-migration,
  ingen invariant-risk.
- **`SearchCriteria` jsonb-dedupe (ADR 0042 Beslut B.1):** orörd — dedupe
  vilar på concept-id-sekvenslikhet, namn ingår aldrig i VO:t.

---

## 5. Implementations-sekvens + agenter (vid CTO-beslut på MAP-1/2/3)

Konkret ordning. DI-registrering i **samma commit** som handlers/port-impl
(memory `feedback_di_with_handlers_same_commit` — pre-push fångar inte
broken-DI-state, CI gör).

1. **senior-cto-advisor-rond** — avgör MAP-1 (snapshot-persistens A/B/C),
   MAP-2 (ADR 0043 vs ADR 0042-amendment — CTO rad 28/50 säger Klas avgör
   granularitet; bekräfta), MAP-3 (rate-limit-policy + cache-mekanism).
   **BLOCKING — ingen kod innan CTO-beslut.**
2. **db-migration-writer** (om MAP-1 = Variant A eller B → tabell behövs):
   snapshot-tabell-migration. Fristående tabell, ingen FK, ingen
   saved_searches/job_ads-påverkan. Mot ADR 0032-sync-skrivlast: ingen
   konflikt (annan tabell, annan host om Variant B). Migration följer
   `F2P9JobAdSearchColumns`-mönstret (raw SQL endast där fluent-API inte
   räcker).
3. **test-writer FÖRST/TDD** (CLAUDE.md §9.1 punkt 4 + §7):
   - ACL reverse-lookup: okänt id → fallback-DTO, ej throw.
   - ACL hierarki: parent-child-relation korrekt (region→municipality,
     occupation-field→occupation).
   - `GetTaxonomyTreeQueryHandler`: happy path + validator-failure
     (spegla `SuggestJobAdTerms`-testtäckning).
   - Bakåtkompat: redan-sparad `SearchCriteria` med concept-id renderar namn
     + okänt id → fallback (integration mot snapshot-seed).
   - Arkitektur-test: Application refererar inte Npgsql/EF-entities; Domain
     orörd (kör `dotnet test --filter Category=Architecture`).
4. **Backend-impl** (test-grön):
   - Application: `ITaxonomyReadModel`-port, `GetTaxonomyTreeQuery` +
     handler + validator + `TaxonomyTreeDto`.
   - Infrastructure: snapshot-entity + EF-config + `TaxonomyReadModel`-impl
     + seed/JSON (Variant A) eller Refit-klient + sync-job (Variant B).
   - DI: `services.AddScoped<ITaxonomyReadModel, TaxonomyReadModel>()` +
     ev. `IMemoryCache` + (Variant B) Refit-klient/recurring-job — **samma
     commit som handler/port-impl**.
   - Api: ny MapGet i `JobAdsEndpoints.cs` + rate-limit-policy + ETag.
5. **security-auditor — BLOCKING-punkter:**
   - Ny inmatnings-/läs-väg (endpoint) — parametriserat, cap, auth-gate
     verifierad.
   - Reverse-lookup-cap (concept-id-antal) mot DoS.
   - (Variant B) ny extern HTTP-yta mot taxonomy.api.jobtechdev.se —
     api-key-hantering, ingen PII (taxonomi är publik referensdata, men
     verifiera ingen oavsiktlig logg-yta).
   - Cache-poisoning/auth-interaktion (privat cache, ingen shared-proxy
     eftersom auth-gated).
6. **nextjs-ui-engineer** (separat batch, läs `node_modules/next/dist/docs/`
   först per web/AGENTS.md): ersätt `JobAdMultiSelect` concept-id-fritext med
   hierarkiska väljare (Län→Kommun, Yrkesområde→Yrke). Behåll Beslut A
   disclosure + Beslut B URL-multi-kontrakt. `onChange` emitterar fortfarande
   concept-id `string[]` (backend-kontraktet oförändrat).
7. **design-reviewer VETO + visual-verify** (Klas godkänner skärmbilder).

### Filer som skapas/ändras per lager

**Domain:** inga (verifierat — `SearchCriteria` orörd).

**Application (nya):**
- `JobAds/Abstractions/ITaxonomyReadModel.cs` (port)
- `JobAds/Abstractions/TaxonomyNodeDto.cs` (eller i query-mappen) — DTO
- `JobAds/Queries/GetTaxonomyTree/GetTaxonomyTreeQuery.cs`
- `JobAds/Queries/GetTaxonomyTree/GetTaxonomyTreeQueryHandler.cs`
- `JobAds/Queries/GetTaxonomyTree/GetTaxonomyTreeQueryValidator.cs`
- `JobAds/Queries/GetTaxonomyTree/TaxonomyTreeDto.cs`

**Infrastructure (nya):**
- `Taxonomy/TaxonomyConcept.cs` (snapshot-entity, Infrastructure-intern)
- `Persistence/Configurations/TaxonomyConceptConfiguration.cs`
- `Taxonomy/TaxonomyReadModel.cs` (impl `ITaxonomyReadModel`)
- `Persistence/Migrations/<ts>_AddTaxonomySnapshot.cs` (db-migration-writer)
- Variant A: `Taxonomy/taxonomy-snapshot.json` + seeder
- Variant B: `Taxonomy/IJobTechTaxonomyClient.cs` (internal Refit) +
  `Application/JobAds/Jobs/SyncTaxonomy/SyncTaxonomyJob.cs` +
  `Worker/Hosting/SyncTaxonomyWorker.cs` + `RecurringJobRegistrar.cs`-rad

**Infrastructure (ändras):**
- `DependencyInjection.cs` — ny `AddScoped<ITaxonomyReadModel,...>` +
  ev. cache + (Variant B) Refit-klient/job-registrering
- `IAppDbContext.cs` — **endast om** snapshot-tabellen ska exponeras som
  `DbSet<TaxonomyConcept>` för handlern. **Hellre inte:** håll snapshot bakom
  porten (`ITaxonomyReadModel` äger EF-querien internt) så `IAppDbContext`
  inte växer med en read-model-tabell som ingen aggregate-handler rör.
  **Bekräfta-punkt för CTO** (del av MAP-2).

**Api (ändras):**
- `Endpoints/JobAdsEndpoints.cs` — ny MapGet `/taxonomy` (eller
  `/taxonomy/regions` + `/taxonomy/occupations`) + rate-limit + ETag
- `RateLimiting/RateLimitingExtensions.cs` — ev. ny policy (MAP-3)

---

## 6. Multi-approach-punkter för senior-cto-advisor (explicit lista)

| ID | Fråga | Varianter | dotnet-architect-observation |
|---|---|---|---|
| **MAP-1** | Snapshot-persistens | A seedad JSON/migration · B synkad cron-tabell · C hybrid | A lägst blast-radius, starkast YAGNI mot CTO:s egen kvartalstakt-motivering. Constraint uppfylls av alla tre (ej diskriminator). CTO avgör. |
| **MAP-2** | ADR-granularitet + porten-vs-DbSet | Ny ADR 0043 vs ADR 0042-amendment · `ITaxonomyReadModel`-port äger EF internt vs `DbSet<TaxonomyConcept>` på `IAppDbContext` | CTO rad 28/50 + Klas avgör ADR-granularitet. Arkitektur-observation: port-inkapsling håller `IAppDbContext` smal (snapshot är read-model, ingen aggregate) — speglar `IJobSource`. CTO bekräftar. |
| **MAP-3** | DoS + cache | `ListReadPolicy` vs `SuggestPolicy` vs ny policy · ETag vs in-memory vs båda · reverse-lookup concept-id-cap | Data statiskt + auth-gated → ETag + in-memory naturligt; cap spegla `MaxConceptIds=10`. security-auditor BLOCKING-input. |

Inga TD-lyft föreslås (§9.6): allt hör till sök-ytan = Fas 2:s domän, ingen
saknad funktion-dependency. CTO Fråga 3 fastslog egen redesign-batch, ej TD.

---

## Referenser

- CLAUDE.md §2.1 (lager), §2.2 (DDD), §2.3 (CQRS), §3.3 (records/DTO),
  §3.6 (IAppDbContext direkt/projektioner), §5.1 (anti-patterns), §9.6 (in-block vs TD)
- docs/reviews/2026-05-17-soktyta-platsbanken-cto.md (Approach A, agent-ordning)
- ADR 0042 rad 21 (inget externt taxonomi-API på sök-vägen), Beslut B/C
- ADR 0032 §2/§4/§5 (IJobSource-port-mönster, sync-skrivlast)
- ADR 0039 Beslut 1 (JobAdSearch SPOT), Beslut 3 (SortBy del av VO)
- Evans 2003 kap. 14 (Anticorruption Layer), kap. 5 (VO-likhet)
- Beck/Fowler 2018 (YAGNI/KISS); Hunt/Thomas 1999 (DRY); Ford/Parsons/Kua 2017 (blast-radius)
- Mönsterkällor i kod: `IJobSource.cs`, `SuggestJobAdTermsQuery*.cs`,
  `JobAdSearch.cs`, `SavedSearchConfiguration.cs`, `F2P9JobAdSearchColumns.cs`,
  `RecurringJobRegistrar.cs`, `DependencyInjection.cs`

# CI-regression-handoff — TaxonomyReadModel singleton cache-poisoning

> **⚠️ DIAGNOS KORRIGERAD / HANDOFF-HYPOTESEN FALSIFIERAD 2026-05-17 (commit `b3772a3`).**
> Den ursprungliga diagnosen nedan ("saved-search-batchen gjorde
> ListSavedSearchesQueryHandler till ny tidig ITaxonomyReadModel-konsument som
> poisonar singleton-cachen") skrevs av CC #1 enbart via gh-loggar UTAN lokal
> repro och är **empiriskt motbevisad** av test-coverage-CC:s §9.4-verifiering.
> **Verklig rotorsak + levererad fix: se sektionen "RÄTTAD DIAGNOS & LÖSNING"
> sist i dokumentet.** Originaltexten bevaras oförändrad nedan som
> granskningstrail (CLAUDE.md §9.7-anda — felaktiga diagnoser strippas inte,
> de markeras och motbevisas).

**Datum:** 2026-05-17
**Status:** ~~main-CI RÖD~~ **LÖST — main-CI GRÖN** (run `25986194273` success, commit `b3772a3`). **Ägare per Klas-beslut: test-coverage-CC** (denna CC rör INTE koden — endast denna handoff). ~~Saved-search-namn-batchen (CC #1) introducerade regressionen~~ → **FALSIFIERAT: saved-search-batchen är icke-kausal (single-variable-revert-isolering)**; fixen koordineras till test-coverage-CC då den arbetar i samma test-/cache-yta.

## Symptom

`build`-workflow på `main` failar sedan saved-search-namn-batchen:
- RÖD: run `25985342946` (commit ~`04b679e`) + `25985582168` (commit `0a0c68b`)
- GRÖN strax innan: run `25984480168` (Fynd 2 deployad, FÖRE saved-search-batchen)
- `deploy-dev` v0.2.12 (`25985349578`) lyckades — annan testfas; `42P01 relation does not exist`-rader i loggen är förväntat **ProdBubble-brus**, EJ felet.

**Enda faktiska fel:** `JobbPilot.Api.IntegrationTests.JobAds.GetTaxonomyEndpointTests.GET_taxonomy_labels_resolves_known_and_unknown_ids_gracefully`
```
System.IndexOutOfRangeException : Index was outside the bounds of the array.
  at System.Text.Json.JsonDocument.GetArrayIndexElement(...)
  at ...GetTaxonomyEndpointTests.GET_taxonomy_labels_resolves_known_and_unknown_ids_gracefully()
     in tests/JobbPilot.Api.IntegrationTests/JobAds/GetTaxonomyEndpointTests.cs:102
```
Rad 102: `tree.GetProperty("regions")[0].GetProperty("conceptId")` → `regions`-arrayen är **TOM** → `[0]` kastar.

## Rotorsak (evidensbaserad, §9.4)

`TaxonomyReadModel` (`src/JobbPilot.Infrastructure/Taxonomy/TaxonomyReadModel.cs`) är **Singleton** (ADR 0043 MAP-2/MAP-3, CTO+Klas-accepterat) med process-global lat cache `private Task<CacheState>? _cached` (GetStateAsync cachar första *lyckade* laddningen).

I `JobbPilot.Api.IntegrationTests` delar fixturerna EN WebApplicationFactory-process → EN singleton-instans → cachen fylls av **första anroparen** i processen och delas av alla efterföljande tester.

- **Före saved-search-batchen:** enda ITaxonomyReadModel-konsumenten på endpoint-vägen var /taxonomy-endpointtesterna, vars fixtur har `taxonomy_concepts` seedad → cache fylls med 21 regioner → grönt.
- **Saved-search-namn-batchen (commit `04b679e`)** gjorde `ListSavedSearchesQueryHandler` till en NY ITaxonomyReadModel-konsument (`ResolveLabelsAsync`). Om ett saved-search-integrationstest kör **före** GetTaxonomyEndpointTests, mot en fixtur där `taxonomy_concepts` är oseeded (TaxonomySnapshotSeeder ej körd i den fixturen / annan DB-state), fyller det singleton-cachen med `CacheState(Tree(regions:[], occupationFields:[]), {})` **permanent för processen**. GetTaxonomyEndpointTests får då tomt träd → `regions[0]` → IndexOutOfRange.

Klassisk delad-singleton-test-isolerings-regression som pre-push EJ fångar men CI gör (jfr memory `feedback_di_with_handlers_same_commit`). Prod är opåverkad (där seedar TaxonomySnapshotSeeder alltid före trafik; security-auditor 2026-05-17 GO står).

## Multi-approach för senior-cto-advisor (CC väljer EJ själv — memory feedback_cto_decides_multi_approach)

Berör ADR 0043 MAP-2/MAP-3 (singleton-cache-beslutet) → CTO avgör; ev. ADR 0043-amendment-not = Klas-STOPP.

- **A — Scoped i st. f. Singleton:** löser isolering men förlorar ADR 0043 MAP-2/MAP-3-perf-beslutet (per-request DB-läsning). Sannolikt avvisad mot ADR men CTO väger.
- **B — Test-isolering:** Api-integrations-fixturer som (direkt/indirekt) rör ITaxonomyReadModel måste seeda `taxonomy_concepts` deterministiskt, ELLER singleton nollställs mellan fixturer (collection-fixture/factory-reset). Prod-koden orörd.
- **C — Cacha ej en TOM laddning som auktoritativ:** korrekt-seedad DB har alltid 21 regioner; ett tomt resultat = felkonfig/oseeded → behandla som "ej laddad", cacha ej, retry. Defensiv prod-fix, ingen ADR-konflikt, sannolikt lägst blast-radius — men CTO avgör (risk: maskerar genuint tomt tillstånd?).
- **D — annat** (CTO).

Vald approach: full backend-svit grön (inkl. Api/Worker-integration), ADR 0043 Beslut E (ACL utanför sök-vägen) + security-auditor-GO bevaras, ingen ny TD (genuin regression att fixa, §9.6).

## Kod-pekare

- `src/JobbPilot.Infrastructure/Taxonomy/TaxonomyReadModel.cs` — `_cached` + `GetStateAsync` (cachar lyckad laddning; tom = lyckad idag) + `LoadAsync`
- `src/JobbPilot.Infrastructure/DependencyInjection.cs` — `AddSingleton<ITaxonomyReadModel, TaxonomyReadModel>()` (Fynd 2)
- `tests/JobbPilot.Api.IntegrationTests/JobAds/GetTaxonomyEndpointTests.cs:102` — kraschpunkt (offer till poisonad cache, ej buggig själv)
- Saved-search Api-integrationstester (test-writer commit `4c3b9f5` + `04b679e`) — sannolik ny tidig konsument som poisonar; verifiera körordning/fixtur-seed
- `src/JobbPilot.Infrastructure/Taxonomy/TaxonomySnapshotSeeder.cs` — seedern; fixtur-seed-kontraktet
- Reviews-kontext: `docs/reviews/2026-05-17-fynd2-taxonomi-acl-cto.md` (MAP-2/MAP-3), `docs/reviews/2026-05-17-savedsearch-namn-cto.md`, `docs/decisions/0043-taxonomy-acl-for-search-surface.md`

## Diagnos-kommandon (reproducerbarhet)

```
gh run view 25985582168 --log-failed   # IndexOutOfRange @ GetTaxonomyEndpointTests.cs:102
gh run view 25984480168 --json conclusion   # success (FÖRE batchen — bevisar grön→röd-gränsen)
```

## Disciplin

senior-cto-advisor INNAN kod (multi-approach + ADR 0043-beröring). test-writer/test-runner verifierar full svit grön. Ej TD. Endast EN CC i TaxonomyReadModel.cs + Api-integrations-fixturerna samtidigt (koordination — CC #1/saved-search rör inget per Klas-beslut). Klas-STOPP vid ev. ADR 0043-amendment.

---

## RÄTTAD DIAGNOS & LÖSNING (test-coverage-CC, 2026-05-17, commit `b3772a3`)

### Hur originaldiagnosen falsifierades (§9.4)

Originaltexten ovan skrevs utan lokal repro. Verifiering steg för steg:

1. **Failande test reproducerat lokalt** (Release, Testcontainers): `GetTaxonomyEndpointTests.GET_taxonomy_labels_resolves_known_and_unknown_ids_gracefully` — `IndexOutOfRangeException` på `tree.GetProperty("regions")[0]` (tom array).
2. **Kört GetTaxonomyEndpointTests HELT ENSAM (noll saved-search-tester):** failar IDENTISKT, samma `warn: TaxonomySnapshotSeeder[3]` (LogSchemaMissing). Saved-search-tester behövs alltså INTE för att reproducera → poisoning-by-test-ordning-hypotesen håller inte.
3. **Single-variable-isolering:** återställde de 3 saved-search-filerna (ListSavedSearchesQueryHandler/GetSavedSearch/SavedSearchDto) till exakt green-commit-versionen (`0a2405c`) vid HEAD, byggde om, körde GetTaxonomyEndpointTests ensam → **failar fortfarande identiskt**. Saved-search-batchen är **icke-kausal**.
4. **git diff `0a2405c`(grön, run `25984480168`, 1130/1130)..HEAD:** enda src-ändringen är de 3 saved-search-filerna. `ApiFactory.cs`, `GetTaxonomyEndpointTests.cs`, `TaxonomySnapshotSeeder.cs`, `DependencyInjection.cs` BYTE-IDENTISKA. Ingen DI-/infra-/seeder-ändring.

### Verklig rotorsak (web-verifierad .NET 10-semantik)

`ApiFactory.InitializeAsync` (delad `[Collection("Api")]`):
```
using var scope = Services.CreateScope();          // (A) triggar EnsureServer → host-start
await ...AppDbContext.Database.MigrateAsync();      // (B) schema skapas HÄR
await ...AppIdentityDbContext.Database.MigrateAsync();
```
Web-verifierat (MS Learn + dotnet/aspnetcore #60370, .NET 10): `WebApplicationFactory.Services`-access triggar `EnsureServer()` → host startar → ALLA `IHostedService.StartAsync` körs FÖRE request-pipeline, dvs FÖRE (B). `TaxonomySnapshotSeeder` + `IdempotentAdminRoleSeeder` (registrerade IHostedService i ApiFactory; `RemoveStartupSeeders` anropas bara i prod-startup-fixturer) träffar tomt schema → `PostgresException 42P01` → Dev/Test-grace-period-catch → bail UTAN seed. StartAsync körs en gång per host-livstid → `taxonomy_concepts` / Admin-rollen oseeded för HELA collection-livstiden. `TaxonomyReadModel`-singleton cachar den tomma laddningen som auktoritativ → `regions[0]` kastar. **Pre-existerande latent fixtur-defekt**; den historiska grön→röd-flippen = test-ordnings-nondeterminism i delad collection, korrelerad men icke-kausal med saved-search-batchen. **Prod opåverkad:** `JobbPilot.Migrate` kör DDL före Api-trafik (ADR 0043 Beslut B) → seedern bailar aldrig i prod.

### Lösning (senior-cto-advisor Approach D/B — "fix the cause not the symptom")

CTO-beslut (agentId `a90812f7f2e202a6a`): test-fixtur-determinism, **ingen prod-kod**, ingen ADR 0043-amendment, ingen security-auditor (ingen prod-cache-semantik rörd), ingen Klas-STOPP (entydigt mot Beck/Meszaros/Fowler/Martin). Approach A (Scoped) avvisad (löser ej rotorsaken + bryter MAP-3). Approach C (cacha ej tom) avvisad denna touch (rör security-GO:ad prod-cache-semantik, YAGNI — tomt kan ej nå prod; noterad som framtida incident-trigger-revision i ADR 0043, ej TD, ej nu).

**Ändring:** `tests/JobbPilot.Api.IntegrationTests/Infrastructure/ApiFactory.cs` — efter de två `MigrateAsync` körs de två idempotenta seedrarna explicit (riktat `is TaxonomySnapshotSeeder or IdempotentAdminRoleSeeder`, ej bred IHostedService-loop). Båda idempotenta (version-gate + `pg_advisory_xact_lock` resp. `RoleManager` check-and-insert + egen scope) → säkra att re-invoke:a; den tidigare bailade host-körningen är no-op. `src/` orört (verifierat `git diff HEAD --stat -- src/` tomt).

### Verifiering

- `GetTaxonomyEndpointTests` ensam: **7/7** (var 1 failed).
- Full backend-svit Release som CI (`dotnet test --solution JobbPilot.sln`): **1139/1139, 0 failed**, alla 6 assemblies (Api 2m22s + Worker-integration).
- code-reviewer GO **0 Blockers / 0 Majors / 2 icke-åtgärdskrävande Minors**.
- **main `build`-CI GRÖN:** run `25986194273` conclusion=success, commit `b3772a3`.

### Lärdom

Handoff-diagnoser utan lokal repro ska §9.4-verifieras innan de agerar som sanning. Gh-logg-only-diagnos missade att seeder-bailen är ordnings-oberoende och pre-existerande. Single-variable-isolering är billig och avgörande.

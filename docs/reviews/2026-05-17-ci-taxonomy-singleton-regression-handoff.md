# CI-regression-handoff — TaxonomyReadModel singleton cache-poisoning

**Datum:** 2026-05-17
**Status:** main-CI RÖD (`build`-workflow). Diagnos klar. **Ägare per Klas-beslut: test-coverage-CC** (denna CC rör INTE koden — endast denna handoff). Saved-search-namn-batchen (CC #1) introducerade regressionen; fixen koordineras till test-coverage-CC då den arbetar i samma test-/cache-yta.

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

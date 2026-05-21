---
session: F6 P4 sök-infrastruktur-fix (perf-bugg + filter-bugg-rotorsak)
datum: 2026-05-20
slug: f6-p4-sok-infrastruktur-fix
status: BACKEND LEVERERAD — pending Klas tag-push + deploy + manuell snapshot-backfill
commits:
  - "<P1: F6P4a migration — pg_trgm + GIN trigram>"
  - "<P2: JobTechHit POCO + sub-types + 6 nya tester>"
  - "<docs(adr): 0061 — sök-perf-strategi (GIN trigram, Approach A)>"
  - "<docs(sessions): F6 P4 sök-infrastruktur-fix>"
adrs:
  - "0061 (Accepted) — Sök-perf-strategi: GIN trigram-index för q-substring-match"
---

# F6 P4 sök-infrastruktur-fix — backend-leverans

**Mål:** Klas-direktiv 2026-05-20 — två problem i samma batch (BLOCKERAR F6 P4b SavedJobAds):

1. **P1 perf-bugg:** `GET /api/v1/job-ads?q=*` 40-52s på dev (52k rader)
2. **P2 filter-bugg-discovery:** ssyk/region/yrken "fungerar inte" — root-cause: BE/data eller FE?

## Discovery (CC, 2026-05-20)

### Perf-baseline verifierad
Autentiserad mot `dev.jobbpilot.se` (session via dev-test-creds):

| Query | Time | Items |
|-------|------|-------|
| `?page=1&pageSize=3` (no filter) | 2.2s | totalCount=51,749 |
| `?q=systemutvecklare&pageSize=5` | **40.4s** | 22KB svar (riktiga träffar) |
| `?ssyk=Z8ts_J5y_4ZJ&pageSize=5` | 0.6s | **0 items** (filter-bugg) |
| `?ssyk=CifL_Rzy_Mku` (Stockholms län-id) | 0.6s | **0 items** |
| `?ssyk=soBq_ia8_xcx` (Administratör) | 0.6s | **0 items** |
| `?ssyk=QQ23_iVQ_Kzw` (AML-Specialist) | 0.6s | **0 items** |

### Filter-bugg-rotorsak FUNNEN — backend
3 picker-conceptIds från `/api/v1/job-ads/taxonomy` testade → alla returnerar 0 träffar. Inspektion av `src/JobbPilot.Infrastructure/JobSources/Platsbanken/JobTechSearchResponse.cs`-POCO:n visade att `JobTechHit` saknade `Occupation`, `OccupationGroup`, `OccupationField`, `WorkplaceAddress` som deserialiserings-properties. `JsonSerializer.Serialize(hit)` i `PlatsbankenJobSource.cs:184` producerade payload utan klassifikations-keys → generated columns (`raw_payload->'occupation'->>'concept_id'` + `raw_payload->'workplace_address'->>'region_concept_id'`) gav NULL för **alla 51,749 rader** → ssyk/region-filter alltid 0 träffar.

Sanitizer-allowlist (`JobTechPayloadSanitizer.cs:42-50`) hade redan alla keys allowlistade — buggen var inte i sanitizer utan upstream i wire-format-POCO:n.

## Multi-approach via senior-cto-advisor (2026-05-20)

CC presenterade A/B/C/D + full discovery, CTO valde:

- **P1 = Approach A (GIN trigram-index på `lower(title)` + `lower(description)`).** Migration-only. INGEN Application-ändring. Bevarar Clean Arch-precedensen från ADR 0042 Beslut D + JobAdSearch.cs rad 19-22-kommentaren ("`EF.Functions.ILike` ligger i Npgsql-extension → Application-Clean-Arch-brott").
- **P2 = JobTechHit POCO-utvidgning** + manuell `SyncPlatsbankenSnapshotJob`-backfill för existerande 51k rader.
- **ADR 0060 Beslut 4 (N+1 YAGNI):** behåll **betingat**. Antagandet "20×<2s = OK" återställs när q-COUNT faller. ADR 0045 fitness function är rätt evolution-trigger om budget fortfarande bryts post-A-deploy.
- **ADR 0061:** NY (sök-perf-strategi). Implementations-dom för ADR 0042 Beslut D skala-trigger.
- **Scope:** P1 + P2 samma batch (båda Fas 1, samma touch, blockerar P4b). **Splittade i tre commits** för granskningstrail (Fowler 2018 atomic commits): P1 migration, P2 POCO+tests, ADR 0061.

Avvisade alternativ (B FTS, C cache-only, D hybrid) motiverade mot principer i CTO-rapport + ADR 0061 Beslut 2.

## dotnet-architect-design (mekanik-verifiering 2026-05-20)

- Migration: skippa `CONCURRENTLY` (EF Core transaktion-wrapping; dev-volym 51k acceptabelt). Functional `gin (lower(col) gin_trgm_ops)` matchar EXAKT `Col.ToLower()`-LIKE i LINQ. Partial-filter `WHERE status='Active' AND deleted_at IS NULL` speglar query-predikatet. Filnamn `F6P4aJobAdTrigramIndexes`. Down dropp:ar bara index (extension behålls).
- POCO: top-level `Occupation`/`OccupationGroup`/`OccupationField`/`WorkplaceAddress` (per JobTech v2-schema). Alla `internal sealed`, snake_case `JsonPropertyName`. Sanitizer-allowlist redan kompatibel.

## Implementation

### P1 — Migration (commit 1)

`src/JobbPilot.Infrastructure/Persistence/Migrations/20260520212725_F6P4aJobAdTrigramIndexes.cs`:

```sql
CREATE EXTENSION IF NOT EXISTS pg_trgm;

CREATE INDEX ix_job_ads_title_lower_trgm
  ON job_ads USING gin (lower(title) gin_trgm_ops)
  WHERE status = 'Active' AND deleted_at IS NULL;

CREATE INDEX ix_job_ads_description_lower_trgm
  ON job_ads USING gin (lower(description) gin_trgm_ops)
  WHERE status = 'Active' AND deleted_at IS NULL;
```

Down dropp:ar bara index (`pg_trgm`-extension idempotent additive). Migration genererad via db-migration-writer; `AppDbContextModelSnapshot.cs` oförändrad (raw SQL, ingen model-DSL).

### P2 — JobTechHit POCO (commit 2)

`src/JobbPilot.Infrastructure/JobSources/Platsbanken/JobTechSearchResponse.cs` utökad:

- 4 nya properties på `JobTechHit`: `Occupation`, `OccupationGroup`, `OccupationField`, `WorkplaceAddress`
- 4 nya `internal sealed class`-sub-typer (`JobTechOccupation`, `JobTechOccupationGroup`, `JobTechOccupationField`, `JobTechWorkplaceAddress`) med snake_case `JsonPropertyName`-attribut

`tests/JobbPilot.Application.UnitTests/JobAds/Infrastructure/JobTechHitDeserializationTests.cs` — 6 nya tester:
1. `Deserialize_PopulatesOccupationConceptId`
2. `Deserialize_PopulatesWorkplaceAddressRegionConceptId`
3. `Deserialize_PopulatesOccupationGroupAndField`
4. `Deserialize_GracefullyHandlesMissingClassification`
5. `RoundTripSerialize_PreservesClassificationJsonPaths` — verifierar att `JsonSerializer.Serialize(hit)` producerar exakt de JSON-paths som generated columns konsumerar
6. `RoundTripThroughSanitizer_PreservesClassificationForGeneratedColumns` — end-to-end via `JobTechPayloadSanitizer.SanitizeForStorage`

### Commit 3 — ADR 0061

`docs/decisions/0061-job-ad-search-perf-strategy.md` + README.md-rad. Skriven av adr-keeper på CC-utkast. Status Accepted (Klas-direktiv i startprompten).

## Reviews (alla gröna)

- **code-reviewer:** 0 Block / 0 Major / 0 Minor. "Mergeklar."
- **security-auditor:** 0 Block / 0 Critical / 0 High / 0 Medium / 0 Low. PII-neutral, GDPR Art. 5(1)(c) data-minimering uppfylld, partial-filter respekterar Art. 17 erasure-flöde, ingen ny logging-yta.

## Tester (alla gröna)

| Svit | Total | Status |
|------|-------|--------|
| Domain.UnitTests | 399 | ✓ |
| Application.UnitTests | 532 (526+6 nya) | ✓ |
| Architecture.Tests | 70 | ✓ |
| Api.IntegrationTests / ListJobAdsFilterTests | 13 | ✓ |
| Api.IntegrationTests / ListJobAdsMultiFilterTests | 6 | ✓ |

Architecture-grindar (`JobSourceLayerTests.JobTech_wire_types_are_internal_to_Infrastructure`) gröna — alla nya sub-POCOs är `internal sealed`.

## Deploy-iteration (2026-05-21, Klas "GO, kör allt")

Fyra deploy-cykler — varje misslyckande gav nästa fix:

| Tag | Resultat | Rotorsak / fix |
|-----|----------|----------------|
| `v0.2.51-dev` | Migrate FAILED | `42501 permission denied to create extension pg_trgm` — `jobbpilot_app` saknar CREATE-privilege (TD-71 REVOKE post-A5). |
| `v0.2.52-dev` | ensure-extensions OK, schema FAILED | Nytt CLI-mode `ensure-extensions` (master-creds, Phase A-mönster) skapar pg_trgm. Schema-task FAILED på `TimeoutException` — GIN-index på description överskred Npgsql command-timeout 30s. |
| `v0.2.53-dev` | Deploy OK, migration applied | `MigrationsOptionsFactory.BuildAppOptions` CommandTimeout 30s→600s. F6P4a applied (73s). MEN q-search förblev 35-50s. |
| `v0.2.54-dev` | Deploy OK, perf löst | Partial-index-predikat `WHERE status='Active'` matchade inte ListJobAds (som saknar status-filter — bara `SuggestJobAdTerms` har det). Ny migration `F6P4aJobAdTrigramIndexPredicateFix` → predikat `WHERE deleted_at IS NULL`. |

**Extra commits (utöver original 4):**
- `e30e387` fix(migrate): ensure-extensions CLI-mode
- `5bcae2c` fix(migrate): CommandTimeout 600s
- `39cf768` fix(job-ads): GIN-index partial-predikat-fix

## Verifierat post-deploy (v0.2.54-dev, dev.jobbpilot.se)

**P1 q-search — index aktivt:**

| Sökterm | Före | Efter (cold) | Efter (warm) |
|---------|------|--------------|--------------|
| systemutvecklare | 40s | 1.6s | 0.19s |
| sjuksköterska | — | 5.4s | 0.56s |
| ekonom | — | 5.0s | — |
| **lärare** | — | **18.7s** | — |

Handler-tid (LoggingBehavior, ej nätverk): systemutvecklare 1371ms→79ms, lärare 18762ms.

**P1 KVARSTÅENDE PROBLEM — vanliga korta svenska termer.** "lärare" 18.7s. Trigram-svaghet: korta termer med vanliga trigram ("are" = extremt vanligt svenskt ordslut — lärare/snickare/bagare/...) ger låg-selektiv GIN-kandidatmängd → många heap-fetch + de-TOAST av description. Specifika/långa termer är snabba; vanliga korta är inte. Detta är den dokumenterade trigram-vs-FTS-avvägningen som ADR 0061 Beslut 2 sköt upp som YAGNI — verklig mätning har nu bevisat problemet. **Kräver ny senior-cto-advisor-rond** (memory `feedback_adr_mechanism_vs_env_phase_triage`): acceptera / FTS-hybrid på description / title-only-trigram.

**P2 filter-bug — VERIFIERAD fungerande:**
- `ssyk=fg7B_yov_smw` (Systemutvecklare) → totalCount=2 ✓
- `region=CifL_Rzy_Mku` (Stockholms län) → totalCount=12 ✓
- `region=CaRE_1nn_cSU` (Skåne län) → totalCount=7 ✓

Små tal = enbart rader importerade med ny JobTechHit-POCO sedan v0.2.51-deployen (stream-jobbet `*/10`). De ~51k legacy-raderna får klassifikation först vid nästa fulla snapshot.

**recent-searches:** 57s/504 → 6.3s ✓

## Pending Klas-aktion (post-leverans)

1. **P2 backfill av 51k legacy-rader:** admin sync-endpoint avvecklad (410 Gone, ADR 0032 §9); ingen Hangfire-dashboard exponerad (TD-83). Backfill sker automatiskt vid nästa `sync-platsbanken-snapshot` (02:00 UTC) ELLER via operatörsåtgärd. Snapshot är idempotent UPSERT — re-importerar alla ~45k Platsbanken-jobb med full klassifikation.
2. **P1 lärare-perf:** senior-cto-advisor-rond för vanliga-korta-termer-svagheten. Rekommenderas som F6 P4-followup, inte blockerande för P4b.
3. **Verifiera efter snapshot-backfill:** ssyk/region-filter mot legacy-rader → totalCount ska reflektera hela korpusen.

## F6 P4b SavedJobAds — status

**Avblockerad.** P1 (q-perf) levererad för normalfall; P2 (filter-bug) kod-verifierad. lärare-perf-refinement är icke-blockerande followup. F6 P4b körs som separat backend-prompt.

## Forts. 2026-05-21 — build-CI-regression, EXPLAIN-diagnos, FTS-beslut

### build-workflow-regression (fixad)
När `CREATE EXTENSION pg_trgm` flyttades ur F6P4a-migrationen (commit `e30e387`) bröts `build`-workflow: Testcontainers-integrationstester kör migrationer men inte deploy-pipelinens `ensure-extensions`-steg → `42704 gin_trgm_ops does not exist`. Pre-push kör ej .NET-test → nådde CI. 7 test-fixturer fick `CREATE EXTENSION IF NOT EXISTS pg_trgm` före MigrateAsync (commits `6680c37` + `455f42e`). `build`-workflow åter grön (alla 7 jobs).

### explain-search diagnostik-mode (`a143f60`, v0.2.55-dev)
Ny CLI-mode i JobbPilot.Migrate — kör EXPLAIN (ANALYZE, BUFFERS) på q-search-filtret, loggar query-planen. Permanent operativt verktyg.

### EXPLAIN ANALYZE-diagnos — entydig
`lärare` COUNT-väg: Bitmap Heap Scan 4881ms, **`Heap Blocks: exact=4635, lossy=0`** → work_mem-hypotes (lossy bitmap) **definitivt utesluten**. description-trigram-index returnerar 12 980 kandidater, 7 581 falska positiva → kostnaden = de-TOAST + LIKE-recheck av ~13k stora description-texter. `systemutvecklare`: 165ms, 439 kandidater. Ren selektivitetsskillnad — fundamental trigram-svaghet.

### CTO-ronder → FTS-hybrid, Variant (b)
- **CTO-rond 1:** hypotes 1 bekräftad → Approach B (PostgreSQL FTS-hybrid).
- **Klas-input:** Platsbanken kör Elasticsearch (web-verifierat). ES-kostnad undersökt — managed $25-130/mån (opraktiskt liten) / self-hosted kräver 8GB RAM (ryms ej på Hetzner CX32). Klas-beslut: inte ES nu, stanna i PostgreSQL.
- **CTO-rond 2:** 7 delbeslut — FTS-only nu (query-token-parser = egen fas F6 P4c), `websearch_to_tsquery`, `ts_rank` på Relevance, behåll trigram (Klas-GO), spinner bekräftad, ADR 0061-amend + ny ADR 0062.
- **dotnet-architect-blocker:** FTS-LINQ-funktioner (`websearch_to_tsquery`/`@@`/`NpgsqlTsVector`) ligger fysiskt i Npgsql-assemblyn — ingen provider-agnostisk väg. CTO:s "smal Application-exception utan ny assembly-referens" ogenomförbar.
- **CTO-rond 3:** Variant (b) — Infrastructure-query-port `IJobAdSearchQuery`. Hela `JobAdSearch.ApplyCriteria`+`ApplySort` flyttas Application→Infrastructure bakom porten (SPOT bevaras, flyttas ej splittras). Kräver ADR 0039-amend + 0061-amend + ny ADR 0062. CTO häver explicit sitt eget rond-2-beslut ("INTE port") på verifierad falsk premiss.

### Klas-beslut 2026-05-21
- Behåll båda trigram-indexen (FTS-hybrid med substring-fallback).
- **FTS-implementationen körs i egen fokuserad session** (ren `/clear`, egen startprompt) — lager-refaktor förtjänar full uppmärksamhet.

## Status vid session-end

**F6 P4 sök-infrastruktur-fix (P1+P2) LEVERERAD & DEPLOYAD.** HEAD `a143f60`, tag `v0.2.55-dev` live på dev.
- P1 q-search: 40s → 1.6s cold / <0.2s warm för specifika termer. Vanliga korta termer (lärare 18.7s) → FTS-session.
- P2 filter-bug: kod-fix verifierad fungerande; backfill av 51k legacy-rader pending 02:00 UTC-snapshot.
- CI grön. 10 commits denna session.

**Nästa: F6 P4 FTS-skifte** — egen session, startprompt genererad i chatten 2026-05-21.
**Sedan: F6 P4b SavedJobAds** + **F6 P4c query-token-parser** + **F6 P4 retention** (51k vs 45k-städning, Klas-observation).

## Disciplin-noter

- senior-cto-advisor invokerades INNAN CC presenterade egen rekommendation (memory `feedback_cto_decides_multi_approach`) — tre ronder.
- dotnet-architect invokerades INNAN kod (CLAUDE.md §9.2) — fångade Clean Arch-blockern.
- code-reviewer + security-auditor INNAN commit (P1/P2-batchen).
- Inga TDs lyfta — alla fynd in-block per §9.6 fas-regeln. F6 P4c (query-parser) är planerad fas, ej TD (saknad funktion-dependency).
- Klas-GO för pg_trgm-extension-grant var förhandsgodkänt i startprompten; ensure-extensions löste det via master-creds-mode.
- ADR-prosa CC-skriven via adr-keeper på explicit Klas-direktiv i startprompten (override av §9.4 webb-Claude-verbatim per memory `feedback_klas_can_override_adr_verbatim_source`).
- Ingen FE-implementation (CLAUDE.md-förbud i prompt) — filter-bugg visade sig BE-fixbar.
- Pre-existing oparsade ändringar (`.claude/settings.json`, `docs/jobbpilot-v3-bundle/`, `docs/reviews/2026-05-17-agent-roster-gap-cto.md`) RÖRDA EJ.

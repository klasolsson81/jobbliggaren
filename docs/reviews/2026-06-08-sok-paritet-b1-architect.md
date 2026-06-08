# Arkitektur-analys — Platsbanken sök-paritet Fas B1 (data-layer, Klass 1)

**Datum:** 2026-06-08
**Agent:** dotnet-architect (agentId `af8a82f5460a193ba`) — teknisk inramning + rek där principen är entydig; multi-approach går till senior-cto-advisor.
**Underlag:** ADR 0067 Beslut 2 + ADR 0043-amendment, on-disk-verifiering 2026-06-08.

## Sammanfattning
OK — ren och korrekt utvidgning av etablerade mönster (F2P9 generated columns + ADR 0043 taxonomi-ACL). Inga kritiska arkitektur-fynd. EN viktig perf-/deploy-flagga (STORED-kolumn = full table rewrite under ACCESS EXCLUSIVE) + multi-approach-flaggor till CTO. Resten entydiga rekar.

## Rekommendation (entydig design)

### 1. STORED-kolumn EF-config — F2P9-klon med EN källskillnad
`JobAdConfiguration.cs` efter `RegionConceptId`-blocket. Shadow-properties (CLAUDE.md §2.1 — Domain får ingen JobTech-taxonomi-koppling, Evans kap. 14):
```csharp
builder.Property<string?>("OccupationGroupConceptId")
    .HasColumnName("occupation_group_concept_id")
    .HasComputedColumnSql("raw_payload->'occupation_group'->>'concept_id'", stored: true);
builder.Property<string?>("MunicipalityConceptId")
    .HasColumnName("municipality_concept_id")
    .HasComputedColumnSql("raw_payload->'workplace_address'->>'municipality_concept_id'", stored: true);
```
**KRITISK källskillnad:** `occupation_group` ligger **top-level** i payloaden (`JobTechSearchResponse.cs:85`), INTE nested under `occupation` som `ssyk_concept_id`. Naiva klonen "kopiera ssyk-raden" ger fel JSON-path. `municipality_concept_id` ligger under samma parent som `region_concept_id` (`workplace_address`) → exakt strukturell klon. Verifiera paths i migration-review mot sample-payload.

### 3. TaxonomyConceptKind — additiv, persisteringssäker
`+= Municipality` (barn till Region), `+= OccupationGroup` (barn till OccupationField). `TaxonomyConceptConfiguration.cs:25` har `.HasConversion<string>()` → persisteras som strängar, enum-ordning irrelevant, inga befintliga rader påverkas. `HasMaxLength(20)` rymmer "OccupationGroup" (15). **XML-kommentaren rad 5-7 ("Ingen Municipality") måste uppdateras till ADR 0043-amendment**, annars ljuger on-disk-doc mot kod.

### 4. Snapshot-fil-shape — additiv nesting, bakåtkompatibel
Nullable nya fält med `= null`-default → gammal snapshot deserialiserar till null, MapRows behandlar som tom (`?? []`). `municipalities[]` under region, `occupationGroups[]` under occupation-field (speglar `occupations`-nesting).

### 5. MapRows — deterministisk, samma nesting-mönster
Emittera Municipality-rader (ParentConceptId=region) + OccupationGroup-rader (ParentConceptId=field). Uppdatera List-kapacitets-hint. **Determinism-varning:** `LoadSnapshot_ShouldHaveUniqueConceptIdsAcrossHierarchy`-testet måste utökas till municipalities + occupationGroups (PK-unikhet). MapRows ska inte tyst svälja dubbletter.

### 7. Blast-radius — låg
`LoadAsync` filtrerar explicit per Kind → nya rader ignoreras → `TaxonomyTreeDto`/picker oförändrade i B1 (korrekt, C1-domän). MEN `labelByConceptId` (rad 108-110) grupperar **alla** concept-id → kommun/grupp-labels blir auto-resolverbara via `ResolveLabelsAsync` (benignt additivt, önskvärt för C2 reverse-lookup; notera i PR-body). Tester: `TaxonomySnapshotSeederTests` (utöka MapRows + unique-id), `TaxonomyAclLayerTests` (ska ej brytas — nya records förblir internal), `TaxonomyQueryHandlersTests` (ska ej brytas — om de bryts läckte ett Kind-filter = fynd).

## Multi-approach-flaggor till senior-cto-advisor (rör ej som beslut)

**[Viktigt] Migration — STORED generated column = FULL TABLE REWRITE under ACCESS EXCLUSIVE.** Din flagga korrekt, discovery-briefen fel. `ADD COLUMN ... GENERATED ALWAYS AS (...) STORED` skriver om hela heapen + tar ACCESS EXCLUSIVE (blockerar läs+skriv på `job_ads`). F2P9 körde redan detta mot rader → mönstret bevisat, men sannolikt mot färre än dagens 44 801. Lokalt = sekunder. **Deploy-konsekvens (Hetzner senare):** kort hård paus på alla jobbannons-queries med stack igång. CTO-material. **En vs två migrationer:** EF genererar två separata ALTER TABLE → två rewrites oavsett (PostgreSQL slår inte ihop EF:s separata satser). Rek till CTO: två AddColumn i EN migration (matchar F2P9), acceptera två rewrites som engångskostnad mot 44k, dokumentera lås-fönster i kommentar. `CREATE INDEX CONCURRENTLY` för Hetzner = eget spår, ej B1.

**[Viktigt] Snapshot-genereringsstrategi + B1/C1-gräns** — CTO-fråga. Konsekvens: kommun-noder (~290) + ssyk-level-4 (~400) gör snapshot större; `TaxonomyReadModel` läser hela `taxonomy_concepts` in i minnet (rad 77-79, ingen Kind-filter) → bounded men växande in-memory-cache materialiserar noder ingen B1-filter konsumerar (betald minneskostnad utan B1-nytta tills C1). Acceptabelt.

**[Nice-to-have] Seeder-idempotens med nya Kinds.** `ExecuteDeleteAsync` + full re-insert gated på `TaxonomyVersion`. Snapshot-versionen MÅSTE bumpas (idag "29"), annars skippar seedern de nya raderna tyst (`LogUpToDate`).

## TDD-ordning till test-writer
1. **FÖRST — migration mot Testcontainers** (ej InMemory — STORED-omberäkning sker bara i riktig PostgreSQL; InMemory ignorerar `computedColumnSql`; `feedback_ef_strongly_typed_vo_contains_translation`). Seeda job_ads-rad med känd raw_payload, kör migration, assert occupation_group_concept_id="DJh5_yyF_hEM" + municipality_concept_id="AvNB_uwa_6n6". Fångar JSON-path-fel (top-level vs nested).
2. MapRows — Municipality-under-Region + OccupationGroup-under-OccupationField + ParentConceptId (unit).
3. LoadSnapshot unique-id — utöka till nya noder (PK-invariant).
4. Seeder-idempotens — re-seed med nya Kinds, advisory-lock-path (integration/Testcontainers).
5. Snapshot-deserialisering — gammal snapshot utan nya fält → tom lista, ej krasch.

## Referenser
CLAUDE.md §2.1/§3.6/§5.1; ADR 0043 + amendment; ADR 0067 Beslut 2; Evans 2003 kap. 14; F2P9-precedens; PostgreSQL-dom (ADD COLUMN STORED = table rewrite + ACCESS EXCLUSIVE).

# Migration-rapport — Fynd 2: Taxonomi-ACL snapshot-tabeller (ADR 0043, Variant A)

**Datum:** 2026-05-17
**Agent:** db-migration-writer
**Uppdrag:** Generera + verifiera EF Core 10 / PostgreSQL-migration för
ADR 0043 taxonomi-ACL (CTO Variant A, MAP-1). Två fristående tabeller,
ingen FK, ingen påverkan på `job_ads`/`saved_searches`.
**Status:** Migration genererad + verifierad. Committad LOKALT. Ej pushad
(Klas håller push-GO).

---

## Migration skapad: F2TaxonomySnapshot

**Filer:**
- `src/JobbPilot.Infrastructure/Persistence/Migrations/20260517015814_F2TaxonomySnapshot.cs`
- `src/JobbPilot.Infrastructure/Persistence/Migrations/20260517015814_F2TaxonomySnapshot.Designer.cs`
- `src/JobbPilot.Infrastructure/Persistence/Migrations/AppDbContextModelSnapshot.cs` (uppdaterad)

**Typ:** Additive — två NYA fristående tabeller. Icke-destruktiv.
**Påverkade entiteter:** `TaxonomyConcept`, `TaxonomySnapshotMeta`
(båda Infrastructure-interna, ej på `IAppDbContext` — ADR 0043 Beslut C/MAP-2).

**Migrationsnamn:** `F2TaxonomySnapshot` — följer repo-konventionen
`F2<PascalCase>` (jfr `F2P9JobAdSearchColumns`, `F2SavedSearches`,
`F2SearchCriteriaMultiValue`, `F2SuggestTitlePrefixIndex`). `F2`-prefix =
Fas 2-sök-yta.

**Genererad via** (repo-konvention, ADR 0013 — `--context` krävs pga
två DbContext; `--startup-project` = Api pga Design-paketet ligger där):

```
dotnet ef migrations add F2TaxonomySnapshot \
  --project src/JobbPilot.Infrastructure \
  --startup-project src/JobbPilot.Api \
  --context AppDbContext \
  --output-dir Persistence/Migrations
```

DesignTime-vägen: `DesignTimeDbContextFactory` →
`MigrationsOptionsFactory.BuildAppOptions` (single source of truth, ADR 0034).
`UseSnakeCaseNamingConvention()` aktiv → snake_case auto-genererad (inga
manuella `HasColumnName`).

---

## Schema-ändringar (verifierad Up-SQL)

```sql
CREATE TABLE taxonomy_concepts (
    concept_id character varying(32) NOT NULL,
    kind character varying(20) NOT NULL,
    label character varying(256) NOT NULL,
    parent_concept_id character varying(32),
    CONSTRAINT pk_taxonomy_concepts PRIMARY KEY (concept_id)
);

CREATE TABLE taxonomy_snapshot_meta (
    id integer NOT NULL,
    taxonomy_version character varying(32) NOT NULL,
    seeded_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_taxonomy_snapshot_meta PRIMARY KEY (id),
    CONSTRAINT ck_taxonomy_snapshot_meta_singleton CHECK (id = 1)
);

CREATE INDEX ix_taxonomy_concepts_kind ON taxonomy_concepts (kind);
CREATE INDEX ix_taxonomy_concepts_parent_concept_id ON taxonomy_concepts (parent_concept_id);

INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
VALUES ('20260517015814_F2TaxonomySnapshot', '10.0.7');
```

Körs i `START TRANSACTION; ... COMMIT;`. Inga `CONCURRENTLY`-index (kan ej
köras i transaktion — speglar F2P9/F2Suggest-mönstret; tabellerna är nya/tomma
så ACCESS EXCLUSIVE-låset är irrelevant).

**Verifierat:**
- Singleton-check-constraint `ck_taxonomy_snapshot_meta_singleton "id = 1"` — finns.
- Index på `kind` + `parent_concept_id` — finns (picker-trädet byggs per Kind;
  yrken slås upp per yrkesområde).
- **INGEN `HasData`/seed-INSERT** — endast DDL + history-rad. CTO MAP-1
  uppfyllt: seedning sker via `TaxonomySnapshotSeeder` (`IHostedService`,
  embedded JSON), inte migration. Migrationen är ren.
- Ingen FK till `job_ads`/`saved_searches` — concept-id är lös referens
  (replika av extern taxonomi, ADR 0043 Beslut C / architect §4).
- ProductVersion 10.0.7 i Designer + ModelSnapshot — konsekvent med hela
  migrations-kedjan (EF Core runtime 10.0.7; `dotnet ef`-CLI är 11.0.0-preview
  men Designer reflekterar runtime, ej CLI — ingen avvikelse).

## Down-SQL (verifierad — reversibel)

```sql
DROP TABLE taxonomy_concepts;
DROP TABLE taxonomy_snapshot_meta;
DELETE FROM "__EFMigrationsHistory" WHERE migration_id = '20260517015814_F2TaxonomySnapshot';
```

`Down` droppar BÅDA tabellerna. `DROP TABLE` kaskaderar implicit inline-
check-constraint + båda indexen (ingen orphan kvar). Ingen FK mellan tabellerna
→ drop-ordning irrelevant. Up→Down är ren round-trip.

---

## ADR 0032 sync-skrivlast — ingen konflikt (bekräftat)

Dessa tabeller rörs ENDAST av `TaxonomySnapshotSeeder` vid app-start:
idempotent, version-medveten (skippar om `TaxonomySnapshotMeta.TaxonomyVersion`
matchar snapshot-versionen), advisory-lock `pg_advisory_xact_lock(4307001)`
mot race mellan samtidiga Api-tasks. Verifierat i
`src/JobbPilot.Infrastructure/Taxonomy/TaxonomySnapshotSeeder.cs` (rad 50–87).

Stream/snapshot-cron (ADR 0032) rör ALDRIG `taxonomy_concepts`/
`taxonomy_snapshot_meta` — separata tabeller, ingen delad skrivväg, ingen
delad rate-limiter (`taxonomy.api.jobtechdev.se` är inte ens i bilden i
Variant A — committad embedded JSON, ingen runtime-extern-hop). Samma
resonemang som pg_trgm-avvisningen (lägre op-yta, ingen ny skrivlast-yta på
kritisk väg). **Ingen skrivlast-konflikt.**

---

## GDPR-bedömning — PII-kolumner medvetet utelämnade

Taxonomi = **publik referensdata** (svenska län, yrkesområden, yrkesnamn från
JobTech offentliga taxonomi). **Ingen PII.** Innehåller inga person-,
ansöknings- eller CV-data.

JobbPilots obligatoriska PII-kolumnmönster (soft-delete `deleted_at`,
audit `created_at/by`+`updated_at/by`, `row_version`, encryption) är
**medvetet utelämnat** — det mönstret skyddar persondata, och dess
mekanismer är meningslösa för en read-only replika av extern publik
referensdata:

- **Soft-delete:** N/A. Inget GDPR-radering-krav på publik referensdata.
  Rader byts ut idempotent av seedern vid version-bump (`ExecuteDeleteAsync`
  + re-insert), inte soft-delete-flaggad.
- **Audit-trail:** N/A för per-rad. `TaxonomySnapshotMeta` (version +
  `seeded_at`) ger seed-nivå-spårbarhet — det är rätt granularitet för en
  ersättningsbar snapshot, inte per-rads-audit.
- **Encryption:** N/A. Inga känsliga fält (län-/yrkesnamn är offentliga).
- **Optimistic concurrency (`row_version`/`xmin`):** N/A. Enda skribent är
  seedern, serialiserad via `pg_advisory_xact_lock(4307001)` → ingen
  concurrent-write-konflikt att lösa.

Motiveringen är förankrad i ADR 0043 (Anticorruption Layer-replika av extern
taxonomi) och architect-review §4 (ingen migrations-påverkan, ingen
invariant-risk, lös concept-id-referens). Skiljer medvetet från PII-mönstret —
det är korrekt design, inte en utelämnad kontroll.

**GDPR-kontroller:**
- PII: ingen — publik referensdata
- Soft delete: N/A (motiverat ovan)
- Audit trail: N/A per-rad (seed-nivå via `taxonomy_snapshot_meta`)
- Encryption: N/A

---

## Verifiering

| Kontroll | Resultat |
|---|---|
| `dotnet build JobbPilot.sln` | Build succeeded — 0 Warning, 0 Error |
| `dotnet ef migrations add` | Done — Designer + ModelSnapshot uppdaterade |
| Up-SQL (`migrations script F2SuggestTitlePrefixIndex F2TaxonomySnapshot`) | Verifierad — endast DDL, ingen seed |
| Down-SQL (`migrations script F2TaxonomySnapshot F2SuggestTitlePrefixIndex`) | Verifierad — droppar båda tabellerna, reversibel |
| ModelSnapshot innehåller båda entiteterna + check-constraint | Verifierad (rad 612/648/669) |
| Designer migration-ID + ProductVersion 10.0.7 | Verifierad — konsekvent med kedjan |
| Testcontainers (`postgres:18`) — full migrations-kedja appliceras | **9/9 SavedSearchesTests passed** (ApiFactory `MigrateAsync` körde hela kedjan inkl. F2TaxonomySnapshot mot riktig Postgres 18) |
| Regression `saved_searches`/`job_ads` | Noll — befintliga integration-tester gröna (bekräftar ADR 0043 §4 bakåtkompat) |

Testcontainers-verifiering: `ApiFactory.InitializeAsync` kör
`AppDbContext.Database.MigrateAsync()` mot en frisk `postgres:18`-container.
Att SavedSearchesTests (9/9) passerar bevisar att F2TaxonomySnapshot
appliceras rent på riktig PostgreSQL 18 OCH att `saved_searches`/`job_ads`
är opåverkade.

---

## Commit

- **Typ:** `chore(infra)` — ren schema/DDL-leverans utan domän-/feature-yta
  (migration + Designer + ModelSnapshot i samma commit per
  memory `feedback_di_with_handlers_same_commit`-disciplin).
- **SHA:** se nedan (fylls vid commit).
- **Pushad:** NEJ — Klas håller push-GO.

CLAUDE.md §2 (Clean Arch — entiteter Infrastructure-interna, ej på
`IAppDbContext`) + BUILD.md §5 (no anti-patterns — parametriserat, ingen
rå string-concat, EF Core) verifierade. Inga TD-lyft (§9.6 — allt hör till
Fas 2 sök-yta, ingen saknad funktion-dependency).

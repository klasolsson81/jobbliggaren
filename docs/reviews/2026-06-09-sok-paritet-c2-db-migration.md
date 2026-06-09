# db-migration-writer — Platsbanken sök-paritet Fas C2 (reverse-lookup-migration)

**Datum:** 2026-06-09
**Agent:** db-migration-writer (design + skrivning; CC verifierade slutkörningen)
**Scope:** `20260609214512_C2SearchParityReverseLookupAndRecentExpansion` — reverse-lookup av `saved_searches.criteria` (jsonb, occupation-name → ssyk-level-4) + `recent_job_searches`-kolumnbyte + radering. Design-ram: CTO-dom (c)/(d)/(f) (`2026-06-09-sok-paritet-c2-cto.md`) + architect F3 (`-c2-architect.md`).

---

## 1. Migrations-struktur

**EN migration för hela C2-batchen** (CCP — (c)+(d)+(e) odelbara; en psql-apply-GO). `Up()` i bindande ordning:

1. **`DELETE FROM recent_job_searches;` FÖRE DDL** — NOT NULL-AddColumn utan default failar annars på befintliga rader; raderna är självåterbyggande cache-data utan audit-trail-värdighet (CTO (d); dev-DB hade 3 rader varav 1 med rå SSYK-kod `{5132}`, omappbar).
2. **Recent-DDL:** `DropColumn(ssyk_list)` + `AddColumn(occupation_group_list)` + `AddColumn(municipality_list)` (text[], NOT NULL). **DROP+ADD, inte RENAME** — kolumnen byter semantik och en kolumn kan inte bli två (architect F3).
3. **Reverse-lookup-transform** via `BuildReverseLookupSql()` (internal, `InternalsVisibleTo` Api.IntegrationTests — testerna kör EXAKT migrationens SQL, ingen testkopia som kan glida):
   - `CREATE TEMP TABLE _occname_to_ssyk4` + INSERT-batchar (500 rader/VALUES-sats, 2179 poster, ordinal-sorterade → deterministisk statement-ordning),
   - **fail-loud DO-block:** omappbart `Ssyk`-id → `RAISE EXCEPTION` (Saltzer/Schroeder; ingen tyst dataförlust),
   - **jsonb-UPDATE:** predikat = **nyckel-existens** (`criteria ? 'Ssyk'`, inkl. `"Ssyk":[]` — gamla Write emitterade alltid nyckeln); `(criteria - 'Ssyk') || jsonb_build_object('OccupationGroup', ...)` med `array_agg(DISTINCT ... COLLATE "C" ORDER BY ... COLLATE "C")` → **sorterad+distinct i LAGRAD form** (ADR 0042 invariant 1; `"C"` = byte-ordning = `StringComparer.Ordinal`-paritet),
   - `DROP TABLE _occname_to_ssyk4;`.

**`Down()`:** mekaniskt reversibel DDL (raderar recent-rader igen + återställer `ssyk_list`), **dokumenterat LOSSY** i XML-doc: recent-rader borta (cache); jsonb-transformen irreversibel (grupp→yrken 1-till-många). Accepterat per CTO (f): 0 rader vid apply, ingen prod (ADR 0066), mappnings-kunskapen committad i resursen.

**Seeder-ordering-fällan strukturellt eliminerad:** migrationen läser ENDAST embedded-resursen — aldrig `taxonomy_concepts` (seedas EFTER migrationer) och aldrig den levande `taxonomy-snapshot.json`.

## 2. Resurs-läsning + injection-skydd

- Frusen resurs: `Persistence/Migrations/Resources/occupation-name-to-ssyk-level-4.v30.json` (embedded, LogicalName-wired i csproj; 2179 poster; regenereras ALDRIG — migrations-immutabilitet, Fowler/Sadalage).
- `LoadFrozenMapping()` fail-loud:ar på: saknad resurs, saknat/icke-objekt `mappings`, icke-sträng-värde, **0 poster**, och **ogiltigt concept-id-format**: varje nyckel OCH värde valideras mot `^[A-Za-z0-9_-]{1,32}$` INNAN interpolation i SQL — grammatiken utesluter `'`, `\`, whitespace och alla quoting-tecken → ingen injection-yta ens om den committade resursen manipulerats (defense-in-depth; ingen escaping-gymnastik, ingen tyst sanering).
- SQL:en genereras deterministiskt ur den frusna resursen → `dotnet ef migrations script --idempotent` producerar samma SQL vid varje generering.

## 3. Test-täckning (Testcontainers, ej InMemory) + utfall

`tests/JobbPilot.Api.IntegrationTests/SavedSearches/C2ReverseLookupMigrationTests.cs` (5 tester, kör migrationens egna SQL via `BuildReverseLookupSql()`):

| Test | Verifierar |
|---|---|
| (a) Transform + converter-läsbarhet | `Ssyk`-nyckel struken, `OccupationGroup` = mappat id, övriga nycklar verbatim; läsbar genom fail-loud-konvertern; **idempotens** (omkörning → identiskt sluttillstånd) |
| (b) `"Ssyk":[]`-radklassen | Nyckel-existens-predikatet: nyckeln strips, `OccupationGroup:[]`, Region-only-raden passerar tom-invarianten |
| (c) Omappbart id (`5132` = dev-DB-fyndet) | `PostgresException` med "omappbart Ssyk-id"; raden ORÖRD (abort före UPDATE) |
| (d) recent_job_searches-DDL | `occupation_group_list`/`municipality_list` NOT NULL ARRAY finns; `ssyk_list` borta; NOT-NULL-på-tom-tabell bevisar DELETE-före-DDL |
| (e) Multi-element + dubbletter | Sorterad distinct i lagrad form (`[mcRJ_kq2_jFr, vPP6_rsw_dck]`, COLLATE "C"-ordinal); två yrken → samma grupp dedupliseras |

**Utfall 2026-06-09/10:** Api-integration **420/420 gröna** (inkl. ovanstående + uppdaterade `SearchCriteriaJsonbBackcompatTests`/`ListJobAdsSsykNoOpTests`/`SavedSearchesTests`/`RecentSearchesTests`). Domain 440, Application 692, Arkitektur 78 — gröna. (Migrate/Worker-sviterna körda separat — se PR-body.)

## 4. Idempotent-script-verifiering

`dotnet ef migrations script --idempotent --context AppDbContext` genererad och granskad (3551 rader; temp-fil raderad). C2-delen korrekt `$EF$`-guardad per statement (nested `$$`-dollar-quoting i fail-loud-DO-blocket kolliderar inte med `$EF$`-taggen). Utdrag:

```sql
IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "migration_id" = '20260609214512_C2SearchParityReverseLookupAndRecentExpansion') THEN
DELETE FROM recent_job_searches;
...
ALTER TABLE recent_job_searches DROP COLUMN ssyk_list;
ALTER TABLE recent_job_searches ADD occupation_group_list text[] NOT NULL;
ALTER TABLE recent_job_searches ADD municipality_list text[] NOT NULL;
...
CREATE TEMP TABLE _occname_to_ssyk4 (...);
INSERT INTO _occname_to_ssyk4 (occupation_name_id, ssyk4_id) VALUES ('15e8_KDZ_31Z', 'mcRJ_kq2_jFr'), ...
DO $$ ... RAISE EXCEPTION 'C2 reverse-lookup: saved_search % bär omappbart Ssyk-id "%". ...' ... $$;
UPDATE saved_searches s
SET criteria = (s.criteria - 'Ssyk')
    || jsonb_build_object('OccupationGroup', COALESCE(
        (SELECT to_jsonb(array_agg(DISTINCT m.ssyk4_id COLLATE "C"
                         ORDER BY m.ssyk4_id COLLATE "C"))
         FROM jsonb_array_elements_text(s.criteria->'Ssyk') AS e(elem)
         JOIN _occname_to_ssyk4 m ON m.occupation_name_id = e.elem),
        '[]'::jsonb))
WHERE s.criteria ? 'Ssyk';
DROP TABLE _occname_to_ssyk4;
```

**Ej applicerad mot dev-DB (5435)** — Klas-GO krävs (persisterad användardata; standing-STOPP per CTO (c)).

## 5. Avvikelser från architect F3

**En, motiverad:** F3-skissens `array_agg(DISTINCT m.ssyk4_id ORDER BY m.ssyk4_id COLLATE "C")` justerades till `array_agg(DISTINCT m.ssyk4_id COLLATE "C" ORDER BY m.ssyk4_id COLLATE "C")` — PostgreSQL kräver att ORDER BY-uttrycket i ett DISTINCT-aggregat matchar argumentlistan exakt ("in an aggregate with DISTINCT, ORDER BY expressions must appear in argument list"). Semantiken identisk (COLLATE ändrar inte värdet, bara sort-/jämförelseordningen). Dokumenterad i migrations-kommentar.

## Addendum 2026-06-10 — in-block-fixar efter security-auditor/code-reviewer

1. **`ConceptIdPattern` `$` → `\z`** (security Minor 1): .NET-`$` matchar före avslutande `\n`; ingen trim sker före SQL-interpolation → exakt ankare krävs. Synkad för symmetri i Domain + fyra validators.
2. **Skalär legacy-form-check** (security Minor 2 + code-review Minor 4): `jsonb_typeof(criteria->'Ssyk') <> 'array'`-check FÖRST i fail-loud-DO-blocket med pedagogiskt RAISE ("icke-array-form (pre-F2 skalär legacy)"); array-checken filtrerar nu på typeof i subquery (hindrar LATERAL-evaluering på icke-array-rader). Nytt Testcontainers-test (c2): `ReverseLookup_ShouldAbortWithClearMessage_WhenSsykIsScalarLegacyForm` — assertar feltext + orörd rad. Sviten omkörd grön (421 tester).

## Dom: GO (design + tester) — apply gated på Klas-GO.

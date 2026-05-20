# ADR 0061 — Sök-perf-strategi: GIN trigram-index för q-substring-match

**Datum:** 2026-05-20
**Status:** Accepted
**Kontext:** JobbPilot F6 Prompt 4 sök-infrastruktur-fix (2026-05-20). Empirisk perf-mätning visade att `GET /api/v1/job-ads?q=systemutvecklare` tog 40s p95 mot dev (51 749 job_ads-rader). ADR 0042 Beslut D:s explicit nedskrivna skala-trigger ("D2 ILIKE-heuristik för Fas 2-volym 5–15k rader; D1 tsvector = framtida skala-trigger") är bevisat brutet. Sekundär konsekvens: `/api/v1/me/recent-searches` (ADR 0060 Beslut 4) tog 33s/1 rad och 57s/504 vid 3 q-rader — N+1-cap × per-rad-COUNT med samma LIKE-pattern förstärker P1-roten.
**Beslutsfattare:** Klas Olsson (produktägare; explicit Accepted-direktiv i F6 P4 sök-infrastruktur-fix-startprompten 2026-05-20)
**Relaterad:** [ADR 0001](./0001-clean-architecture.md) (Dependency Rule), [ADR 0042](./0042-search-surface-information-architecture.md) Beslut D (LIKE-fallback + skala-trigger), [ADR 0045](./0045-performance-budget-and-fitness-functions.md) (perf-budgetar + fitness functions), [ADR 0060](./0060-recent-job-searches-auto-capture.md) Beslut 4 (N+1 YAGNI med cap=20).

> **Livscykel-/proveniens-not:** Skriven 2026-05-20 av Claude Code (adr-keeper-disciplin) på explicit Klas-direktiv i F6 P4 sök-infrastruktur-fix-startprompten (Leverans #7) — medveten override av CLAUDE.md §9.4 webb-Claude-verbatim-konventionen (memory `feedback_klas_can_override_adr_verbatim_source`). Besluts-substansen är grundad i senior-cto-advisor-dom 2026-05-20 (Approach A vald över B/C/D), dotnet-architect-design 2026-05-20 (migration-mekanik) och CC web-search 2026-05-20 (AWS RDS pg_trgm trusted-extension-status + PostgreSQL pg_trgm GIN-opclass-stöd för LIKE-pattern). Status **Accepted** per Klas explicit-direktiv.

---

## Kontext

ADR 0042 Beslut D etablerade `EF.Functions.Like(LOWER(col), "%q%")` som heuristik för Fas 2-volym (5–15k rader) med ett uttalat skala-villkor: tsvector/FTS skulle aktiveras "vid framtida skala-trigger, ej som TD". F6 P4-mätning 2026-05-20 bevisar att tröskeln har passerats:

| Endpoint | Mätt latens | Förväntat (ADR 0045-budget) |
|----------|-------------|-----------------------------|
| `GET /api/v1/job-ads?q=systemutvecklare` | 40–52s | < 2s p95 |
| `GET /api/v1/me/recent-searches` (1 q-rad) | 33s | < 4s p95 |
| `GET /api/v1/me/recent-searches` (3 q-rader) | 57s / 504-timeout | < 4s p95 |
| `GET /api/v1/job-ads` utan filter (baseline) | 2.2s | < 2s p95 |

Rotorsak: `EF.Functions.Like(j.Title.ToLower(), "%q%") || EF.Functions.Like(j.Description.ToLower(), "%q%")` (`src/JobbPilot.Application/JobAds/Queries/JobAdSearch.cs:51-65, 116-124`) genererar LOWER-LIKE substring-pattern som ingen B-tree-index kan accelerera → full-table-scan över 51 749 rader. Recent-searches förstärker via ADR 0060 Beslut 4:s N+1 capped vid 20 — varje row-COUNT använder samma icke-indexerade LIKE.

Den underliggande designfrågan: vilken indexstrategi aktiverar vi när ADR 0042 Beslut D:s skala-tröskel passerats, utan att bryta Clean Arch-gränsen som samma ADR redan etablerade?

## Beslut

### Beslut 1 — Approach A: GIN trigram-index på `LOWER(title)` + `LOWER(description)`

Migration `F6P4aJobAdTrigramIndexes` (`src/JobbPilot.Infrastructure/Persistence/Migrations/20260520212725_F6P4aJobAdTrigramIndexes.cs`) introducerar:

```sql
CREATE EXTENSION IF NOT EXISTS pg_trgm;

CREATE INDEX ix_job_ads_title_lower_trgm
  ON job_ads USING gin (lower(title) gin_trgm_ops)
  WHERE status = 'Active' AND deleted_at IS NULL;

CREATE INDEX ix_job_ads_description_lower_trgm
  ON job_ads USING gin (lower(description) gin_trgm_ops)
  WHERE status = 'Active' AND deleted_at IS NULL;
```

`pg_trgm` är trusted extension på AWS RDS PostgreSQL 16+ (web-verifierat 2026-05-20). Partial-filtret speglar query-predikatet (Active + soft-delete) exakt → mindre index och GDPR soft-delete-filter respekterat. Functional-uttrycket `lower(col)` matchar exakt `Col.ToLower()` i LINQ-genererad SQL, vilket är vad query-planneren behöver för att nyttja indexet utan att handler-koden ändras.

**Clean Arch bevarad:** ingen Application-lager-ändring. `JobAdSearch.cs` rad 19–22 har redan en kommentar som motiverar varför `EF.Functions.ILike` (Npgsql-extension) undviks. GIN-trigram-acceleration sker uteslutande i Infrastructure/migration-skikt — Application förblir provider-agnostisk per ADR 0001 (Martin 2017 kap. 22 — Dependency Rule).

### Beslut 2 — Approach A vald över B/C/D; ADR 0042 Beslut D:s skala-trigger bedöms uppfylld

ADR 0042 Beslut D:s "D1 tsvector = framtida skala-trigger" tolkas som *trigger-villkoret är uppfyllt* (51k > 15k tröskel + bevisad 40s p95). Vald approach är **A (trigram)**, inte D1 (FTS):

- **Clean Arch (Martin 2017 kap. 22):** Approach B (FTS) skulle kräva `EF.Functions.ToTsQuery` (Npgsql-extension) i Application eller raw SQL — bryter samma Clean Arch-gränsen som ADR 0042 Beslut D etablerade. Approach A respekterar precedensen.
- **YAGNI (Beck 1999):** Svensk morfologi (jobbar → jobb) är inte ett bevisat problem; det bevisade problemet är substring-latens. Fixa det bevisade.
- **KISS / SoC:** A = 1 migration + 0 nya kodrader. B = schema-change + tsvector-generated-column + handler-refactor. Komplexitets-ratio ~10× för marginell semantisk vinst som ingen efterfrågat.
- **DRY (Hunt/Thomas 1999):** A bevarar `JobAdSearch.ApplyCriteria` som SPOT för både `ListJobAdsQuery` och `RunSavedSearchQuery` (ADR 0039 Beslut 1).

**Avvisade alternativ:**

- **Variant B (PostgreSQL FTS via tsvector + generated column):** Bryter Clean Arch (Npgsql-extension eller raw SQL i Application). Premature semantisk komplexitet utan bevisat behov.
- **Variant C (Elasticsearch / extern sökmotor):** Massiv ops-overhead för ett indexerings-problem som PG löser nativt. Bryter ADR 0050 (deployment-target = AWS PG; ingen extern sökmotor planerad).
- **Variant D1 (FTS-only utan trigram):** Saknar substring-match-semantik som UI-kontraktet förutsätter ("systemut" ska matcha "systemutvecklare" mitt i ord).

### Beslut 3 — ADR 0060 Beslut 4 (N+1 YAGNI) behålls betingat

ADR 0060 Beslut 4:s YAGNI-grund (cap=20 + ADR 0045 fitness functions = evolution path) **håller**. Antagandet "20 × < 2s = acceptabelt" var alltid betingat på q-COUNT < 2s. Beslut 1 gör q-COUNT < 2s, vilket återställer cap × per-rad-COUNT till sitt designade golv. Om ADR 0045 fitness function fortfarande triggar budget-brott efter A-deploy + verifierad mätning → då (och bara då) lyfter fitness-function-trigger evolution-rekommendation (Hangfire-cache, batch-SQL eller annat). Inte preemptive.

### Beslut 4 — Down-migration drop:ar bara index, inte extension

`pg_trgm`-extension kan delas av framtida tabeller — `DROP EXTENSION` i down-migration är inte additive-safe. Down drop:ar de två GIN-indexen men behåller extensionen. Matchar F2-mönstret för extensions (idempotent additive).

## Konsekvenser

### Positiva

- **q-search-latens förväntas falla 40s → < 200ms** på 51k rader (PG-docs trigram-index p95-storleksordning för LIKE-pattern över GIN trgm_ops).
- **Recent-searches list-latens följer med** (20 cap × < 200ms = < 4s totalt, väl under ADR 0045-budget).
- **Ingen handler-ändring** → noll regress-yta i Application-lager.
- **Befintliga LIKE-pattern accelereras transparent** — relevans-sorteringen i `JobAdSearch.ApplyRelevanceSort` (rad 116–124, 4 LOWER-LIKE-pattern) drar nytta av samma index.
- **Clean Arch-precedens från ADR 0042 hedras** — index-acceleration är Infrastructure-koncern, inte Application-koncern.

### Negativa / accepterade trade-offs

- **GIN-trigram-index är skriv-tyngre än B-tree.** Acceptabelt — JobAd-insertion-volym (~50k upserts per snapshot-cykel, cron-frekvens) är inte hot-path.
- **Index-storlek växer** (description-kolumnen kan vara stor). Acceptabelt — RDS-storage är billig mot 40s p95-latens.
- **Ingen svensk stemming i Fas 1.** Acceptabelt — explicit YAGNI-dom; lyfts som ADR-trigger vid faktisk klagomål, inte spekulativt.
- **ACCESS EXCLUSIVE-lås under GIN-bygget** (se Mekanik-not 1) — sekund-intervall vid ~51k rader, accepterat för dev/staging; om prod-volym växer dramatiskt kan separat CONCURRENTLY-runbook bli aktuell.

### Verifikations-plan post-deploy

1. Re-mät `GET /api/v1/job-ads?q=systemutvecklare` mot dev (förvänta < 2s, mål < 200ms).
2. `EXPLAIN ANALYZE` på samma query — verifiera "Bitmap Index Scan on ix_job_ads_title_lower_trgm" eller motsv. på description.
3. Mät `GET /api/v1/me/recent-searches` med 3 q-rader (förvänta < 10s, ner från 57s).
4. ADR 0045 fitness function (NBomber p95) ratchet:as till nya nivån vid framtida observation.

## Mekanik-noter

### Mekanik-not 1 — `CREATE INDEX CONCURRENTLY` uteslutet medvetet

EF Core wrappar migrations i transaktion via Migrate schema-task; PG `CREATE INDEX CONCURRENTLY` kan inte köras i transaktion. Dev-volym ~51k rader → ACCESS EXCLUSIVE-låset under GIN-bygget är sekund-intervall, backward-compatible med körande API. Speglar mönstret i F2P9 + `F2SuggestTitlePrefixIndex`.

### Mekanik-not 2 — Functional-index måste matcha LINQ-uttryck exakt

`gin (lower(title) gin_trgm_ops)` matchar `lower(title) LIKE '%q%'` (EF.Functions.Like + `.ToLower()`-translation). Ett direkt `gin (title gin_trgm_ops)` skulle **inte** matcha LOWER-wrappade queries — query-planneren behöver textuell expression-match mellan index och predikat. Verifierat mot PostgreSQL-docs F.35 pg_trgm.

### Mekanik-not 3 — Cross-ref till ADR 0042

ADR 0042 Beslut D förblir gällande mekanik (LIKE-fallback för okänd-skala), men dess "D1 tsvector skala-trigger" tolkas nu *uppfyllt → vald approach A trigram*. Ingen supersession; ADR 0061 är *implementations-domen* när skala-tröskeln passerades. ADR 0042 status oförändrad.

### Mekanik-not 4 — Cross-ref till ADR 0045

ADR 0045 budgetar + fitness-function-mekanism oförändrade. ADR 0061 är *vilken indexstrategi vi valde när fitness function flaggade brott*. Ratchet av p95-tröskel ska göras post-deploy enligt ADR 0045 Beslut 5–6 (observe-only Fas 1 → flip till blockerande gate vid Klas-GO).

### Mekanik-not 5 — Cross-ref till ADR 0060

ADR 0060 Beslut 4 N+1 YAGNI bevarad betingat på Beslut 1:s effekt; re-mätning post-deploy avgör om Beslut 4 behöver framtida amend (inte nu, per CTO-dom 2026-05-20).

### Mekanik-not 6 — F6 P4 batch-relation

Migrationen levereras tillsammans med JobTechHit-POCO-fix (separat domän — filter-bugg-rotorsak: `JobTechSearchResponse.cs` saknade Occupation/WorkplaceAddress-properties). Två problem, samma batch per CLAUDE.md §9.6 (båda är Fas 1, samma touch, blockerar F6 P4b). Splittade i två commits för granskningstrail (Fowler 2018 — atomic commits per change-reason).

## Referenser

- Robert C. Martin, *Clean Architecture* (Prentice Hall, 2017), kap. 4 (ADRs), kap. 22 (Dependency Rule)
- Kent Beck, *Extreme Programming Explained* (1999) — YAGNI
- Hunt/Thomas, *The Pragmatic Programmer* (1999) — DRY/SPOT
- Ford/Parsons/Kua, *Building Evolutionary Architectures* (O'Reilly, 2017) — fitness functions som evolution-trigger
- Martin Fowler, *Refactoring* 2nd ed (Addison-Wesley, 2018) — atomic commits per change-reason
- [PostgreSQL docs F.35 — pg_trgm](https://www.postgresql.org/docs/current/pgtrgm.html) — GIN trigram-opclass + LIKE-pattern-stöd
- [AWS RDS PostgreSQL — Extension versions](https://docs.aws.amazon.com/AmazonRDS/latest/PostgreSQLReleaseNotes/postgresql-extensions.html) — `pg_trgm` trusted extension PG 16
- [Npgsql EFCore docs — Trigrams](https://www.npgsql.org/efcore/api/Microsoft.EntityFrameworkCore.NpgsqlTrigramsDbFunctionsExtensions.html) — referens (ej använd här; dokumenterad varför inte)
- Mätningar och STOPP-rapporter: `docs/sessions/2026-05-20-*-f6-p4-sok-infrastruktur-fix.md` (skapas i samma batch)
- Kod-källor: `src/JobbPilot.Infrastructure/Persistence/Migrations/20260520212725_F6P4aJobAdTrigramIndexes.cs`, `src/JobbPilot.Application/JobAds/Queries/JobAdSearch.cs:19-65, 116-124`
- Agent-domar: senior-cto-advisor 2026-05-20 (Approach A vs B/C/D), dotnet-architect 2026-05-20 (migration-mekanik), CC web-search 2026-05-20 (AWS RDS + PG-docs)

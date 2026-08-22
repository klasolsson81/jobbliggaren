# db-migration-writer — PR #1439 (#1409)

- **Agent:** `db-migration-writer` (SQL- och soft-delete-semantik; ingen migration i PR:en)
- **Datum:** 2026-08-22 · **Worktree:** `c:/tmp/jbl-orphan`
- **Runda 1 skope:** `git diff origin/main...HEAD`, bas `4c8ad6e1` (trepunkt, verifierad)
- **Status runda 1:** **0 Blocker, 0 Major.** Alla fem granskningsfrågor höll vid mätning i källkoden.

## Svar på de fem frågorna

**1. Plockas raden upp korrekt? VERIFIERAT KORREKT.** `job_seekers.deleted_at` är
`timestamp with time zone`; `GetAccountsReadyForHardDeleteAsync` filtrerar `DeletedAt < cutoff` där
`cutoff = clock.UtcNow.AddDays(-30)` (`DateTimeOffset`). **Båda sidor är timestamptz** (absoluta
tidpunkter), så ingen `AT TIME ZONE`-fälla finns — den kräver konvertering mot en tz-naiv `timestamp`,
och ingen kolumn här är tz-naiv. `NOW()` → mognar om 30 dagar. `NOW() - INTERVAL '31 days'` → eftersom
`T_job ≥ T_sql` alltid (jobbet kan bara köra EFTER operatörens SQL) reduceras villkoret till
`T_sql - T_job < 1 dag`, vilket alltid håller — **en hel dags marginal mot klockskevhet.**

**2. Är `job_seekers.id` rätt kolumn? VERIFIERAT KORREKT.** `id uuid` PK; `JobSeekerId` är en
`readonly record struct` med `HasConversion` till `Guid`. §3.3:s query, jobbets `js.Id.Value` och
`HardDeleteAccountAsync(Guid)` använder samma värde. `::uuid`-casten är rätt typad.

**3. Är den råa UPDATE:en säker och komplett? VERIFIERAT — med en premisskorrigering.**
Noll `CREATE TRIGGER` i hela migrationsmappen. Enda index på `job_seekers` är `ix_job_seekers_user_id`;
inget partiellt index på `deleted_at`. **`JobSeeker.SoftDelete(clock)` sätter ENDAST `DeletedAt`** och
reser `JobSeekerDeletedDomainEvent` — den råa UPDATE:en reproducerar exakt samma fält-avtryck.
`JobSeekerDeletedDomainEvent` har **noll konsumenter** i `src/`, så att kringgå den kostar inget.

⚠ **Premisskorrigering:** granskningsuppdraget antog att §4.1:s restore-SQL rör **fem** tabeller. Mätt:
det är **sex** (`job_seekers`, `applications`, `follow_ups`, `application_notes`, `resumes`,
`resume_versions`), vilket stämmer mot soft-delete-kaskaden. **Detta finns inte i den levererade
diffen** — PR:en avstår uttryckligen från att publicera någon aggregat-räkning. Felet låg i frågan.

**Ändrar sex-mot-en slutsatsen? Nej.** §4.2:s UPDATE siktar på hard-delete-upplockningen, inte på att
återimplementera soft-delete-kaskaden. `HardDeleteAccountAsync` hämtar alla ägda aggregat med
`IgnoreQueryFilters()` **ovillkorligt** och bryr sig inte om barnens `deleted_at`-state; FK CASCADE tar
barnen. Runbookens *"Det är hela ingreppet; jobbet gör resten"* är korrekt.

**4. Håller completeness-påståendet? VERIFIERAT — testet gör mer än en iff-invariant.**
`AccountHardDeleteCascadeFitnessTests` har **fyra** distinkta vakter: (a) fail-closed partition — varje
konkret `AggregateRoot<>` i Domain måste klassificeras, annars failar bygget; (b) wiring completeness —
varje CascadeMap-DbSet måste vara bunden till ett delete-verb i metodkroppen, brace-matchad och
kommentar-/strängstrippad **så att prosa inte kan uppfylla kravet**; (c) iff-regeln om
`IgnoreQueryFilters`, läst ur **EF-modellen** och inte ur en handhållen lista; (d) en anti-vakuitets-pin
som bevisar att varje arm faktiskt granskades av (c). Dokumenterad scope-gräns: vakten täcker
`AggregateRoot<TId>`, inte `Entity<TId>`. **PR-bodyn säger "aggregat", vilket är exakt vaktens scope —
ingen överdrift.**

Bekräftat att `dataKeyStore` och `auditTrailEraser` delar **samma** scopade `AppDbContext`, så
DEK-radering och audit-anonymisering ligger verkligen i den ambienta transaktionen.

**5. Räcker "raden borta ur §3.3:s query"? JA — av ett spårbart skäl specifikt för detta fall.**
Hela Steg 2 körs mot en delad anslutning inom en `BeginTransactionAsync`…`CommitAsync` med
`RollbackAsync` i catch. Att `job_seekers`-raden är varaktigt borta är därför **samma sak** som att hela
batchen committades. Enda delen utanför transaktionen är Steg 2h — och för en reverse orphan finns per
definition ingen Identity-rad att radera, så `if (user is not null)` hoppar över anropet. **Kontrollen
är tillräcklig HÄR; den generaliserar inte**, och runbooken påstår inte att den gör det.

## Korsverifiering av PR-bodyns egna påståenden

- *"All three Identity-delete sites live in `src/`"* — **bekräftat**, exakt tre anropsställen, och inget
  av dem kan lämna en `JobSeeker` bakom sig utom sweepen vid klockskevhet.
- *"GetAsync consults the tombstone and never reads the Identity row"* — **bekräftat** direkt i
  `RedisSessionStore`.

## Sammanfattning

**0 Blocker, 0 Major.** Enda avvikelsen är en Minor-nivå premisskorrigering i granskningsuppdragets egen
frågeställning, inte i diffen eller PR-bodyn.

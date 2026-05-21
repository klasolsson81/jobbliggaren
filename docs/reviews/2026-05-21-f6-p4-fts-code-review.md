# Code-review: F6 P4 FTS-skifte — IJobAdSearchQuery-port (Application→Infrastructure)

**Status:** ⚠ Changes requested
**Granskat:** 2026-05-21
**Granskare:** code-reviewer
**Auktoritet:** CLAUDE.md §2.1 (Clean Arch), §2.3 (CQRS), §3.6 (LINQ-hygien), §5.1 (anti-patterns), §7 (test-coverage), §1.6 (ADR-process), §9.2
**Scope:** Backend — Application + Infrastructure + Persistence + tester. 22 filer (3 nya Application, 1 ny Infrastructure-impl + migration, 6 ändrade, 1 borttagen, 5 testfiler).

---

## Sammanfattning

Arkitektoniskt är skiftet **välutfört**. Lager-flytten är motiverad och korrekt
riktad: PostgreSQL FTS-LINQ (`websearch_to_tsquery` / `@@` / `ts_rank`) ligger
fysiskt i Npgsql-assemblyn som arch-testet förbjuder i Application — porten
`IJobAdSearchQuery` är Application-ägd, impl:en `internal sealed` i
Infrastructure. SPOT bevaras via `JobAdFilterCriteria`. Tunna adaptrar är genuint
tunna. `IDateTimeProvider` används överallt, inga magiska strängar, inga
sync-over-async, inga MediatR-rester, `CancellationToken` propageras hela vägen.

**Inga Blockers.** Två Major och fyra Minor. Ingen av dem rör kod-korrekthet —
de rör granskningstrail, en defensiv död gren, och kommentar-/coverage-hygien.

---

## Major (bör fixas innan commit)

### M1 — ADR 0062 citeras ~30 gånger men filen finns inte

Filer: i princip varje ny/ändrad fil (`IJobAdSearchQuery.cs`,
`JobAdSearchQuery.cs`, `JobAdFilterCriteria.cs`, `JobAdSearchCriteria.cs`,
migration, `JobAdConfiguration.cs`, `DependencyInjection.cs`, alla testfiler).

`docs/decisions/` innehåller `0061-job-ad-search-perf-strategy.md` som högsta
nummer. `0062-*.md` existerar inte och saknas i `docs/decisions/README.md`-index.
Varje fil i denna ändring åberopar "ADR 0062" som den styrande beslutskällan för
hela lager-flytten, port-kontraktet och FTS-strategin.

Motivering: CLAUDE.md §1.6 — "For new ADRs: use the `/new-adr` command".
CLAUDE.md §6.3 mekanism 3 gör agent-rapporter + beslutstrail till
granskningsersättningen för PR-flödet. En ADR-referens som pekar på en
icke-existerande fil bryter den trailen: nästa Claude/Klas kan inte verifiera
CTO-rond-besluten, Variant B-valet eller `description-LIKE`-borttagningens
EXPLAIN-ANALYZE-grund. Detta är ett arkitekturbeslut (lager-flytt) — DoD §9 och
§1.6 kräver ADR.

Krävs: skapa `docs/decisions/0062-*.md` (FTS-hybrid + IJobAdSearchQuery-port)
via `/new-adr` och lägg in i README-index — i samma commit-batch som koden.
Om ADR:n redan finns oincheckad lokalt: inkludera den i staging.
Delegera till: adr-keeper (`/new-adr`) / Klas.

### M2 — `JobAdSearchQuery` importerar `Microsoft.EntityFrameworkCore` och exponerar `EF.Property`-strängar; verifiera arch-testtäckning av den nya impl-filen

Fil: `src/JobbPilot.Infrastructure/JobAds/JobAdSearchQuery.cs`

Impl:en ligger korrekt i Infrastructure och får importera EF Core + Npgsql.
`JobAdSearchLayerTests` verifierar tre saker positivt (port i Application,
criteria i Application, impl `internal`). Det negativa förbudet — att Application
**inte** drar in Npgsql/EF-relational — delegeras enligt fil-kommentaren till
`TaxonomyAclLayerTests.Application_should_not_depend_on_Npgsql_or_EF_relational`.

Det testet kontrollerar Application-assemblyns referenser generellt. Eftersom
`JobAdSearch.cs` (gamla statiska modulen) tas bort och hela FTS-LINQ:en flyttas,
är detta nettoresultat korrekt — men granskningen kan inte mot enbart diffen
bekräfta att `TaxonomyAclLayerTests` faktiskt fångar `NpgsqlTypes`-importen
specifikt (impl:en importerar `NpgsqlTypes` för `NpgsqlTsVector`). Den gamla
`JobAdSearch.cs` undvek medvetet `EF.Functions.ILike` just för att den låg i
Npgsql-extensionen.

Krävs: bekräfta att `TaxonomyAclLayerTests`-regeln täcker både
`Npgsql.EntityFrameworkCore.PostgreSQL` **och** `NpgsqlTypes`-assemblyn (de är
separata paket). Om regeln endast matchar EF-relational: utöka den, eller lägg
ett explicit negativt assert i `JobAdSearchLayerTests`
(`Application should not reference NpgsqlTypes`). 73 arch-tester gröna bevisar
inte att just denna regression-vektor är låst — den nya impl-filen är första
stället `NpgsqlTypes` legitimt får importeras, så förbudssidan måste vara skarp.
Delegera till: test-writer / dotnet-architect (verifiering).

---

## Minor (nice-to-fix)

### m1 — Död `pattern`-variabel i `ApplyCriteria` när `Q` enbart matchar via FTS-grenen

Fil: `src/JobbPilot.Infrastructure/JobAds/JobAdSearchQuery.cs:119`

`var pattern = $"%{q.ToLowerInvariant()}%";` byggs alltid i `q`-grenen och
används i `EF.Functions.Like(j.Title.ToLower(), pattern)`. Det är korrekt — men
notera att `j.Description` inte längre refereras någonstans i `ApplyCriteria`.
Den utförliga blockkommentaren (rad 89–94) förklarar väl varför description-LIKE
togs bort. Inget dött fält här — bara en notering att kommentaren rad 113–118
fortfarande pratar om ".ToLower() … translateras till SQL LOWER" vilket är
korrekt. Ingen åtgärd krävd på koden; behåll kommentaren. Markerad endast för
att granskningen ska vara explicit att description-grenen är borta enligt design
(ADR 0062-regressionstestet `ApplyCriteria_DescriptionMidWordSubstring_DoesNotMatch`
bevisar det).

Åtgärd: ingen — verifierad korrekt. (Listas för spårbarhet.)

### m2 — Coverage-flytt unit→integration: bevaka §7

Filer: `ListJobAdsQueryHandlerTests.cs` (omskriven),
`ListJobAdsMultiFilterTests.cs` (Batch 4-test borttaget),
`ListJobAdsFtsTests.cs` (ny).

Den omskrivna `ListJobAdsQueryHandlerTests` testar nu enbart adapter-kontraktet
(7 tester) — sort-/filter-/relevans-assertions flyttades till `ListJobAdsFtsTests`
(integrationssvit, 11 tester) och `ListJobAdsMultiFilterTests` (18). Det är
**rätt** beslut: efter lager-flytten är handlern en tunn adapter och
sort/FTS-logiken kan bara meningsfullt testas mot riktig Postgres (InMemory
stödjer inte `NpgsqlTsVector`/`ts_rank`). Beteendet bevaras — `ListJobAdsFtsTests`
test 7 (`ApplyCriteria_PublishedAtDesc/Asc`, `ExpiresAt*`) återskapar explicit
den sort-coverage som lämnade unit-testet, och kommentarerna refererar §7.

CLAUDE.md §7 säger "PR med sänkt coverage på Domain: motiverat eller avvisat".
Domain-coverage är oförändrad (ingen Domain-kod rörd). Application-handler-raderna
minskar för att logik flyttade till Infrastructure — den koden är nu täckt av
integrationssviten. Nettotäckningen sänks inte; den flyttar lager med koden.
Motiveringen finns i fil-kommentarerna. Godkänt — listas för att granskningen
ska vara explicit att §7 prövats och håller.

Åtgärd: ingen — verifierad. Säkerställ att den borttagna Batch 4-relevanstestets
intention (exakt/prefix/contains är *medvetet* ersatt av ts_rank, inte tappad)
står i ADR 0062 (se M1).

### m3 — `IModelCustomizer`-strippen i `TestAppDbContextFactory` — sund men bör snävas i assert

Fil: `tests/JobbPilot.Application.UnitTests/Common/TestAppDbContextFactory.cs`

`IgnoreSearchVectorModelCustomizer` tar bort `SearchVector`-shadow-propertyn ur
InMemory-modellen eftersom InMemory-providern inte stödjer `NpgsqlTsVector`.
Lösningen är ren: den ärver `ModelCustomizer`, kör `base.Customize` först, och
rör **endast** `JobAd.SearchVector`. Den kontaminerar inte annan unit-test-
täckning — `RemoveProperty` är scopad till en namngiven property på en
entitetstyp, och `FindProperty` returnerar null-safe om propertyn saknas
(framtidssäkert om mappningen ändras). Produktions-DbContext (Npgsql) och
integrationstester rör detta inte. Korrekt.

En liten skärpning: `if (searchVector is not null)` följt av `jobAd!` — om
`SearchVector`-mappningen någon gång tas bort ur `JobAdConfiguration` blir denna
customizer tyst en no-op och ingen märker att den blivit onödig. Överväg en
kort kommentar eller ett `Debug.Assert` att propertyn förväntas finnas, så
strippen inte blir tyst död kod vid framtida mappnings-ändring. Låg prioritet.

Åtgärd: valfri kommentar/assert. Delegera till: test-writer (om Klas vill ha den).

### m4 — XML-doc-cref:ar mot `IJobSource`/`ITaxonomyReadModel` — verifierade OK

Fil: `IJobAdSearchQuery.cs`, `JobAdSearchLayerTests.cs`

`<see cref="IJobSource"/>` och `<see cref="ITaxonomyReadModel"/>` löser mot
`Application/JobAds/Abstractions/` — samma namespace, giltiga cref:ar, ingen
varning. `JobAdSearchLayerTests` cref:ar
`TaxonomyAclLayerTests.Application_should_not_depend_on_Npgsql_or_EF_relational`
— verifiera att den medlemmen fortfarande heter exakt så (kopplat till M2).

Åtgärd: ingen — verifierad (utom M2-beroendet).

---

## Bra gjort

- **Lager-flytten är korrekt riktad.** Porten Application-ägd, impl `internal
  sealed` i Infrastructure, ren DTO (`PagedResult<JobAdDto>`) över gränsen —
  ingen EF-entity läcker (CLAUDE.md §2.1, §5.1). Speglar medvetet det etablerade
  `IJobSource`/`ITaxonomyReadModel`-mönstret.
- **SPOT som kompilator-garanti.** `JobAdFilterCriteria` som egen typ —
  `SearchAsync` och `CountAsync` konsumerar samma typ och kan inte divergera.
  `ApplyCriteria(JobAdFilterCriteria)` är den enda filter-vägen, delad av tre
  konsumenter. ADR 0039 Beslut 1 hålls. Introduce Parameter Object korrekt
  tillämpat.
- **Genuint tunna adaptrar.** `ListJobAdsQueryHandler` är ren mappning.
  `RunSavedSearchQueryHandler` behåller auth/cross-tenant-logiken (ADR 0031
  failed-access-detection) lokalt och delegerar bara sök-kompositionen — rätt
  ansvarsfördelning, ingen logik tappad.
- **LINQ-hygien (§3.6).** `AsNoTracking()` default, separat `CountAsync`-query
  före paginering, projektion direkt till `JobAdDto`, `Skip/Take`. Inget
  `SELECT *`, inget `IQueryable` läcker ut.
- **Inga §5.1-anti-patterns.** `TextSearchConfig = "swedish"` är const (ingen
  magisk sträng), inga döda fält, ingen Repository-abstraktion (`IAppDbContext`
  direkt), `IDateTimeProvider` injicerat överallt i testseeden, inga
  `DateTime.UtcNow`, inget `dynamic`, inga tomma catch.
- **CS8524-disciplin.** `ApplySort`-switch listar alla `JobAdSortBy`-värden +
  `throw` på okänt — fail-fast vid framtida enum-tillägg.
- **DI i samma commit som impl** (`feedback_di_with_handlers_same_commit`) —
  `AddScoped` med motiverad livscykel (Scoped = paritet med `IAppDbContext`,
  korrekt kontrast mot singleton-`ITaxonomyReadModel`).
- **Migration korrekt.** Partial-predikat `WHERE deleted_at IS NULL` matchar
  query-filtret (global query filter `DeletedAt == null`) — Postgres kan använda
  partiella indexet. `Down` dropp:ar index + kolumn, rör inte pg_trgm. STORED
  generated column, ingen ny extension. `JobAdConfiguration` + snapshot
  konsistenta.
- **Shadow-property-disciplin.** `NpgsqlTsVector` är provider-typ — korrekt
  hållen utanför `JobAd`-aggregatet som shadow-property (CLAUDE.md §2.1).
- **Negativt regressionstest.** `ApplyCriteria_DescriptionMidWordSubstring_
  DoesNotMatch_RegressionGate` bevisar att description-LIKE-borttagningen är
  avsiktlig och har en kontroll-assertion (anchor i titeln) som utesluter
  falsk-grön p.g.a. misslyckad seed. Genomtänkt.

---

## Delegationer

| Fynd | Åtgärd | Ägare |
|---|---|---|
| M1 | Skapa `docs/decisions/0062-*.md` + README-index, samma commit-batch | adr-keeper (`/new-adr`) / Klas |
| M2 | Verifiera/utöka arch-regel mot `NpgsqlTypes` i Application | test-writer / dotnet-architect |
| m3 | Valfri assert/kommentar i `IgnoreSearchVectorModelCustomizer` | test-writer (om Klas vill) |

---

## Verdikt

Koden är arkitektoniskt sund och commit-redo så snart **M1** är åtgärdat —
en lager-flytt utan ADR bryter granskningstrailen som CLAUDE.md §6.3 gör till
PR-ersättning, och §1.6/§DoD §9 kräver ADR för arkitekturbeslut. **M2** bör
verifieras parallellt (snabb kontroll, troligen redan täckt). m1/m2/m4 är
verifierade utan åtgärd; m3 är valfri.

Inga Blockers. Re-review behövs inte om M1 är en ren ADR-tillägg och M2
bekräftar befintlig täckning — bifoga ADR 0062 + arch-regel-bekräftelse till
STOPP-rapporten så Klas kan granska parallellt.

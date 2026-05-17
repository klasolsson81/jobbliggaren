# STOPP 3a — testflytt InMemory→Npgsql (J3-rapport, test-writer)

**Datum:** 2026-05-17 (körd 2026-05-18)
**Roll:** test-writer (testflytt per senior-cto-advisor rev2 (B))
**Auktoritativ spec:** `2026-05-17-fas3-stopp3a-divergence-cto-2.md` §3/§4
(ae08b1fa4a1d99520) · cto-1 · architect-design · ADR 0048
**Status:** Klar. Inga commits (CC committar i push 3a.5). Inga prod-filer rörda.

---

## 1. Sammanfattning

De 4 read-handler-testklasserna flyttade från
`tests/JobbPilot.Application.UnitTests/Applications/Queries/` (EF InMemory via
`TestAppDbContextFactory`) → `tests/JobbPilot.Api.IntegrationTests/Applications/`
(Npgsql/Testcontainers, befintlig `ApiFactory`-infra). Alla scenarier +
assertions semantiskt oförändrade. Testnamn bevarade 1:1 (klassnamn fick
`Integration`-suffix per projektets konvention; metodnamn oförändrade).
**0 prod-filer ändrade. `TestAppDbContextFactory` orörd.**

Körresultat: 32 flyttade integrationstester GRÖNA mot Npgsql; 470
Application.UnitTests GRÖNA (ingen regression efter borttagning).

---

## 2. Filer

**Skapade (integration, Npgsql/Testcontainers):**

- `tests/JobbPilot.Api.IntegrationTests/Applications/GetApplicationsQueryHandlerIntegrationTests.cs`
- `tests/JobbPilot.Api.IntegrationTests/Applications/GetPipelineQueryHandlerIntegrationTests.cs`
- `tests/JobbPilot.Api.IntegrationTests/Applications/GetApplicationByIdQueryHandlerIntegrationTests.cs`
- `tests/JobbPilot.Api.IntegrationTests/Applications/ReadHandlerManualPostingFallbackIntegrationTests.cs`

**Borttagna (InMemory-dubbletter, helt — ingen kvar-dubblering):**

- `tests/JobbPilot.Application.UnitTests/Applications/Queries/GetApplicationsQueryHandlerTests.cs`
- `tests/JobbPilot.Application.UnitTests/Applications/Queries/GetPipelineQueryHandlerTests.cs`
- `tests/JobbPilot.Application.UnitTests/Applications/Queries/GetApplicationByIdQueryHandlerTests.cs`
- `tests/JobbPilot.Application.UnitTests/Applications/Queries/ReadHandlerManualPostingFallbackTests.cs`

Verifierat: `0` kvarvarande referenser till de 4 klassnamnen i `tests/`.
Gamla `Applications.Queries`-namespacet: `Total: 0` tester vid discovery.
`Queries`-mappen tom (SDK-style projekt, ingen `.csproj`-redigering behövd).

---

## 3. Scenario-mappning (gammalt → nytt) — alla bevarade

Metodnamn oförändrade i samtliga fall (spårbar täckning, ADR 0044).

### GetApplicationsQueryHandlerTests → ...IntegrationTests (7 tester)
`Handle_WhenUserIdIsNull_ReturnsEmptyPagedResult` ·
`Handle_WhenJobSeekerNotFound_ReturnsEmptyPagedResult` ·
`Handle_WithNoStatusFilter_ReturnsAllApplications` ·
`Handle_WithStatusFilter_ReturnsOnlyMatchingApplications` ·
`Handle_WithSubmittedStatusFilter_ExcludesDraftApplications` ·
`Handle_DoesNotReturnApplicationsBelongingToOtherJobSeeker` ·
`Handle_TotalCount_IsIndependentOfPageSize`

### GetPipelineQueryHandlerTests → ...IntegrationTests (5 tester)
`Handle_WhenNoApplications_ReturnsEmptyList` ·
`Handle_WhenUserIdIsNull_ReturnsEmptyList` ·
`Handle_WithApplicationsOfDifferentStatuses_GroupsByStatus` ·
`Handle_WithSingleApplication_ReturnsSingleGroup` ·
`Handle_DoesNotReturnApplicationsBelongingToOtherJobSeeker`

### GetApplicationByIdQueryHandlerTests → ...IntegrationTests (8 tester)
`Handle_WhenApplicationExists_ReturnsApplicationDetailDto` ·
`Handle_WhenApplicationExists_PopulatesFollowUps` ·
`Handle_WhenApplicationExists_PopulatesNotes` ·
`Handle_WhenApplicationNotFound_ReturnsNull` ·
`Handle_WhenApplicationBelongsToOtherUser_ReturnsNull` ·
`Handle_WhenApplicationBelongsToOtherUser_LogsFailedAccessAttempt` ·
`Handle_WhenApplicationIdUnknown_DoesNotLogFailedAccessAttempt` ·
`Handle_WhenUserIdIsNull_ReturnsNull`

### ReadHandlerManualPostingFallbackTests → ...IntegrationTests (12+1 tester)
`GetApplications_WithJobAdLinked_ProjectsJobAdSummaryFromJobAd` ·
`GetApplications_WithManualPosting_ProjectsManualSourceAndNullPublishedAt` ·
`GetApplications_WithNeitherJobAdNorManual_ProjectsNullJobAd` ·
`GetApplications_DoesNotLeakOtherUsersApplications` ·
`GetApplicationById_WithJobAdLinked_ProjectsJobAdSummaryFromJobAd` ·
`GetApplicationById_WithManualPosting_ProjectsManualSourceNullPublishedAt` ·
`GetApplicationById_WithNeither_ProjectsNullJobAdButKeepsDetail` ·
`GetApplicationById_CrossUserAccess_ReturnsNullAndLogsAttempt` ·
`GetPipeline_WithManualPosting_ProjectsManualSourceNullPublishedAt` ·
`GetPipeline_WithJobAdLinked_ProjectsJobAdSummaryFromJobAd` ·
`GetPipeline_WithNeither_ProjectsNullJobAd`
**+ tillagd:** `GetApplications_WithSoftDeletedJobAd_FallsBackToNullViaQueryFilter`
(se §5 — addition, ej 1:1-flytt).

---

## 4. Konverteringsmönster

- `TestAppDbContextFactory.Create()` (InMemory) →
  `_factory.Services.CreateScope()` + `GetRequiredService<AppDbContext>()`
  (Npgsql) — kopierat verbatim från `ManualPostingPersistenceTests.cs`
  (redan grön mot Npgsql).
- `FakeDateTimeProvider.Default` (fast 2026-04-19) →
  `GetRequiredService<IDateTimeProvider>()` (DI-registrerad
  `DateTimeProvider`, samma som ManualPostingPersistenceTests). Assertioner
  på datum refererar endast provider-oberoende fasta konstanter
  (`JobAdPublishedAt = 2026-04-01`, ManualPosting `ExpiresAt = 2026-07-01`)
  → bevarade 1:1. `JobAd.ValidateCore` begränsar endast
  `expiresAt > publishedAt`; fast dåtidsdatum är giltigt.
- Soft-deletad JobAd: `db.Entry(jobAd).Property(nameof(JobAd.DeletedAt))
  .CurrentValue = clock.UtcNow` — SAMMA mekanism som den redan fixade
  `ManualPostingPersistenceTests` (JobAd saknar domän-SoftDelete).
- `[Collection("Api")]` + `ApiFactory`-ctor-injektion. Primary-constructor +
  parameterlös testctor är ogiltig C# (CS8862) → klassisk ctor som tar
  `ApiFactory` och sätter `_currentUser.UserId`-stub (NSubstitute).
- `using DomainApplication = JobbPilot.Domain.Applications.Application;` per
  fil (Application.UnitTests har det som `global using`; integrationsprojektet
  saknar global alias).

**Cross-user/auth (ADR 0031) — bevarad 1:1:** integrationsfixturen har ingen
scope:ad `ICurrentUser` för dessa Application-query-handlers (greppat: inga
befintliga Api.IntegrationTests konstruerar dessa handlers). Handlern
konstrueras därför direkt med NSubstitute `ICurrentUser`/`IFailedAccessLogger`
— **identiskt med unit-sviten**, bara mot Npgsql-`AppDbContext` istället för
InMemory. Handler- och auth-logik är oförändrad → ADR 0031-scoping verifieras
exakt som förut (cross-user → null + `LogCrossUserAttempt` Received(1); okänt
id → DidNotReceive). User-scoping mot delad Testcontainers-Postgres
([Collection("Api")]) är naturligt isolerad: varje test seedar unik
`Guid.NewGuid()`-user → handlern filtrerar på `JobSeekerId` → inga
främmande rader i resultatet (SQL-verifierat:
`WHERE ... j.user_id = @currentUser_UserId_Value`).

---

## 5. Avvikelse-flagga (1 addition, ingen assertion förlorad)

**Tillagt test (ej 1:1-flytt):**
`ReadHandlerManualPostingFallbackIntegrationTests
.GetApplications_WithSoftDeletedJobAd_FallsBackToNullViaQueryFilter`.

Uppdragets KRAV-lista + spec §3.1/§4.3 namnger uttryckligen "soft-deletad
JobAd→fallback" som scenario som ska täckas. Det scenariot fanns **inte** som
read-handler-test i någon av de 4 källfilerna (det fanns endast som
query-nivå-test i `ManualPostingPersistenceTests.cs`,
`ReadJoin_WithSoftDeletedJobAd_...`, vilket INTE flyttas — den filen rörs ej).
Anledningen är exakt divergensen: query-filter-på-join hedras ej av InMemory,
så scenariot kunde aldrig uttryckas på unit-nivå. Jag har därför **lagt till**
det som handler-nivå-integrationstest (stärker täckning på rätt nivå, tar inte
bort något). Detta är en addition utöver 1:1-flytten — flaggas för
medvetenhet; ingen assertion från källfilerna förlorad eller försvagad.

**Ingen assertion kunde inte bevaras 1:1.** Alla ursprungliga
scenarier/assertions överförda exakt. Inget STOPP-villkor (spec §6
re-invoke) utlöst: de flyttade fakta failar ej mot Npgsql, och de gröna
körningarna bekräftar converter+LEFT JOIN-translation (rotorsak = enbart
provider-divergens, ej defekt i ADR 0048-joinen).

---

## 6. Körresultat

**Build:** `dotnet build` båda projekt — `0 Warning(s) 0 Error(s)`.

**Flyttade integrationstester (Npgsql/Testcontainers, Docker 29.0.1):**

```
=== TEST EXECUTION SUMMARY ===
   JobbPilot.Api.IntegrationTests  Total: 32, Errors: 0, Failed: 0,
   Skipped: 0, Not Run: 0, Time: 11,216s
```

Filter: `-filter "/*/*/GetApplicationsQueryHandlerIntegrationTests/*"`
(+ 3 övriga klasser). Alla 32 GRÖNA mot riktig Npgsql.

**Genererad SQL (verbatim, GetApplications — ADR 0048-gate-bevis):**

```sql
SELECT a0.id, a0.job_seeker_id, j0.id, a0.status, a0.created_at,
       a0.updated_at, j0.id IS NOT NULL, j0.title, j0.company_name,
       j0.url, j0.source, j0.published_at, j0.expires_at,
       a0.manual_company IS NOT NULL AND a0.manual_title IS NOT NULL,
       a0.manual_title, a0.manual_company, a0.manual_url,
       a0.manual_expires_at
FROM (
    SELECT a.id, a.created_at, a.job_ad_id, a.job_seeker_id, a.status,
           a.updated_at, a.manual_company, a.manual_expires_at,
           a.manual_title, a.manual_url
    FROM applications AS a
    WHERE a.deleted_at IS NULL AND a.job_seeker_id = @jobSeekerId
    ORDER BY a.updated_at DESC
    LIMIT @p
) AS a0
LEFT JOIN (
    SELECT j.id, j.expires_at, j.published_at, j.source, j.title,
           j.url, j.company_name
    FROM job_ads AS j
    WHERE j.deleted_at IS NULL
) AS j0 ON a0.job_ad_id = j0.id
ORDER BY a0.updated_at DESC
```

Bekräftar architect (D)-kanonisk form: **EN** `LEFT JOIN` mot `job_ads`,
JobAd:s query-filter (`j.deleted_at IS NULL`) applicerat på join-grenen
(ADR 0048 c — inget `IgnoreQueryFilters`, inget manuellt DeletedAt-predikat),
join-härledd projektion. Ingen shadow-FK (rivet av CC). Ingen N+1.

**Application.UnitTests (regressionsvakt efter borttagning):**

```
=== TEST EXECUTION SUMMARY ===
   JobbPilot.Application.UnitTests  Total: 470, Errors: 0, Failed: 0,
   Skipped: 0, Not Run: 0, Time: 1,678s
```

Gamla `Applications.Queries`-namespace vid discovery: `Total: 0` (inga
orphans, ingen kvar-dubblering). `TestAppDbContextFactory` orörd — 38 övriga
filer som delar factoryn opåverkade ((C):s 42-fil-risk undviken helt).

---

## 7. Disciplin-noter

- Inga prod-filer rörda (test-writer-gräns: endast `tests/**`). Config-
  rivning + handler-join-återställning + Zod/DTO ligger i CC:s push 3a.5
  (J3 atomisk) — denna rapport täcker enbart testflytten.
- Inga commits gjorda (CC committar pathspec-scoped).
- `TestAppDbContextFactory.cs` ej rörd (delas av ~42 filer; (C) avvisad).
- `ManualPostingPersistenceTests.cs` ej rörd (dess soft-delete-/SQL-test
  ligger kvar där; ingen dubblering med flyttade fakta).
- Coverage ej sänkt (ADR 0044): alla scenarier täcks fortsatt, nu på
  integrationsnivå + 1 tillagt soft-delete-fallback-fakta.

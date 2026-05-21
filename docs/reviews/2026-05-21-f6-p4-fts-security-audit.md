# Security-audit: F6 P4 FTS-skifte (ADR 0062)

**Status:** APPROVED — inga blockers, inga critical, inga high
**Granskat:** 2026-05-21
**Auktoritet:** CLAUDE.md §5.1/§5.4, GDPR Art. 5/17/32, ADR 0001/0031/0049/0061
**Granskare:** security-auditor (pre-commit, CLAUDE.md §9.2)

## Scope

Ostagade ändringar för PostgreSQL full-text-search-hybrid på jobbannons-sök.
Sök-kompositionen flyttad Application→Infrastructure bakom `IJobAdSearchQuery`.
Ny migration `F6P4FtsSearchVector` lägger STORED generated `tsvector`-kolumn +
partial GIN-index. Granskade områden: SQL-injektion (Område 7), LIKE-wildcard-DoS
(Område 7), GDPR/PII (Område 1+4), auth/cross-tenant (Område 3), logging-hygien
(Område 6), migration-PII-yta (Område 1).

## Fynd per severity

### Block — inga

### Critical — inga

### High — inga

### Medium — inga

### Low

**L1 — `q` med LIKE-metatecken (`%`/`_`) tolkas fortfarande literalt i
title-LIKE-grenen (pre-existing, oförändrad attack-yta)**
Fil: `src/JobbPilot.Infrastructure/JobAds/JobAdSearchQuery.cs:119,124`
`var pattern = $"%{q.ToLowerInvariant()}%"` → `EF.Functions.Like(j.Title.ToLower(), pattern)`.
Ett `q` som `%%%%%` blir `%%%%%%%` och ger en bredmatchande LIKE. Detta är
**inte en regression** — borttagna `JobAdSearch.cs` gjorde exakt samma
(`$"%{q}%"` mot title OCH description). FTS-skiftet *minskar* faktiskt ytan:
description-LIKE-grenen är borttagen, och title är en kort kolumn (max 300,
ix_job_ads_title_lower_trgm GIN-trigram, ingen TOAST de-toast). Kombinerat med
`ListJobAdsQueryValidator` q-cap 2–100 tecken är resurs-impact begränsad.
Ingen åtgärd krävs i denna commit. Om defense-in-depth önskas senare: escapa
`%`/`_` innan pattern-bygget (`q.Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_")`
+ `Like(..., pattern, "\\")`). Lyft inte som TD — opportunistisk touch.

## Verifierat OK

### SQL-injektion (Område 7) — ren

- `EF.Functions.WebSearchToTsQuery(TextSearchConfig, q)` — `q` passeras som
  argument till en EF.Functions-translation. Npgsql genererar en parametriserad
  `websearch_to_tsquery(@p, @q)`; `q` binds, konkateneras aldrig in i SQL-text.
  `websearch_to_tsquery` kastar dessutom aldrig på dålig syntax (till skillnad
  från `to_tsquery`) — robust mot user-input.
- `TextSearchConfig = "swedish"` är en hårdkodad `const string`, ingen
  injektionsyta. Matchar `to_tsvector('swedish', …)` i både migration och
  `JobAdConfiguration` — korrekt (config-mismatch skulle ge tyst index-miss,
  inte säkerhetshål).
- title-LIKE: `pattern` byggs C#-side och fångas i expression-tree → binds som
  parameter (`LIKE @p`). Ingen konkatenering.
- `EF.Property<NpgsqlTsVector>(j, "SearchVector")` och `EF.Property<string?>(j, "SsykConceptId"/"RegionConceptId")`
  — shadow-prop-namn är hårdkodade strängkonstanter, ingen user-input-yta.
- `ssykValues.Contains(...)` / `regionValues.Contains(...)` → parametriserad
  `IN (@p0, @p1, …)`. Listorna är cap:ade av `SearchCriteria.MaxConceptIds` +
  regex-validerade (`^[A-Za-z0-9_-]{1,32}$`) i `ListJobAdsQueryValidator`.

### GDPR / PII (Område 1 + 4) — ren

- `search_vector` härleds STORED från `title` + `description` på `job_ads`.
  `job_ads` är publik annonstext från JobTech (ADR 0032), **inte** en
  PII-encrypted aggregat. ADR 0049 listar exakt fem PII-kolumner som kräver
  KMS-envelope: `applications.cover_letter`, `application_notes.content`,
  `follow_ups.note`, `resume_versions.content`, `job_ads.raw_payload`.
  `title`/`description` är **inte** på den listan — `search_vector` rör
  ingen PII och ingen krypterad kolumn. Ingen ny PII-kategori, ingen ADR/
  privacy-policy-uppdatering krävs.
- `search_vector` rör **inte** `job_ads.raw_payload` (ADR 0049 PII-yta) —
  generated-uttrycket refererar bara `title` och `description`. Verifierat i
  både migration (`computedColumnSql`) och `JobAdConfiguration` snapshot.
- **Art. 17 / soft-delete:** partial-index `WHERE deleted_at IS NULL`. En
  soft-deletad annons får ingen index-entry → ingen FTS-träff. `JobAdConfiguration`
  har dessutom `HasQueryFilter(j => j.DeletedAt == null)` → query-WHERE
  implicerar index-WHERE, planneren kan nyttja partial-indexet OCH erasure
  respekteras. STORED generated-kolumnen finns kvar på soft-deletade rader men
  är inte sökbar via indexet och filtreras bort av global query filter —
  korrekt soft-delete-semantik. Hard-delete (Art. 17-cascade, ADR 0024)
  tar bort raden inkl. `search_vector` automatiskt.

### Auth / cross-tenant (Område 3) — oförändrad, ingen regression

- `RunSavedSearchQueryHandler` rad 19–55 är **byte-identisk** med pre-refactor
  (verifierat mot `git show HEAD`): `currentUser.UserId.HasValue`-guard,
  `JobSeekers.Where(js => js.UserId == currentUser.UserId.Value)`,
  `SavedSearches.Where(s => s.Id == … && s.JobSeekerId == jobSeekerId)`,
  och ADR 0031 failed-access-grenen (`AnyAsync`-existens-check +
  `LogCrossUserAttempt` endast vid existerande-men-cross-tenant).
- Endast sök-kompositionen (rad 57–68) ändrades: `JobAdSearch.ApplyCriteria/ApplySort`
  → `search.SearchAsync(...)`. Refaktorn rör inte ownership-checken. Den tunna
  adaptern delegerar redan **efter** auth-grinden — `criteria` hämtas bara om
  ownership matchat.
- `ListRecentSearchesQueryHandler` opererar fortfarande på `currentUser`-scopad
  data (`r.Ssyk/Region/Q` från egna RecentJobSearch-rader); `CountAsync` räknar
  publika `job_ads` — ingen tenant-data, ingen IDOR-yta.
- `IJobAdSearchQuery` exponerar bara publik annonsdata; ingen tenant-scoping
  behövs i porten själv (job_ads är inte user-ägt).

### Logging-hygien (Område 6) — ren

- Ingen `ILogger`-användning i `JobAdSearchQuery`, `IJobAdSearchQuery`,
  criteria-records eller någon av de tre ändrade handlers. `q` och sök-data
  loggas inte i klartext. CLAUDE.md §5.1 (loggning av känslig data) ej brutet.
- `IFailedAccessLogger.LogCrossUserAttempt` loggar bara `aggregateType`,
  `aggregateId` (Guid), `requestingUserId` (Guid), `operation` — oförändrat,
  ingen PII, per ADR 0031.

### Migration (Område 1) — ren

- Ingen ny PII-kolumn. `search_vector` är en derived FTS-artefakt av publik text.
- Ingen krypteringskringgång — `search_vector` rör inte `raw_payload` eller
  någon KMS-envelope-kolumn (ADR 0049).
- `to_tsvector`/GIN är core PostgreSQL 16+, ingen ny extension (pg_trgm finns
  redan via ADR 0061). Down-migration drop:ar index + kolumn rent.
- GIN-index via raw SQL — partial-predikat hårdkodat (`WHERE deleted_at IS NULL`),
  ingen user-input, ingen injektionsyta.

### Clean Arch / lager-isolering — korrekt

- `JobAdSearchQuery` är `internal sealed`, Npgsql-FTS-LINQ inkapslad i
  Infrastructure. Porten `IJobAdSearchQuery` ligger i Application.
  `JobAdSearchLayerTests` är anti-regressions-grind. Inget säkerhetsproblem,
  noteras som korrekt hantering av provider-bunden kod.
- DI registrerad i samma commit som port-impl (`DependencyInjection.cs`),
  `AddScoped` — delar request-scopets `IAppDbContext`, korrekt livscykel.

## Sammanfattning

FTS-skiftet är säkerhetsmässigt rent. Inga blockers, critical, high eller medium.
Den enda noteringen (L1, Low) är en **pre-existing och faktiskt minskad**
LIKE-metatecken-yta — title-LIKE-grenen ärver mönstret från borttagna
`JobAdSearch.cs`, medan den DoS-tunga description-LIKE-grenen tas bort helt.
q-input cap:ad 2–100 tecken av validatorn. `websearch_to_tsquery` parametriserar
`q` korrekt. `search_vector` rör ingen PII och ingen krypterad kolumn.
Soft-delete/Art. 17 respekteras via partial-index + global query filter.
RunSavedSearch-auth är byte-identisk efter refaktorn — ingen IDOR-regression.

**Mergeklar.** Ingen GDPR-eskalering till Klas krävs.

# dotnet-architect — Rename PR review (JobbPilot → Jobbliggaren)

**Datum:** 2026-06-13
**Branch:** `refactor/rename-jobbliggaren` (HEAD `e6b97ed`, base `main` `3f33474`)
**Scope:** ADR 0069 — atomic product rename `JobbPilot.*` → `Jobbliggaren.*` (~1233 files touched, mechanical namespace/identifier rename)
**Reviewer role:** Clean Architecture integrity + project-graph soundness post-rename + csproj reconstruction verification

---

## Verdict

**GODKÄND — inga Blocker, inga Major, inga Minor.**

The architecture survived the mechanical rename intact. The `.sln` + 13 `.csproj`
reconstruction (after the C# Dev Kit BuildHost corruption) is graph-correct: every
layer dependency is one-directional and matches the pre-rename topology. No EF
migration was added; no schema/table/column carries the brand. The only
behavioural delta is the CTO-approved test-isolation fix in two integration-test
fixtures — production code is a pure rename.

This is a confirmation review: the green oracle (Release build 0/0, Architecture.Tests
78 facts PASS, Api.IntegrationTests 476/0, FE green, `dotnet format` clean) already
constrains correctness. Findings below are the independent architectural confirmation
the oracle cannot give on its own (that the arch-tests still *guard* rather than
pass vacuously, and that the reconstructed graph has no hidden inversion).

---

## 1. Clean Architecture integrity post-rename — CONFIRMED

The layering invariants hold under the new name. Crucially, the Architecture.Tests
assertions were renamed consistently, so they **still actually guard** (not vacuously
passing):

- `tests/Jobbliggaren.Architecture.Tests/DomainLayerTests.cs:11–22` — Domain forbidden
  from depending on `Microsoft.EntityFrameworkCore`, `Microsoft.AspNetCore`, `Mediator`,
  `FluentValidation`, and `Jobbliggaren.Application/Infrastructure/Api/Worker`. The
  forbidden-namespace strings were renamed to `Jobbliggaren.*` — the assertion targets
  the real new namespaces, so a Domain leak would still fail it.
- `DomainLayerTests.cs:29–63` — Application↛Infrastructure, Application↛AspNetCore,
  Application↛Api/Worker; all forbidden strings renamed.
- `DomainLayerTests.cs:66–84` — Application MAY depend on EF Core core abstractions
  (ADR 0009 deliberate compromise) but NOT on concrete providers (`Npgsql`,
  `*.Relational`, `*.SqlServer`, `*.Sqlite`). Intact.
- `WorkerLayerTests.cs:69–71` — `RecurringJobRegistrar.Assembly.GetName().Name`
  pinned to the **string** `"Jobbliggaren.Worker"`. This is the kind of assertion a
  rename can silently break (string literal, not a type ref); it was updated and the
  build+test pass proves the assembly name matches it.
- `WorkerLayerTests.cs:74–99` — Mediator pipeline order (`LoggingBehavior` →
  `ValidationBehavior` → `AuthorizationBehavior` → `AdminAuthorizationBehavior` →
  `FieldEncryptionKeyPrefetchBehavior` → `UnitOfWorkBehavior` →
  `RecentJobSearchCaptureBehavior` → `AuditBehavior`) unchanged. Pure rename did not
  reorder or drop a behavior.
- `JobAdSearchLayerTests.cs` + `PagedResultContractTests.cs` resolve
  `Jobbliggaren.Application.AssemblyMarker` / `Jobbliggaren.Infrastructure.AssemblyMarker`
  by fully-qualified type. These compile only if the markers exist under the new
  namespace — Release build 0/0 is the proof.

The 78 facts reference live `Jobbliggaren.*` types and strings throughout. They are
not vacuously green.

## 2. Project graph soundness (post-reconstruction) — CONFIRMED

`Jobbliggaren.sln` declares all 13 projects with renamed paths and GUIDs preserved.
The reconstructed `<ProjectReference>` graph is correct and contains **no inversion**:

| Project | ProjectReferences | Correct? |
|---|---|---|
| `Jobbliggaren.Domain` | (none) | ✓ depends on nothing |
| `Jobbliggaren.Application` | Domain | ✓ |
| `Jobbliggaren.Infrastructure` | Domain, Application | ✓ |
| `Jobbliggaren.Api` | Application, Infrastructure, Domain | ✓ (composition root) |
| `Jobbliggaren.Worker` | Application, Infrastructure, Domain | ✓ (composition root) |

No stray `Domain → Infrastructure` or any other reverse edge was introduced by the
rebuild. Note: Api/Worker referencing Infrastructure directly is the **established
JobbPilot/Jobbliggaren composition-root topology** (ADR 0009/0010/0023) — the
composition roots wire Infrastructure implementations into DI. The Architecture.Tests
guard the *reverse* direction (`Infrastructure ↛ Api/Worker`,
`DomainLayerTests.cs:87–97`), which passes. The reconstruction did not weaken this.

Package boundaries in the reconstructed csproj are also correct: `Npgsql` /
`*.Relational` / Identity / JWT live only in `Jobbliggaren.Infrastructure.csproj`;
`Jobbliggaren.Application.csproj` carries only EF Core *core* (`Microsoft.EntityFrameworkCore`,
no provider) + `Mediator.Abstractions` + `FluentValidation` + DI abstractions — exactly
what `DomainLayerTests` Fact `Application_should_not_depend_on_EFCore_database_providers`
permits. `InternalsVisibleTo` entries were renamed to the new test-assembly names.

Build 0/0 + 78 arch facts green constrain this graph; this review confirms the
csproj/sln contents match that constraint by inspection.

## 3. EF / persistence — CONFIRMED (ADR 0069 D2 honoured)

- **No EF migration added.** `git diff --diff-filter=AM 3f33474..e6b97ed` on
  `Migrations/*.cs` returns **empty** — every one of the ~80 migration files is a pure
  *rename* (path moved under `Jobbliggaren.Infrastructure`, content byte-identical
  modulo namespace). No new `*_*.cs` migration, no edited `Up`/`Down`, no touched
  `AppDbContextModelSnapshot` content.
- **No brand in schema.** `HasDefaultSchema` appears only as `"identity"`
  (`MigrationsOptionsFactory.cs:43`). No `jobbpilot` / `jobpilot` token anywhere under
  `Persistence/`. No table/column carries the brand. DbContexts remain brand-neutral
  `AppDbContext` / `AppIdentityDbContext`.
- The rename is therefore **connection-string / infra-config only**: the brand moves
  in Postgres role names (`Roles.cs` → `jobbliggaren_migrations` / `jobbliggaren_app` /
  `jobbliggaren_worker`, per ADR 0034 + D2) and JWT `Audience`, never in schema DDL.

## 4. DDD / CQRS — CONFIRMED (pure rename)

- **Zero non-brand production additions.** Filtering every added `src/**/*.cs` line
  against the brand token leaves only `using Mediator;` and verbatim type-declaration
  lines of files git classified as "new" because they paired below its rename-similarity
  threshold (`LoginCommand`, `LogoutCommand`, `RefreshCommand`, `GetCurrentUserQuery`,
  `GetMyProfileQuery`, `GetSavedSearchQuery`, `AssemblyMarker`, `Roles`). Each is
  semantically identical to its pre-rename form (e.g. `LoginCommand` still
  `: ICommand<Result<SessionDto>>`). No aggregate, handler, or value-object semantics
  changed. No public setter introduced (guarded by
  `DomainLayerTests.Domain_aggregates_should_only_have_private_setters`).
- **Only behavioural delta = the CTO-approved test-isolation fix**, scoped to two
  integration-test fixtures (not production):
  - `tests/Jobbliggaren.Api.IntegrationTests/SavedSearches/C2ReverseLookupMigrationTests.cs:44–57`
  - `tests/Jobbliggaren.Api.IntegrationTests/SavedSearches/SearchCriteriaJsonbBackcompatTests.cs:37–48`

  Both add `IAsyncLifetime.InitializeAsync` doing `DELETE FROM saved_searches;` before
  each test. This addresses a latent shared-Testcontainers-fixture coupling (a
  whole-table migration-replay guard that implicitly relied on `[Collection("Api")]`
  name-based execution order) that the rename's reordering surfaced. Correct fix —
  `[Collection]` order is not a contract; a whole-table scan must own its precondition.
  Both fixtures use the brand-neutral `AppDbContext`, reconfirming D2.

---

## Notes for downstream (not blocking this PR)

- ADR 0069 D3 correctly excludes AWS Terraform + `deploy-dev.yml`; the
  ADR-0036 mandatory `dotnet-architect` IaC gate **moves to** the separate AWS-teardown
  PR (TD-104 / ADR 0066). That PR must not proceed without it.
- `Jobbliggaren.Infrastructure.csproj` still references `AWSSDK.KeyManagementService`.
  This is pre-existing and out of scope for a rename PR (local default is
  `LocalDataKeyProvider` per CLAUDE.md §11; AWS KMS path is dormant). Flagging only so
  it is tracked alongside the AWS-teardown work — **not a finding against this PR.**

---

## Sammanfattning

Mechanical rename verified architecturally sound. Layering one-directional and
guarded by live (non-vacuous) Architecture.Tests; reconstructed `.sln` + 13 `.csproj`
graph correct with no inverted edge; no EF migration and no brand in schema (D2 met);
production code a pure rename with the sole behavioural change being a correct,
CTO-approved test-isolation guard in two fixtures. **GODKÄND.**

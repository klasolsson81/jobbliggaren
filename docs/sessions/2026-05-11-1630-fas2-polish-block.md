---
session: Fas 2 Polish-block — arch-audit Minor/Nit-fixar (N-1 + N-3 + H-4 + N-2 + H-3)
datum: 2026-05-11
slug: fas2-polish-block
status: KLAR (väntar Klas-diff-granskning innan push)
commits:
  - (pending) refactor(domain): N-1 + N-3 + H-4 — domain event-konsistens + DomainException + paging-rename
  - (pending) fix(infra): N-2 — IdempotentAdminRoleSeeder prod-gate-hardening
  - (pending) refactor(auth): H-3 — SoC-split role-fetch till IClaimsTransformation
  - (pending) docs: Fas 2 polish-block session-end — N-1/N-2/N-3/H-3/H-4 stängda + TD-58/59/60/61 lyfta
---

# Fas 2 Polish-block — arch-audit-fynd-fix

## Mål

Klas valde Alt 2 (polish-block) efter arch-audit Fas 1 Discovery levererad
2026-05-11 ~12:15. Audit klassade 0 Blocker / 0 Major / 4 Minor / 3 Nit.
Klas-val: kör 5 in-block-fix (H-3, H-4, N-1, N-2, N-3) i en session.
H-1 + H-2 TD:as eftersom de naturligt hör till Fas 6 admin-impersonation.

**Tidsbudget:** ~3h CC-tid. Inga nya features. Refactor + DDD-konsistens-fix.

## Sammanfattning

5 audit-fynd fixade in-block, 4 TDs lyfta för defererade fynd:

- **N-1:** Domain events upprade på `Application.SoftDelete` + `JobSeeker.SoftDelete`
  (Resume hade redan event — konsistens uppåt per CTO Riktning A).
- **N-3:** `DomainException` skapad i `Domain.Common`, `Resume.MasterVersion`
  wrapt i guard med specifik `Code`-string. Middleware-catch i Program.cs
  returnerar 400 med `{ code, error }`-shape.
- **H-4:** `PageNumber` → `Page` i `GetResumesQuery` + `GetApplicationsQuery`
  + validators + handlers + tester. PagedResultContractTests heuristik strikt.
- **N-2:** `IdempotentAdminRoleSeeder` 42P01-catch env-gated på
  `IsDevelopment() || IsEnvironment("Test")` per CTO Alt A (fail-loud i prod).
  Anti-regression-test bevisar prod-bubbling.
- **H-3:** Role-fetch flyttat från `SessionAuthenticationHandler` till ny
  `SessionRoleClaimsTransformation : IClaimsTransformation`. Per-request-fetch
  bibehållen. Arch-test för IClaimsTransformation-konsument-allowlist (analogt
  med audit-bypass-port-pattern) infört.

**TDs lyfta (4):** TD-58 H-1 IAccountHardDeleter ISP-split (defer Fas 6),
TD-59 H-2 ICurrentJobSeeker-port (defer Fas 6 impersonation),
TD-60 ADR auth-pipeline-ordning (defer docs-pass),
TD-61 audit-trail-evidence-test för seeder (defer observability-pass).

**Tester:** 594 → 607 (+13 nya). Alla gröna inkl. arch-test 31 → 32 +
Application.UnitTests 196 → 201 + Api.IntegrationTests 178 → 179.

## Process

### Mandatory reads vid session-start

1. CLAUDE.md (hela), särskilt §2/§3/§5/§9.2/§9.6
2. `docs/reviews/2026-05-11-arch-audit-discovery.md` — audit-rapporten (fil-ref + scope-rek)
3. `docs/current-work.md` (audit-discovery state)
4. `docs/steg-tracker.md` v1.17 (skim)
5. `docs/tech-debt.md` (aktiva TDs)

### Block A — Domain DDD-konsistens + paging-rename (~1.5h)

**Discovery:** läste Application.cs, JobSeeker.cs, Resume.cs, ResumeVersion.cs,
GetResumesQuery + Handler + Validator, GetApplicationsQuery + Handler + Validator,
PagedResult.cs, PagedResultContractTests, Api/Program.cs catch-kedja.
DomainException existerade INTE i src/ — typen skapades från grunden.
ResumeDeletedDomainEvent hade inga subscribers.

**CTO-invocation (multi-approach):**
- **N-1 Riktning A:** lägg till events uppåt. CTO-motivering: domain events
  är historiska fakta (Evans 2003 + Vernon 2013 kap. 8), frånvaro av subscriber
  idag argumenterar inte mot raise. CCP/REP (Martin 2017 kap. 13): Resume,
  Application, JobSeeker är tre soft-deletable aggregates inom samma bounded
  context — samma livscykel-pattern. GDPR cascade-bevis kräver event-trail.
- **N-3 Alt A:** DomainException i Domain.Common. CLAUDE.md §2.1 förbjuder
  Domain-beroenden uppåt. Alt B (Application.Common.Exceptions) bryter
  dependency-rule. Alt C (per-aggregate exception) bryter OCP. Alt D
  (lämna InvalidOperationException) bryter §3.4 + §5.1.

**Implementation:**
- Nya files: `ApplicationDeletedDomainEvent`, `JobSeekerDeletedDomainEvent`,
  `DomainException` (i Domain.Common, sealed, code+message + `<remarks>`).
- `Application.SoftDelete` + `JobSeeker.SoftDelete`: idempotent (`if (DeletedAt.HasValue) return;`)
  + RaiseDomainEvent.
- `Resume.MasterVersion`: count switch wrappar `Single()`, kastar
  `DomainException("Resume.MasterInvariantBroken", ...)` med specifik 0- och
  multi-master-fall.
- Api/Program.cs: ny `catch (DomainException)` → 400 `{ code, error }`.
- `GetResumesQuery.PageNumber` → `Page`, `GetApplicationsQuery.PageNumber` → `Page`.
  Handlers + validators + tester uppdaterade kompilator-drivet.
- PagedResultContractTests heuristik: strikt mot `Page` (legacy `PageNumber` avvisas).
- Tester: 4 nya för raised events + 2 reflection-baserade för MasterVersion-invariant.

**Reviews:**
- **dotnet-architect:** 0 Blocker / 0 Major / 3 Minor / 2 Nit. Inga åtgärder
  in-block (alla observation-grad eller scope-creep).
- **code-reviewer:** Approved, 3 Minor / 2 Nit. In-block-fix: whitespace
  i test, test-naming `<Scenario>`-led tillagt, XML-doc-remarks i DomainException,
  kommentar i ClearVersions/DuplicateMaster reflection-helpers.

### Block B — IdempotentAdminRoleSeeder prod-gate-hardening (~1.5h)

**Discovery:** verifierade `ApiFactory.InitializeAsync` catch-22-mönstret
(Services.CreateScope FÖRE MigrateAsync). 4 prod-test-fixturer påverkade.

**CTO-invocation:** **Alt A** (env-gate). Motivering: CLAUDE.md §3.4 fail-loud,
§5.1 catch-all anti-pattern, Twelve-Factor §10 dev/prod parity, CCP
(EnsureSafeForEnvironment-mönstret existerar redan). Alt B (fixture-refactor)
bryter 4h-regeln. Alt C (log-level) behåller fail-silent.

**Implementation:**
- Constructor: ny `IHostEnvironment hostEnvironment`-param.
- Catch-when-klausul: `when (ex.SqlState == "42P01" && IsSchemaInitGracePeriod(hostEnvironment))`.
- Ny `internal static bool IsSchemaInitGracePeriod(IHostEnvironment env)`.
- Infrastructure.csproj: `InternalsVisibleTo` Application.UnitTests + Api.IntegrationTests.
- 5 nya unit-tester (Theory 5 InlineData över env-namn).
- Prod-fixturer (`HttpsRedirectionGateFactoryBase`, `ProductionStartupFactory`):
  filter-loop tar bort seeder ServiceDescriptor (catch-22-isolation).
- Anti-regression-test `IdempotentAdminRoleSeederProdBubbleTests`: separat
  `ProdSeederBubbleFactory` som BEHÅLLER seedern + skipar Identity-migration
  → bevisar 42P01-bubbling i Production via host-start-fail.

**Reviews:**
- **security-auditor:** 1 Major / 2 Minor / 1 Nit. **Major** fixed in-block:
  separat prod-bubble-fixture som bevisar gate-användning E2E (inte bara
  predicate-funktionen isolerat). Minor 2 (audit-trail-evidence-claim) → TD-61.
- **code-reviewer:** Approved, 1 Minor / 2 Nit. Inga åtgärder in-block
  (filter-loop bräcklighet acceptabel, magic-string-konvention etablerad
  i Worker/Program.cs:75).

### Block C — H-3 SessionAuth SoC-refactor (~1h)

**Discovery:** läste SessionAuthenticationHandler.cs, AdminAuditLogTests
(role-revoke-immediacy-mönster), AddAuthentication/AddAuthorization-flow i
Program.cs. Verifierade att ASP.NET kör IClaimsTransformation efter
authentication-handler men före authorization-policy.

**Implementation:**
- Ny `SessionRoleClaimsTransformation : IClaimsTransformation` i Infrastructure.Auth.
- SessionAuthenticationHandler: tog bort role-fetch + LogRoleResolutionFailed,
  constructor-param `IUserAccountService` borttagen. Klassen är inte längre `partial`.
- DI: `AddScoped<IClaimsTransformation, SessionRoleClaimsTransformation>()`.
- Sentinel-claim `jobbpilot:roles_resolved` för idempotency (efter
  dotnet-architect-feedback — `HasClaim(Role)`-guarden var otillförlitlig).
- `IHttpContextAccessor.HttpContext?.RequestAborted` på `GetRolesAsync` (efter
  security-auditor Major — resurs-läckage-skydd vid client-disconnect).
- Defensiv `is ClaimsIdentity identity`-check (security-auditor Nit).
- Ny arch-test `ClaimsTransformationAllowlistTests` (analogt med
  AuditingLayerTests audit-bypass-allowlist-pattern).

**Reviews:**
- **security-auditor:** 1 Major / 2 Minor / 1 Nit. **Major** fixed in-block:
  `CancellationToken.None` → `httpContextAccessor.HttpContext?.RequestAborted`.
  Minor (XML-doc-kommentar-vilseledande) fixed in-block. 401→403-divergens
  acceptabel (auth-protokoll håller stängt). Observation: framtida federerat
  IdP kan kräva ny idempotency-strategy (sentinel-claim hanterar idag).
- **dotnet-architect:** 0 Blocker / 0 Major / 2 Minor / 2 Nit. **Minor** fixed
  in-block: sentinel-claim istället för `HasClaim(Role)`-guard, arch-test
  för IClaimsTransformation-konsument-allowlist. ADR-pipeline-ordning → TD-60.

### Block D — TDs + docs + commits

4 TDs lyfta enligt agent-reviews:
- **TD-58:** H-1 IAccountHardDeleter ISP-split (defer Fas 6).
- **TD-59:** H-2 ICurrentJobSeeker-port (defer Fas 6 impersonation).
- **TD-60:** ADR för auth-pipeline-ordning + IClaimsTransformation-disciplin (defer docs-pass).
- **TD-61:** Audit-trail-evidence-test för IdempotentAdminRoleSeeder
  (defer observability-pass).

`docs/current-work.md` uppdaterad. `docs/steg-tracker.md` rad för Fas 2
polish-block. Session-logg (denna fil).

**Commits (pending Klas-GO):** 4 commits per Conventional Commits.

## Tester (full svit grön — väntar push)

- **Domain.UnitTests:** 163 (+6 från Block A: 4 events, 2 invariant)
- **Application.UnitTests:** 201 (+5 från Block B: 5 env-gate-tests)
- **Architecture.Tests:** 32 (+1 från Block C: ClaimsTransformation-allowlist)
- **Migrate.UnitTests:** 6 (oförändrat)
- **Api.IntegrationTests:** 179 (+1 från Block B: prod-bubble-test)
- **Worker.IntegrationTests:** 26 (oförändrat)
- **Total: 607** (+13 från Block A+B+C)

## Beslut/avvägningar

1. **N-1 events uppåt (CTO Riktning A):** Domain events som historiska fakta
   per Evans/Vernon. Frånvaro av subscriber idag är inte argument mot raise.
2. **N-3 DomainException i Domain.Common (CTO Alt A):** Dependency rule
   (CLAUDE.md §2.1) tvingar placering. OCP avvisar per-aggregate-exception (Alt C).
3. **N-2 env-gate (CTO Alt A):** Fail-loud i prod över silent-tolerance.
   `EnsureSafeForEnvironment`-mönstret är redan etablerat.
4. **H-3 IClaimsTransformation:** Ren ASP.NET-extension-punkt-disciplin.
   Per-request-fetch bibehållen, SoC-vinst på handler.
5. **In-block-fix-default per 4h-regel:** Alla agent-Major och nästan alla
   Minor fixades in-block. TDs lyfts bara där scope > 4h eller saknad
   funktion-dependency (alla 4 TDs på Fas 6 admin-impersonation).
6. **Reflection i tester (Resume.MasterVersion):** Acceptabelt för invariant-
   brott-simulering av rehydrering-scenario — invarianten skyddas av domain-API:t,
   alternativet (test-only constructor) bryter encapsulation permanent.

## Risker/oklarheter

- **TD-60 (auth-pipeline-ordning-ADR):** Inte påbörjad. Future-auth-ändringar
  (impersonation, federerat IdP) blir lättare när ADR finns.
- **TD-61 (audit-trail-evidence):** Seeder-XML-doc hävdar audit-observability
  men oförifierad. Stör inte prod idag men gör impersonation-audit-design
  svårare innan verifiering.
- **`dotnet format` triggade nested-loop-reindentation i
  ConnectionStringLeakageTests.cs:** Inkluderas i Block A-commit som
  format-disciplin. Inga semantiska ändringar.

## Nästa session

Klas reviewar diff (CLAUDE.md §6.3 punkt 4 — manuell diff-granskning).
Vid GO: 4 commits + push. Sedan optionell väg:

- **Väg A:** Lyft TD-60 (auth-pipeline-ADR) som dedikerat docs-pass (~45 min).
- **Väg B:** Lyft TD-61 (audit-trail-evidence-test) som observability-pass
  (~1h).
- **Väg C:** Fortsätt feature-arbete (Fas 2 JobTech-integration, blockerad
  till ADR 0005 + kostnadsskydd — eller annan icke-blockerad Fas 1-feature).
- **Väg D:** Pausa, ny session.

Inga aktiva TDs blockerar Väg C. Audit-rapporten + denna polish-block
levererar "100% clean" före Fas 2-feature-arbete.

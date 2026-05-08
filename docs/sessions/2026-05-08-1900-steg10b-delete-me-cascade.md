---
session: "2026-05-08 — STEG 10b: DELETE /me + GDPR Art. 17-cascade + HardDeleteAccountsJob (ADR 0024 D3-D6)"
datum: 2026-05-08
slug: steg10b-delete-me-cascade
status: KLAR
commits:
  - sha: (pending)
    msg: "feat(auth): STEG 10b — DELETE /me + Art. 17-cascade + HardDeleteAccountsJob (ADR 0024 D3-D6, stänger TD-16)"
  - sha: (pending)
    msg: "docs: STEG 10b docs-sync (runbook + tech-debt + steg-tracker + current-work + session-logg)"
---

## Mål för sessionen

STEG 10b — TD-16 del 2 (GDPR Art. 17-cascade) och DELETE /me-flödet. Stänger TD-16 helt (del 1 via STEG 10a, del 2 via 10b).

## Vad som genomfördes

### Block A — IAuditTrailEraser-port + impl + ISessionStore-utökning

**Filer:**
- `IAuditTrailEraser.cs` (port, Application/Common/Auditing) — `AnonymizeUserAuditTrailAsync(Guid userId, ct)`
- `AuditTrailEraser.cs` (impl, Infrastructure/Auditing) — `db.Database.ExecuteSqlAsync` med FormattableString-parametrisering
- `ISessionStore.cs` — utökad med `InvalidateAllForUserAsync` (rek-metod från ADR 0017 deferred-not, stängs här)
- `RedisSessionStore.cs` — secondary Redis-set `jobbpilot:user:{userId}:sessions` med SADD/SREM/SMEMBERS via IConnectionMultiplexer
- `InMemorySessionStore.cs` — LINQ-baserad InvalidateAllForUserAsync
- `DependencyInjection.cs` — IConnectionMultiplexer + IAuditTrailEraser DI

**Designval:** secondary Redis-set framför SCAN-fallback (rek från tidigare diskussion). Aktiva sessioner spåras explicit; bulk-invalidering i O(N) över user:s sessioner istället för O(M) över hela Redis-keyspace.

### Block B — DeleteAccountCommand + DELETE /me + LoginCommandHandler D5-blockering

**Filer:**
- `DeleteAccountCommand.cs` + handler (Application/Auth/Commands/DeleteAccount) — Mediator-command, `IAuthenticatedRequest` + `IAuditableCommand<Result<Guid>>`. Cascade soft-delete JobSeeker + alla Application + alla Resume i samma SaveChanges.
- `MeEndpoints.cs` — `DELETE /` + post-commit `InvalidateAllForUserAsync`
- `LoginCommandHandler.cs` — D5-blockering vid `JobSeeker.DeletedAt is not null`. Returnerar `Auth.InvalidCredentials` (sec-fix — se Reviews nedan).

**Designval:** Idempotency-tidig-return vid `DeletedAt is not null` undviker dubbla audit-rader vid retry. Failsafe-strategi: Redis-fel mid-DELETE swallow:as INTE — säkerhet > UX vid kontoradering.

### Block C — HardDeleteAccountsJob + AddCoreIdentityForWorker

**Filer:**
- `IAccountHardDeleter.cs` (port, Application/Auth/Jobs/HardDeleteAccounts) — 3 metoder: CleanupIdentityOrphans + GetAccountsReadyForHardDelete + HardDeleteAccount
- `AccountHardDeleter.cs` (impl, Infrastructure/Auth) — cross-context (AppDbContext + UserManager + IAuditTrailEraser). Explicit `BeginTransactionAsync` runt domain-delete + audit-anonymize. Identity-DELETE separat boundary.
- `HardDeleteAccountsJob.cs` (orchestrator, Application/Auth/Jobs/HardDeleteAccounts) — Steg 0 + 1 + 2 loop
- `AddCoreIdentityForWorker`-extension i DependencyInjection.cs — HTTP-fri `AddIdentityCore<>()` (utan AuthenticationScheme/Cookies/SignInManager). Worker laddar för UserManager-tillgång.
- `Worker/Program.cs` — laddar `AddCoreIdentityForWorker`
- `RecurringJobRegistrar.cs` — `hard-delete-accounts` cron 04:00 UTC daily (per ADR 0024 D6 — 1h efter retention)

**Cross-context-fråga löst:** ADR 0023 D2 säger Worker laddar bara `AddPersistence`. För hard-delete behöver Worker UserManager → ny HTTP-fri Identity-extension. `AddDefaultTokenProviders()` utelämnas (kräver IDataProtectionProvider).

### Block D — Architecture-tester + smoke-tester

**Arch-tester (4 nya):**
- `IAuditTrailEraser ska INTE konsumeras från Application` (porten anropas av AccountHardDeleter i Infrastructure)
- `IAuditTrailEraser i Infrastructure: allow-list AuditTrailEraser + AccountHardDeleter + DependencyInjection`
- `IAccountHardDeleter i Application: allow-list HardDeleteAccountsJob`
- `IAccountHardDeleter i Infrastructure: allow-list AccountHardDeleter + DependencyInjection`

**Smoke-tester (6 nya):**
- 3× `AuditTrailEraserSmokeTests` — anonymisering verifierar PII→NULL + accountability-fält bevaras + isolation + idempotency
- 3× `HardDeleteAccountsJobIntegrationTests` — eligible/within-window/orphan-cleanup

**Bug fångad:** AddDefaultTokenProviders() krävde IDataProtectionProvider → tagit bort (Worker behöver bara CRUD-yta).

### Block E — Integration-test för DELETE /me end-to-end

5 nya tester i `DeleteMeTests.cs`:
- 401 utan token
- 204 + JobSeeker.DeletedAt verifierad
- Login blockerad efter delete (Auth.InvalidCredentials, INTE AccountPendingDeletion — sec-fix)
- Session-id ger 401 efter DELETE (secondary Redis-set verifierad)
- Account.Deleted audit-rad skrivs med korrekt UserId+AggregateType+AggregateId

### Block F — Reviews + säkerhetsfixar

**code-reviewer:** Approved with Minors. 1 Major + 7 Minor + 7 Nit.
**security-auditor:** Approved with Majors. 0 Critical + 4 Major + 5 Minor.

#### Säkerhetsfixar applicerade efter audit:

1. **Sec-1 (Major) — Login-orakel via `Auth.AccountPendingDeletion`** (GDPR Art. 32 information disclosure). Fix: returnera samma `Auth.InvalidCredentials` (401) som okänd email/fel lösen vid soft-deletad konto. Test uppdaterad.
2. **Sec-4 (Major) — Defense-in-depth null-check** i DeleteAccountCommandHandler. Throw-safe fallback istället för null-forgiving operator.
3. **Sec-Minor-3 — Redis SADD-före-SET-main** i CreateAsync. Skydd mot session-orphan-vid-Redis-fel som hade missas av InvalidateAllForUserAsync.

#### Code-fixar applicerade:
- M1 — RecurringJobRegistrar-kommentar förtydligad
- m6 — Död kod i DeleteMeTests
- m7 — D5-blockerings unit-test (2 nya: soft-deleted → InvalidCredentials, active → success)
- m5 — namespace `Me` → `MyProfile` (CA1716, "Me" är reserved keyword)

#### Defererade till tech-debt:
- **TD-21** — rate-limiting på DELETE /me + auth-endpoints (Sec-2, prod-blockare)
- **TD-22** — app-logg-retention + IP/UA-redaction (Sec-3 strukturell, Klas-policy-beslut)
- **TD-23** — Redis MULTI/EXEC atomicitet (Sec-Minor-3 fullständig fix, Fas 2)
- **TD-24** — DeleteAccountCommand cascade-paginering (Sec-Minor-1, Fas 4)
- **TD-25** — HardDeleteAccountsJob per-konto try/catch (Code-Nit-5, opportunistiskt)

### Block F (forts) — Runbook + docs-sync

`docs/runbooks/account-deletion.md` — komplett ops-runbook:
- Flöde steg-för-steg (soft-delete → restore-fönster → hard-delete)
- Övervakning (Hangfire dashboard, structured log, SQL-queries)
- Manuell SQL-restore inom 30-dagars-fönstret (Fas 6 admin-yta saknas)
- Failure-scenarier (Redis-fel mid-DELETE, hard-delete-loop-fail, Identity-DELETE-fail, audit-anon-fail)
- GDPR-noter med paragrafreferenser

## Reviews

- **code-reviewer:** Approved with Minors. M1 + m5 + m6 + m7 fixade. Nits delvis defererade till TD.
- **security-auditor:** Approved with Majors (0 Critical). Major-1 (Sec-1 login-orakel) + Major-4 (Sec-4 null-check) + Sec-Minor-3 fixade. Major-2 (rate-limiting) → TD-21. Major-3 (app-logg-retention) → TD-22 (kräver Klas-policy-beslut).

## Tech-debt-status efter STEG 10b

- **TD-16 STÄNGD** (del 1+2 komplett) ✓
- **TD-13** (PII-encryption Fas 2) — kvarstår
- **TD-14** (DeleteResumeVersion Fas 4) — kvarstår
- **TD-15** (Resume-formulär a11y Fas 1) — kvarstår
- **TD-17** (Hangfire prod-härdning) — kvarstår, **kvarstående Fas 1 prod-blockare**
- **TD-18, TD-19, TD-20** — oförändrade
- **TD-21 ny** — rate-limiting (Fas 1 prod-blockare)
- **TD-22 ny** — app-logg-retention + IP/UA-redaction (Fas 1 prod-blockare)
- **TD-23 ny** — Redis MULTI/EXEC atomicitet (Fas 2)
- **TD-24 ny** — DeleteAccountCommand cascade-paginering (Fas 4)
- **TD-25 ny** — HardDeleteAccountsJob per-konto try/catch (opportunistiskt)

## Tester totalt efter STEG 10b

- **Backend:** 475 (157 Domain + 171 Application + 22 Architecture + 109 Api Integration + 16 Worker SmokeTest) — +17 sedan STEG 10a (2 Application unit + 4 arch + 6 worker smoke + 5 api integration)
- **Frontend:** 65 Vitest + 19 Playwright E2E (oförändrat)

## Filer ändrade (sammanfattning)

**Nya (12):**
- 2 Application portar/orchestratorer (IAuditTrailEraser, AccountHardDeleter-port + job)
- 2 Application orchestratorer (IAccountHardDeleter, HardDeleteAccountsJob)
- 1 Application command + handler (DeleteAccountCommand)
- 2 Infrastructure impls (AuditTrailEraser, AccountHardDeleter)
- 3 test-filer (AuditTrailEraserSmokeTests, HardDeleteAccountsJobIntegrationTests, DeleteMeTests)
- 1 runbook (account-deletion.md)
- 1 session-logg (denna fil)

**Modifierade (10):**
- ISessionStore + RedisSessionStore + InMemorySessionStore (InvalidateAllForUserAsync)
- DependencyInjection (AddCoreIdentityForWorker + IConnectionMultiplexer)
- Worker/Program.cs + RecurringJobRegistrar
- LoginCommandHandler (D5-blockering med Sec-1-fix)
- MeEndpoints (DELETE /me)
- AuditingLayerTests (4 nya arch-tester)
- WorkerTestFixture (AddCoreIdentityForWorker + AppIdentityDbContext-migrate)
- 3 Redis*-Tests + LoginCommandHandlerTests (constructor-uppdateringar + 2 nya unit-tester)
- tech-debt.md (TD-16 stängd, TD-21-25 nya)

## Klas:s två CC-design-frågor besvarade

1. **`ISessionStore.InvalidateAllForUserAsync`-strategi:** secondary Redis-set (rek). Implementerad och verifierad via integration-test.
2. **`LoginCommandHandler`-D5-blockering:** ny `IAppDbContext`-injektion (rek). Plus säkerhets-fix från audit (returnera Auth.InvalidCredentials, inte AccountPendingDeletion).

## Nästa session

**Återstående Fas 1 prod-deploy-blockare:**
- TD-17 (Hangfire prod-härdning)
- TD-21 (rate-limiting)
- TD-22 (app-logg-retention — kräver Klas-policy-beslut)

**Andra alternativ:**
- Fas 0-stängning (deploy + GitHub Actions CI/CD + bootstrap-IAM-cleanup)
- Fortsätt features (Fas 1 UX-pass, Resume-version-Tailored?)

Klas beslutar i nästa session.

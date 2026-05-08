# Current work — JobbPilot

**Status:** STEG 10b KLAR. **TD-16 STÄNGD HELT** (audit-retention + Art. 17-cascade). DELETE /me-flödet implementerat med säkerhets-fix mot info-disclosure. TD-21–25 nya. Nästa: STEG 11 — kräver beslut.
**Senast uppdaterad:** 2026-05-08
**Långsiktig bana:** `docs/steg-tracker.md` — single source of truth för STEG/fas-progression
**Tech debt:** `docs/tech-debt.md`

---

## Aktivt nu

**STEG 10b klar.** GDPR Art. 17-flödet (DELETE /me + cascade soft-delete + 30-dagars restore-fönster + HardDeleteAccountsJob) implementerat per ADR 0024 D3-D6. Stänger TD-16 helt (del 1 audit-retention via STEG 10a, del 2 Art. 17-cascade via STEG 10b).

### STEG 10b — DELETE /me + Art. 17-cascade (ADR 0024 D3-D6)

**Strategi:** Soft-delete vid DELETE /me + 30-dagars restore-fönster + hard-delete via Hangfire-jobb 04:00 UTC. Audit-anonymisering vid hard-delete (inte vid soft-delete — under restore-fönstret ska user kunna se sin audit). Architecture-test-låst bypass-disciplin för audit-anonymisering.

**Block A — IAuditTrailEraser + ISessionStore-utökning:**
- `IAuditTrailEraser`-port i Application/Common/Auditing — anropas av AccountHardDeleter
- `AuditTrailEraser`-impl i Infrastructure — `db.Database.ExecuteSqlAsync` med FormattableString-parametrisering
- `ISessionStore.InvalidateAllForUserAsync` — stänger ADR 0017 deferred-not
- `RedisSessionStore` secondary index `jobbpilot:user:{userId}:sessions` (SADD-före-SET-main per security-fix)

**Block B — DeleteAccountCommand + DELETE /me + LoginCommandHandler D5:**
- `DeleteAccountCommand` (Mediator, IAuthenticatedRequest, IAuditableCommand<Result<Guid>>) — cascade soft-delete + idempotency
- `DELETE /api/v1/me`-endpoint + post-commit `InvalidateAllForUserAsync`
- `LoginCommandHandler` D5-blockering — returnerar `Auth.InvalidCredentials` (inte `AccountPendingDeletion`) för att undvika info disclosure

**Block C — HardDeleteAccountsJob:**
- `IAccountHardDeleter`-port (3 metoder: CleanupIdentityOrphans + GetReady + HardDeleteAccount)
- `AccountHardDeleter`-impl: cross-context (AppDbContext + UserManager + IAuditTrailEraser). Explicit `BeginTransactionAsync` runt domain-delete + audit-anonymize. Identity-DELETE separat boundary (orphan-cleanup-loopen plockar upp om den failer).
- `HardDeleteAccountsJob`-orchestrator: Steg 0 + 1 + 2 loop med cancel-token + progress-log
- `AddCoreIdentityForWorker`-extension: HTTP-fri `AddIdentityCore<>()` (utan AuthenticationScheme/Cookies/AddDefaultTokenProviders) för Worker-DI
- Hangfire `hard-delete-accounts` cron 04:00 UTC daily

**Block D — Architecture + smoke-tester:**
- 4 nya arch-tester (IAuditTrailEraser + IAccountHardDeleter-isolering, allow-list-pattern)
- 3 AuditTrailEraser smoke-tester (PII→NULL, isolation, idempotency)
- 3 HardDeleteAccountsJob smoke-tester (eligible/within-window/orphan-cleanup)

**Block E — Integration-test för DELETE /me:**
- 5 end-to-end-tester (401/204+softDelete/login-blocked/session-invalidated/audit-rad)

**Block F — Reviews + säkerhetsfixar:**
- code-reviewer Approved with Minors (1 Major + 7 Minor + 7 Nit). M1+m5+m6+m7 fixade.
- security-auditor Approved with Majors (0 Critical + 4 Major + 5 Minor). Sec-1+Sec-4+Sec-Minor-3 fixade.
- TD-21–25 nya (rate-limiting + app-logg-retention som Fas 1 prod-blockare).
- 2 nya unit-tester för D5-blockering.
- Runbook `docs/runbooks/account-deletion.md` (komplett ops-procedur + manuell SQL-restore inom 30d)

### Reviews genomförda

- **code-reviewer (Block A-E):** Approved with Minors. 1 Major (RecurringJobRegistrar-kommentar) + 7 Minor (D5-unit-test, namespace, död kod, etc.) + 7 Nit. Alla Major+relevanta Minor fixade. Nits delvis defererade.
- **security-auditor (Block A-E):** Approved with Majors. **0 Critical**, 4 Major, 5 Minor. Sec-1 (login-orakel) + Sec-4 (defense-in-depth) + Sec-Minor-3 (Redis SADD-ordning) fixade. Sec-2 (rate-limiting) → TD-21. Sec-3 (app-logg-retention) → TD-22 (Klas-policy-beslut).

### Säkerhets-fix värd att lyfta: Sec-1 (login-orakel)

Initialt returnerade D5-blockering `Auth.AccountPendingDeletion` (400) för soft-deletade konton vs `Auth.InvalidCredentials` (401) för okänd email/fel lösen. **Auditor flaggade som information disclosure (GDPR Art. 32):** angripare med credential-stuffing-listor kunde identifiera nyligen raderade konton (high-value social-engineering-mål).

**Fix:** soft-deletade konton returnerar nu samma `Auth.InvalidCredentials` (401) som okänd email/fel lösen. Användaren kontaktar support out-of-band om de vill återställa kontot. Test verifierar att `AccountPendingDeletion`-koden aldrig läcker till klient.

### Tech-debt-status efter STEG 10b

- ~~**TD-9** stängd i STEG 8~~
- ~~**TD-16** **STÄNGD HELT** (del 1 STEG 10a, del 2 STEG 10b)~~
- **TD-13** (PII-encryption Fas 2) — kvarstår
- **TD-14** (DeleteResumeVersion Fas 4) — kvarstår
- **TD-15** (Resume-formulär a11y Fas 1) — kvarstår
- **TD-17** (Hangfire prod-härdning) — kvarstår, **kvarstående Fas 1 prod-blockare**
- **TD-18, TD-19, TD-20** — oförändrade
- **TD-21 ny** — rate-limiting på DELETE /me + auth-endpoints (**Fas 1 prod-blockare**)
- **TD-22 ny** — app-logg-retention + IP/UA-redaction (**Fas 1 prod-blockare**, kräver Klas-policy-beslut)
- **TD-23 ny** — Redis MULTI/EXEC atomicitet i CreateAsync (Fas 2)
- **TD-24 ny** — DeleteAccountCommand cascade-paginering (Fas 4 vid power-user-volym)
- **TD-25 ny** — HardDeleteAccountsJob per-konto try/catch (opportunistiskt)

## Senaste commits

| SHA | Beskrivning |
|-----|-------------|
| (pending) | docs: STEG 10b docs-sync (runbook + tech-debt + steg-tracker + current-work + session-logg) |
| (pending) | feat(auth): STEG 10b — DELETE /me + Art. 17-cascade + HardDeleteAccountsJob (ADR 0024 D3-D6, stänger TD-16) |
| ff93f69 | docs: STEG 10a docs-sync (ADR 0024 + runbook + tech-debt + steg-tracker + current-work + session-logg) |
| 110e618 | feat(auditing): STEG 10a — audit-log retention partitioning + Hangfire-job (ADR 0024 D1+D2) |
| 8982213 | docs: STEG 9 docs-sync (ADR 0023 + tech-debt + steg-tracker + current-work + session-logg) |
| 152f047 | feat(worker): STEG 9 — Worker-pipeline + Hangfire + DetectGhostedApplicationsJob (ADR 0023) |

## Tester totalt

- **Backend:** 475 (157 Domain + 171 Application + 22 Architecture + 109 Api Integration + 16 Worker SmokeTest) — +17 sedan STEG 10a
- **Frontend:** 65 Vitest + 19 Playwright E2E (oförändrat)

## När nästa session startar

1. Kör `git log --oneline -10` — verifiera HEAD
2. Verifiera backend-tester: kör test-exen direkt under `tests/*/bin/Debug/net10.0/`
3. För smoke-tests: `tests/JobbPilot.Worker.IntegrationTests/bin/Debug/net10.0/JobbPilot.Worker.IntegrationTests.exe -trait "Category=SmokeTest"`
4. Läs `docs/steg-tracker.md` §6 för STEG 11-kandidater
5. Läs senaste session-logg (STEG 10b) för detaljer
6. Läs ADR 0024 för full kontext

## Kända begränsningar / quirks

- **postgres-dev** på port **5435**
- **`dotnet ef`** plockar inte upp `appsettings.Local.json` — använd `export ConnectionStrings__Postgres=...`
- **`dotnet test`** på solution-nivå returnerar "Zero tests ran" (xunit.v3.mtp-v2-issue) — kör test-exen direkt
- **API kräver `ASPNETCORE_ENVIRONMENT=Development`** för Redis-connstring
- **`audit_log` är partitionerad** — bootstrap-fönstret är 7 dagar framåt; retention-jobb 03:00 UTC. Default-partitionen fångar overflow.
- **Hangfire 3 jobs**: audit-log-retention + detect-ghosted (båda 03:00 UTC) + hard-delete-accounts (04:00 UTC)
- **Hangfire-schema** skapas automatiskt vid Worker-start i dev — TD-17 dokumenterar prod-härdning
- **AddCoreIdentityForWorker** registrerar Identity utan AddDefaultTokenProviders (kräver IDataProtectionProvider — HTTP-bagage)
- **DELETE /me-flödet:** soft-delete direkt → 30 dagar restore-fönster → hard-delete + audit-anonymisering
- **D5-blockering returnerar `Auth.InvalidCredentials`** (inte `AccountPendingDeletion`) för att undvika info disclosure
- **Mediator-pipeline-config:** ALLTID via `AddMediatorPipelineBehaviors()` — `options.PipelineBehaviors`-fält fungerar INTE med Mediator.SourceGenerator 3.0.2
- **Komposit-PK på audit_log:** `(id, occurred_at)` per ADR 0024 D2 (kompletterar ADR 0022)
- **Worker integration smoke-test** kräver Docker-Compose uppe + tar ~7-10 sekunder per körning
- **Middleware-deprecation-varning** i Next.js (kvar från STEG 6)

## Open follow-ups

**Fas 1 prod-deploy-blockare (3 kvar):**
- TD-17 (Hangfire prod-härdning)
- TD-21 (rate-limiting)
- TD-22 (app-logg-retention — Klas-policy-beslut)

**Övriga TD:**
- TD-13 (PII-encryption Fas 2)
- TD-14 (DeleteResumeVersion Fas 4)
- TD-15 (Resume-formulär a11y Fas 1)
- TD-18 (intervju-states-utökning)
- TD-19 (Worker defense-in-depth Fas 2)
- TD-20 (SqlQuery<FormattableString>-refactor opportunistiskt)
- TD-23 (Redis MULTI/EXEC opportunistiskt)
- TD-24 (cascade-paginering Fas 4)
- TD-25 (per-konto try/catch opportunistiskt)

Per CLAUDE.md §1.5: docs-sync efter varje STEG (inte bara session-end). Glöm inte session-logg.

## STEG 11 — kräver beslut

**Återstående Fas 1 prod-deploy-blockare (3):**
- TD-17 — Hangfire prod-härdning
- TD-21 — rate-limiting
- TD-22 — app-logg-retention (kräver Klas-policy-beslut före kod)

**Andra alternativ:**
- Fas 0-stängning (deploy + GitHub Actions CI/CD + bootstrap-IAM-cleanup)
- Fortsätt Fas 1-features (Application Management UX-pass, Resume-version-Tailored, etc.)

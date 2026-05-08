# JobbPilot — STEG-tracker

> **Version:** 1.6
> **Senast uppdaterad:** 2026-05-08
> **Roll:** permanent översikt över STEG- och fas-progression.

Kompletteras av:
- `docs/current-work.md` — aktiv session-state
- `docs/sessions/` — per-session-loggar
- `docs/decisions/` — arkitekturbeslut
- `docs/tech-debt.md` — teknisk skuld

---

## 1. Översikt

JobbPilot:s utvecklingsbana spårar två dimensioner:

- **Faser** — strategiska tidsblock per BUILD.md §18. Nio faser plus Fas 9+ (efter klass-launch).
- **STEG** — tekniska arbetsenheter inom faser. Numrering är historisk och behålls oförändrad även när faserna inte mappar 1:1 mot STEG-gränserna.

Mellan-arbete (upptakter, cleanup-passningar, disciplin-uppgraderingar) är inte STEG men dokumenteras i §4.

## 2. Fas-översikt (per BUILD.md §18)

| Fas | Namn | Tidsuppskattning | Milstolpe | Status |
|-----|------|------------------|-----------|--------|
| Fas 0 | Foundation | ~2 v | Registrera + logga in på dev.jobbpilot.se | Lokalt klar¹ |
| Fas 1 | Core Domain | ~3 v | CV manuellt + "fake" ansökningar i admin-audit | Pågående |
| Fas 2 | JobTech Integration | ~2 v | Söka jobb på Platsbanken via appen, spara sökningar | Planerad² |
| Fas 3 | Application Management | ~2 v | Fullständig ansökningshantering (utan AI) | Planerad |
| Fas 4 | AI Layer | ~3-4 v | Alla AI-features end-to-end + 14 dagar dogfood | Planerad |
| Fas 5 | Integrationer | ~2 v | Gmail auto-loggar, intervjuer i Google Calendar | Planerad |
| Fas 6 | Admin & Analytics | ~2 v | Admin-panel komplett | Planerad |
| Fas 7 | Internal Beta | ~2 v | 3 användare aktivt 14 dagar | Planerad |
| Fas 8 | Klass-launch | ~1 v | 20 klasskamrater onboardade — v1 klar | Planerad |
| Fas 9+ | Efter klass-launch | — | Mobil, Kanban, intervjuträning, LinkedIn, Stripe, Chrome ext m.m. | Framtid |

**Totalt:** ~20 veckor till klass-launch (mjuk uppskattning, inga hårda deadlines).

¹ Fas 0:s lokala milstolpar uppfyllda (Clean Arch-solution, Identity, Next.js, design system). Kvarvarande för full Fas 0-stängning enligt BUILD.md §18: första deploy till dev.jobbpilot.se, GitHub Actions CI/CD verifierad, bootstrap-IAM-user raderad.

² Fas 2 är blockerad till ADR 0005 (go-to-market) är beslutad och kostnadsskydd implementerat (Budget Actions, `registrations_open`-flagga, rate limiting, runbook `docs/runbooks/aws-cost-recovery.md`) per BUILD.md §18.

## 3. STEG-historik

STEG-numrering följer faktisk arbetsutveckling och mappar inte exakt mot fas-gränserna i BUILD.md §18. Se §7 för numreringsfotnot.

### Klara

| STEG | Fas | Beskrivning | Sessions |
|------|-----|-------------|----------|
| Pre-STEG | Fas 0 | Research, plan, design-research | Sessions 1, 2, 2.5 |
| Pre-STEG | Fas 0 | AWS, Docker, agents, skills, hooks, GitHub, docs | Sessions 3-5 |
| STEG 1 | Fas 0 | .NET Solution-uppsättning + ADR 0008-0011 | Session 6 |
| STEG 2 | Fas 0 | Domain/Infrastructure/Application/API — JobAd aggregate (35 tester) | Session 7 |
| STEG 3 | Fas 1 | Auth-stack (Identity + JWT, ADR 0012-0014) + JobSeeker aggregate (75 tester) | Session 8 |
| STEG 4a | Fas 0 | Frontend bootstrap (Next.js 16, civic-tokens, shadcn nova, ADR 0015-0016) | Session 9 |
| STEG 4b | Fas 1 | Session-auth backend (ISessionStore, SessionAuthenticationHandler, IAuthAuditLogger, ADR 0017-0018) + frontend auth (login/register/me-sidor, /(app)-layout, 153 tester) | Session 4b.1, 4b.2 |
| STEG 5 | Fas 1 | Application-aggregat — domän (SmartEnum state machine, FollowUp, ApplicationNote), EF Core, 5 commands, 3 queries, 7 API-endpoints, 280 tester (53 nya) | 2026-05-07 |
| STEG 6 | Fas 1 | Frontend /ansokningar — pipeline-tabell, ny-ansökan, detaljvy, transitionsformulär, Server Actions, Zod v4, 28 Vitest + 13 Playwright E2E | 2026-05-08 |
| STEG 7a | Fas 1 | Resume-aggregat backend — domain (Resume AR + ResumeVersion + ResumeContent VO), EF JSONB via HasConversion, migration `AddResumeAggregate`, 5 commands + 2 queries, 7 API-endpoints, +98 tester. Plan-design via CC (utan webb-Claude). ADR 0021 (Master-mutation), TD-13/TD-14. | 2026-05-08 |
| STEG 7b | Fas 1 | Frontend /cv — Resume-pages, ResumeContentForm med RHF `useFieldArray` för Experiences/Educations/Skills, Server Actions, Zod v4, 37 Vitest + 6 Playwright E2E. TD-15. | 2026-05-08 |
| STEG 8 | Fas 1 | Audit log-infrastruktur — pipeline-behavior + marker-interface (ADR 0022). 10 commands märkta IAuditableCommand. Migration `AddAuditLogTable`. IP-anonymisering /24+/48, server-gen correlation-ID. Stänger TD-9. +41 tester (14 Domain + 11 Application + 12 Integration + 4 Architecture). | 2026-05-08 |
| STEG 9 | Fas 1+2/3 förskott | Worker-pipeline-aktivering + Hangfire-infrastruktur (ADR 0023). DI-modulär refaktor (`AddPersistence`/`AddIdentityAndSessions`/`AddHttpAuditing`). 3 Worker-stubs av audit-portarna. `DetectGhostedApplicationsJob` orchestrator + `StaleApplicationSpecification`. Application-aggregat utökat med `LastStatusChangeAt` + `GhostedThresholdDays` (per Application, BUILD.md §schema). Migration `AddApplicationStaleDetectionFields` (NOW()-backfill, partial index). **Pipeline-bug-fix:** `AddMediatorPipelineBehaviors()` (open-generic DI) ersätter trasig `options.PipelineBehaviors`-fält-reference. Newtonsoft.Json 13.0.3 transitiv CVE-pinning. +32 tester (9 Domain + 12 Application + 5 Architecture + 6 Worker SmokeTest). | 2026-05-08 |
| STEG 10a | Fas 1 | Audit-log retention via PostgreSQL native daily partitioning + Hangfire-jobb (ADR 0024 D1+D2). `audit_log` konverterad till `PARTITION BY RANGE (occurred_at)` med komposit-PK `(id, occurred_at)`. Migration `AddAuditLogPartitioning` (rename → 7 bootstrap-partitions + default → INSERT-SELECT med explicit kolumnlista → DROP legacy). `IAuditPartitionMaintainer`-port + impl + `AuditLogRetentionJob`-orchestrator. Hangfire-cron 03:00 UTC daily. Idempotent (`CREATE IF NOT EXISTS`). 3 nya arch-tester för bypass-isolering. 4 nya smoke-tester. Runbook `docs/runbooks/audit-retention.md`. **Stänger del 1 av TD-16** (Art. 5(1)(e) Storage Limitation). TD-20 ny (defensiv refactor av SqlQueryRaw → SqlQuery<FormattableString>, defererad). +7 tester (3 arch + 4 smoke). | 2026-05-08 |
| STEG 10b | Fas 1 | DELETE /me + GDPR Art. 17-cascade + HardDeleteAccountsJob (ADR 0024 D3+D4+D5+D6). `IAuditTrailEraser`-port + impl (audit-bypass via direct SQL UPDATE). `DeleteAccountCommand` + handler (cascade soft-delete + idempotent). `DELETE /api/v1/me`-endpoint + post-commit session-invalidering via secondary Redis-set. `LoginCommandHandler` D5-blockering vid `JobSeeker.DeletedAt` (returnerar `Auth.InvalidCredentials`, inte `AccountPendingDeletion` — sec-fix mot info disclosure). `IAccountHardDeleter` + `AccountHardDeleter` cross-context impl. `HardDeleteAccountsJob` orchestrator: Steg 0 orphan-cleanup + Steg 1 hämta mogna + Steg 2 explicit transaction (anonymize audit + hard-delete cascade) + separat Identity-DELETE-boundary. Cron 04:00 UTC daily. `AddCoreIdentityForWorker`-extension (HTTP-fri Identity för Worker, utan AddDefaultTokenProviders). 4 nya arch-tester + 6 nya smoke-tester + 5 nya integration-tester + 2 unit-tester (D5). Runbook `docs/runbooks/account-deletion.md`. **Stänger TD-16 helt.** TD-21-25 nya (rate-limiting prod-blockare, app-logg-retention prod-blockare, Redis MULTI/EXEC defensiv refactor, cascade-paginering Fas 4, per-konto try/catch opportunistiskt). +17 tester (2 unit + 4 arch + 6 worker smoke + 5 api integration). | 2026-05-08 |

### Pågående

(inga aktiva STEG just nu — se §5)

### Planerade

| STEG | Fas | Beskrivning | Status |
|------|-----|-------------|--------|
| STEG 11 | Fas 0/1 | **Ej beslutat.** Återstående Fas 1 prod-deploy-blockare: TD-17 (Hangfire prod-härdning), TD-21 (rate-limiting), TD-22 (app-logg-retention — kräver Klas-policy-beslut). Eller: Fas 0-stängning (deploy + GitHub Actions CI/CD + bootstrap-IAM-cleanup). Eller: fortsätt Fas 1-features. | Behöver beslutas |

## 4. Mellan-arbete

Cleanup-passningar, disciplin-uppgraderingar och dokumentations-arbete som inte hör till någon enskild STEG. Klas använder begreppet "Fas 0.x" för cleanup-arbete mellan officiella faser.

| Period | Beskrivning | Källor | Status |
|--------|-------------|--------|--------|
| 2026-05-07 | Upptakt: ADR 0019 etablerad (solo direct-push), CLAUDE.md uppgraderad (§9.4 discovery, §9.5 web-search, §9.2 utökad), tech-debt.md etablerad, hook-vakt fix:ad för Agent SDK-läget, precompact-rapporter exkluderade från versionshantering | Webb-chats: ADR 0019-chatt + Moment 1-5-chatt | Pågående (Moment 5 = denna tracker) |

## 5. Aktuellt

**STEG-fokus:** STEG 10b klar 2026-05-08. DELETE /me + GDPR Art. 17-cascade + HardDeleteAccountsJob (ADR 0024 D3-D6). **TD-16 STÄNGD HELT.** Inga aktiva STEG.

**STEG 7a** (Resume-aggregat backend): Komplett 2026-05-08.

**STEG 7b** (Frontend /cv): Komplett 2026-05-08.

**STEG 8** (Audit log-infrastruktur): Komplett 2026-05-08. Pipeline-behavior + marker-interface (ADR 0022). 10 commands i Application+Resume märkta `IAuditableCommand<TResponse>`. Audit-rad och data-mutation persisteras atomiskt i samma SaveChanges. +41 tester. TD-9 stängd, TD-16 ny.

**STEG 9** (Worker-pipeline + Hangfire): Komplett 2026-05-08. ADR 0023. Worker-aktivering + Hangfire 1.8.23 + Hangfire.PostgreSql 1.21.1. DI-modulär refaktor. Stale-detektering på Application-aggregatet (`LastStatusChangeAt` + `GhostedThresholdDays` per BUILD.md §schema). `DetectGhostedApplicationsJob` orchestrator. **Kritisk fångst:** Pipeline-bug där `MediatorPipelineBehaviors.InOrder` inte registrerade behaviors via `options.PipelineBehaviors` → tyst data-loss. Fix: `AddMediatorPipelineBehaviors()` open-generic DI. Newtonsoft.Json 13.0.3 transitiv CVE-pinning. +32 tester (9 Domain + 12 Application + 5 Architecture + 6 Worker SmokeTest). TD-17 ny (Hangfire prod-härdning, blocker för Fas 1 prod-deploy). TD-18 ny (intervju-states-utökning). TD-19 ny (defense-in-depth-förbättringar för Worker-jobb).

**Spec-drift fångad:** STEG 9-startprompt sade "ghosted_threshold_days per JobSeeker (default 21)" men BUILD.md §schema rad 715–727 specificerar **per Application**. Klas valde BUILD.md-versionen (DDD: Application-aggregatet äger sin tids-semantik). Steg-tracker uppdaterad ovan.

**Plan-design-modell:** STEG 9 körde plan-design via CC + dotnet-architect-validering (utan webb-Claude). Fungerade för väl-avgränsad infrastruktur när ADR 0022-spec fanns delvis färdig. Webb-Claude behövs inte för upprepningsmönster eller infrastruktur som har ADR-stöd.

**Test-strategi-validering:** Klas tillägg #3 (integration smoke-test framför manuellt smoke-test) bevisade sitt värde — manuellt smoke-test hade missat pipeline-bug-fyndet. Mönster: alla nya orchestrator-/Worker-jobb ska ha integration smoke-test med `[Trait("Category", "SmokeTest")]`.

**STEG 10a** (Audit-retention via partitioning): Komplett 2026-05-08. ADR 0024 D1+D2. `audit_log` partitionerad daglig. `AuditLogRetentionJob` registrerad i Hangfire 03:00 UTC. Komposit-PK `(id, occurred_at)`. **Lärdomar fångade:** PK-constraint följde inte med RENAME (fix: `ALTER TABLE … RENAME CONSTRAINT`); EF Core 10 PendingModelChangesWarning kräver `ValueGeneratedNever` på alla komposit-PK-kolumner; SqlQueryRaw + format-string tolkar `[0-9]{8}` som `{8}`-placeholder (fix: escape till `{{8}}`, smoke-test fångade). +7 tester (3 arch + 4 smoke). TD-16 del 1 stängd. TD-20 ny (defensiv refactor defererad).

**STEG 10b** (DELETE /me + Art. 17-cascade): Komplett 2026-05-08. ADR 0024 D3-D6. Två CC-design-frågor besvarade: secondary Redis-set för InvalidateAllForUserAsync + ny IAppDbContext-injektion i LoginCommandHandler. Säkerhets-fix från audit: returnera `Auth.InvalidCredentials` istället för `AccountPendingDeletion` för att undvika info disclosure (GDPR Art. 32). `AddCoreIdentityForWorker`-extension löser cross-context-fråga (Worker behöver UserManager utan HTTP-bagage). +17 tester totalt. TD-16 stängd. TD-21-25 nya (varav TD-21+TD-22 är Fas 1 prod-blockare).

**Nästa:** STEG 11 — kräver beslut. Se §6.

För session-detaljer och commit-historik, se `docs/current-work.md`.

## 6. Nästa STEG

**STEG 10b — DELETE /me + Art. 17-cascade**

ADR 0024 D3+D4+D5+D6 är designade i webb-Claude och justerade efter STEG 10.1-discovery. Implementation är bounded:

**Komponenter:**
- `IAuditTrailEraser`-port + Infrastructure-impl (audit-bypass-pattern via direct SQL UPDATE)
- `DeleteAccountCommand` som samlat Mediator-command (cascade soft-delete: JobSeeker + alla Application + alla Resume)
- `DELETE /me`-endpoint i `MeEndpoints.cs` + post-commit `InvalidateAllForUserAsync`
- `LoginCommandHandler`-blockering vid `JobSeeker.DeletedAt is not null` (kräver ny `IAppDbContext`-injektion)
- `HardDeleteAccountsJob`-orchestrator: Steg 0 orphan-cleanup + Steg 1 hämta soft-deletade > 30 dagar + Steg 2 hard-delete med explicit transaction + separat Identity-DELETE-boundary
- Architecture-test för `IAuditTrailEraser`-bypass-isolering (analog 10.7)
- Smoke-test för Art. 17-cascade + hard-delete
- Integration-test för DELETE /me end-to-end
- Runbook `docs/runbooks/account-deletion.md`

**Två öppna CC-design-frågor inom ADR-ramen:**

1. **`ISessionStore.InvalidateAllForUserAsync`-strategi** (ADR 0017 listar metoden som deferred): bygg secondary Redis-set `user:{userId}:sessions` (rek — proper bulk invalidation) eller SCAN-fallback (O(N) över alla session-nycklar — temporary, dokumenterat som tech-debt)
2. **`LoginCommandHandler`-blockering**: ny `IAppDbContext`-injektion (rek — standard pattern) för att hämta JobSeeker.DeletedAt mellan validate-credentials och session-create

**Status:** Designed, redo att implementeras. Klas beslutar startdatum.

## 7. Numreringsfotnot

Faktisk historisk numrering följer projektets utveckling, inte BUILD.md §18:s fas-indelning. Sammanfattning:

- **Sessions 1-5:** pre-STEG infrastrukturarbete. Hör till Fas 0.
- **STEG 1-2:** kärnkod-grundläggning (.NET solution, JobAd domain). Hör till Fas 0.
- **STEG 3+:** post-bootstrap arbete. Hör delvis till Fas 0 (frontend bootstrap STEG 4a — design system-baseline) och delvis till Fas 1 (auth + Core Domain).
- **STEG 4a/4b sub-numrering:** "a/b/c"-suffix används när ett STEG sträcker sig över flera sessioner med substantiellt distinkt scope.
- **Moment-numrering** (1-5 i upptakter): separat axel för mellan-arbete, inte STEG.

Renumrering har övervägts och avvisats — bryter audit-trail mot commits, sessions/-loggar och ADR-referenser.

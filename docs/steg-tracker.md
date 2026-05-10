# JobbPilot — STEG-tracker

> **Version:** 1.11
> **Senast uppdaterad:** 2026-05-10 (STEG 13c APPLIED — HTTPS aktivt på dev.jobbpilot.se)
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
| STEG 11 | Fas 1 | **Fas 1 prod-deploy-blockare-cleanup** (TD-22 → TD-17 → TD-21). **TD-22** stängd: `IIpAnonymizer`-port (Application) + impl (Infrastructure) lyft från `RequestContextProvider`-private metod, konsumeras nu även av `AuthAuditLogger` så app-loggens IP /24+/48-anonymiseras vid logg-tid. ADR 0024 D7 (App-logg-redaction + 30d CloudWatch-retention som matchar Art. 17-fönstret). EmailHash-HMAC defererad till Fas 2 → ny TD-27. **TD-17** stängd 5/6 punkter: `HangfireWorkerOptions` config-driven (allow-list-defense `IsDevelopment\|\|IsEnvironment(Test)` för PrepareSchemaIfNecessary, fail-loud-throw utanför), `BackgroundJobServerOptions.ShutdownTimeout=25s` + `HostOptions.ShutdownTimeout+3s` (Fargate SIGTERM 30s grace), cron-kollision åtgärdad (detect-ghosted 03:00 → 03:30 UTC), runbook `hangfire-schema.md` (Install.sql-export, GRANT-modell med REVOKE PUBLIC, 8-punkts dashboard-auth-checklista, kalibrerings-fas, idempotency-tabell). Punkt 4 (ConnectionStrings split) defererad till Fas 0-stängning (kräver två AWS Secrets Manager-poster). **TD-21** stängd: tre rate-limit-policies (account-deletion 1/60s per UserId med NoLimiter för anonymous, auth-write 20/min per IP, auth-loose 30/min per IP), `UseForwardedHeaders` middleware (KnownNetworks-prod-konfig defererad till runbook §3.3 pre-launch-gate), `OnRejected`-callback med LoggerMessage source-gen + Retry-After-header (RFC 6585), separat `StrictRateLimitApiFactory` för isolerad 429-integration-test, `parallelizeTestCollections=false` så env-var-overlay inte race:ar. Frontend typed-confirmation-UX defererad → ny TD-28. **Reviews:** alla 3 backend-block fick security-auditor + code-reviewer parallellt (TD-21 även re-review efter Sec-Major-fixes). 0 Critical/Blocker, alla Major-fynd fixade in-block. +27 tester (8 IpAnonymizer + 4 AuthAuditLogger inkl IPv6 + 1 IIpAnonymizer arch-test + 5 HangfireWorkerOptions + 6 RateLimitingOptions + 2 AuthWriteRateLimit + 1 strict-fixture). Backend totalt 502 tester gröna. | 2026-05-09 |

### Klara (forts.)

| STEG | Fas | Beskrivning | Sessions |
|------|-----|-------------|----------|
| STEG 12 | Fas 0 | **Kod-pre-launch-gates inför första prod-deploy** (Alt A1 av A4-sekvens A1→A2→A3). **Block 1:** TD-17 punkt 4 stängd — `HangfireConnectionStringResolver` lyft till statisk testbar metod, fallback-kedja `HangfireStorage → Postgres` så prod kan splitta access-yta (jobbpilot_worker DML-only) utan dev-overhead. Worker `appsettings.Production.json` overlay (PrepareSchemaIfNecessary=false + ShutdownTimeoutSeconds=25). **Block 2:** TD-21 KnownNetworks stängd — `ForwardedHeadersConfig` config-driven med fail-loud parse (System.Net.IPNetwork + KnownIPNetworks per .NET 10). Production-defense `EnsureSafeForEnvironment` allow-list `IsDevelopment\|\|IsEnvironment("Test")` (Sec-Major-1 fixad in-block) — tom KnownNetworks utanför dev/test → uppstart-throw → ECS-container startar inte. Api `appsettings.Production.json` overlay (KnownNetworks tom som pre-launch-gate, ForwardLimit=1 för ALB-only). **Block 3:** Två overlay-filer + comment-stilad i sektioner. **Sec-Major-2 docs-fix:** `aws-setup.md §3.3` förtydligad om CloudFront edge-IPs i AWS-managed prefix-list (com.amazonaws.global.cloudfront.origin-facing) — bara VPC-CIDR räcker inte för ForwardLimit=2; ALB-only-deploy ska sätta 1. **Reviews:** security-auditor (Sec-Major-1+2 fixade in-block, 3 Minor + 2 Nit defererade) + code-reviewer (0 Major, M1-M3 fixade in-block, 4 Nit "rätt val"). +35 tester (5 HangfireConnectionStringResolver + 17 ForwardedHeadersConfig parse + 14 EnsureSafeForEnvironment + 1 strict-fixture artifact). Backend 537 totalt (157 Domain + 183 Application + 23 Architecture + 26 Worker + 148 Api Integration). | 2026-05-09 |
| STEG 13a | Fas 0 | **APPLIED 2026-05-10.** **Infra-as-code-stack: networking + databas + cache** (Alt A2 första block). Tre nya Terraform-modules + ny `environments/dev/` env-stack: **`modules/network/`** — VPC `10.0.0.0/16` 3-AZ, public/private subnets, single NAT (cost-optimized AZ-a), VPC Endpoints (S3 Gateway + Secrets Manager + KMS Interface; Bedrock utelämnad pga region-mismatch eu-north-1 ↔ eu-central-1), 5 strikta SGs (alb/ecs/rds/redis/vpce). **`modules/rds/`** — Postgres 18.3 db.t4g.medium Multi-AZ, KMS-encrypted storage + master-secret + Performance Insights, AWS-managed master-pwd via Secrets Manager (auto-rotation 7d), Enhanced Monitoring 60s, deletion_protection. **`modules/redis/`** — Valkey 8.0 replication group cache.t4g.small × 2 noder, multi-AZ failover, transit + at-rest encryption, AUTH-token (64 chars `[a-zA-Z0-9]`, ~380 bits entropy) i Secrets Manager. **`environments/dev/`** — komponerar modulerna via `data "aws_kms_alias"`-lookup mot baseline-master-key, två dev-secrets-placeholders (db/app + db/hangfire-storage) som sätts post-DDL i STEG 14. **Sec-Major-1 + Sec-Minor-6 fixade in-block** — RDS parameter-group: `log_statement=none` (hindrar password-leak vid STEG 14 DDL), `log_parameter_max_length=0` + `_on_error=0` (trunkerar bind-värden i slow-query-log → ingen PII via WHERE), explicit `aws_cloudwatch_log_group` med 30d retention + KMS (uppfyller ADR 0024 D7). **Sec-Major-2 ADR-accepterad** via ADR 0025 (ECS-egress 0.0.0.0/0 Fas 0-acceptance, omvärderingstrigger Fas 1→2 + förberedd hardening-väg). 5 Minor + 3 Nit defererade. **Ingen .NET-kod rörd** (537 tester oförändrade). **Inte applied** — kräver SSO-login, budget-höjning ($50→$200), version-verifiering (Valkey 8.0, postgres18 family-sträng) som operativa pre-apply-steg. | 2026-05-09 |
| STEG 13c | Fas 0 | **APPLIED 2026-05-10** (HTTPS smoke-test PASS på `https://dev.jobbpilot.se/api/ready` med HSTS-header). **Edge + DNS + HTTPS-flip** (ADR 0026 trigger 1 / TD-30, deadline 2026-06-08 — uppfylld 28 dagar i förväg). Domän `jobbpilot.se` registrerad hos STRATO (~80 kr/år). Två nya Terraform-modules: **`modules/route53/`** (apex hosted zone `Z028392711DGTDR1MGVC9` i prod/baseline som global delad resurs likt KMS) + **`modules/acm/`** (DNS-validerad cert med `aws_acm_certificate_validation` blocking-resource, timeouts.create=75m för svensk-registrar-marginal). dev-stack edit: `data "aws_route53_zone"` lookup + `module "acm_dev"` + A-ALIAS `dev.jobbpilot.se → ALB`. ALB-modul-edit: HTTP→HTTPS-redirect status_code 301 (AWS ALB hardlimited till 301/302; 308 ej implementerbart utan Lambda@Edge). **Sec-Major-2 fixad** (security-auditor): HSTS-implementation i Api/Program.cs — ny `HstsOptions` config-class + EnsureSafeForEnvironment-paritet med ForwardedHeadersConfig (dotnet-architect Viktigt-fynd 5), gate:at på `!IsDevelopment() && AlbOptions.HttpsEnabled`, FÖRE UseHttpsRedirection (förutsätter UseForwardedHeaders, dokumenterat). max-age=31536000 + includeSubDomains, preload defererat till prod-launch. **Sec-Major-1 dokumenterat** (DNSSEC defererad till Fas 1 via ADR 0027 med 4 trigger-villkor). 3 agent-reviews (code-reviewer APPROVE-with-fixes M-1 fixed timeouts.create=75m; security-auditor 0 Critical, 2 Sec-Major i ADR 0027, 3 Sec-Minor; dotnet-architect APPROVE-with-fixes Viktigt-fynd 5 inline). 5 nya TDs (TD-32 PQ-TLS-policy, TD-33 HSTS-pipeline-test, TD-34 DNSSEC Fas 1, TD-35 apex+www cert, TD-36 mTLS Fas 2). +17 tester (165/165 integration-gröna inkl. EnsureSafeForEnvironment Theory-cases). Apply: prod/baseline (1 add hosted zone) + dev (4 add ACM+CNAME+validation+ALIAS) + dev (4 add 1 change HTTPS-flip) — total ~3 min ECS rolling deployment. ADR 0026 → Superseded by ADR 0027. README.md tillagd separat (sober civic-utility-stil, 17 snabblänkar, 2 Mermaid-diagram). | 2026-05-10 |
| STEG 13b | Fas 0 | **APPLIED 2026-05-10** (Api smoke-test PASS via ALB; Worker restart-loop pga PLACEHOLDER-creds tills STEG 14). 4 in-flight kod-fixar krävdes: KMS-policy CloudWatch Logs grant, ASCII-only SG-descriptions, Dockerfile noble base-image (.NET 10 GA bytte default OS Bookworm→Noble), placeholder secret-versions med ignore_changes. Total apply 45 min. Spend ~$79/mån löper. Se `docs/sessions/2026-05-10-0730-steg13b-apply.md`. **Container-infra: ECR + IAM + CloudWatch + ALB + ECS Fargate** (Alt A2 andra block). Fem nya Terraform-modules: **`modules/ecr/`** — 2 separata repos (api, worker), KMS-encryption, scan_on_push, lifecycle keep-last-10. **`modules/cloudwatch_logs/`** — 3 LogGroups (api, worker, ecs-exec) med 30d retention + KMS (ADR 0024 D7). **`modules/iam_ecs/`** — execution-role + task-role-api + task-role-worker. Defense-in-depth via `aws:SourceAccount`-condition + `kms:ViaService`-restriction. Bedrock-policy attach via `data "aws_iam_policy"`-lookup. **`modules/alb/`** — internet-facing ALB, drop_invalid_header_fields, HTTP-listener default → target-group-api, HTTPS-listener gated på `var.https_listener_enabled` + `lifecycle.precondition` på acm_certificate_arn. **`modules/ecs/`** — Fargate cluster + Container Insights + capacity providers (FARGATE_SPOT default i dev = ~70% rabatt), task-defs (api: 0.5 vCPU + 1 GB med /api/ready-health; worker: 0.25 vCPU + 0.5 GB HTTP-fri per ADR 0023), services med deployment_circuit_breaker, autoscaling gated på var. Plus 2 Dockerfiles (multi-stage .NET 10, non-root `USER app`, no curl/HEALTHCHECK), `.dockerignore`, `/api/ready`-endpoint i Api/Program.cs (TODO TD-29: liveness ej readiness — fix vid Fas 2), `AlbOptions`-record i Api/Configuration. **Sec-Major-1 ADR-accepterad** via ADR 0026 (ALB HTTP-only Fas 0, 30d-tidsfönster deadline 2026-06-08, 5 triggers, mitigation-stack: rate-limiting + IP-anonymisering + audit-cascade + CloudTrail + restriktiv egress + DNS-disciplin). **Sec-Major-2 fixad in-block** — UseHttpsRedirection env-gate via `AlbOptions.HttpsEnabled` (matchande `var.alb_https_enabled` i Terraform → atomisk single source of truth med ALB-listener). **Kritisk Redis CS-mismatch fixad** (dotnet-architect-fynd) — komponerad CS i Terraform till single secret `ConnectionStrings__Redis = "host:port,password=...,ssl=True,abortConnect=False"` matchar `Infrastructure/DependencyInjection.cs:90` `GetConnectionString("Redis")`-pattern. Worker-konfig minimerad: ingen Redis (Worker använder inte Redis — verifierat). 4 agent-reviews (security-auditor approved Sec-Major-1+2 stängda, dotnet-architect OK, adr-keeper godkänd, code-reviewer approved 4 Minor varav M1+M2 fixade in-block). 3 nya TDs (TD-29 strict readiness Fas 2, TD-30 domänköp deadline 2026-06-08, TD-31 test för UseHttpsRedirection-gate). Cost vid apply: ~$79/mån (RDS $13 + Redis $8 + NAT $32 + ALB $16 + ECS $7 + ECR/CW $3). **Inte applied** — kräver `docker build/push` mellan IAM/CloudWatch/ECR-skapning och ALB/ECS-service-startup (annars `image_pull_failure`). | 2026-05-09 |

### Pågående

(inga aktiva STEG just nu — se §5)

### Planerade

| STEG | Fas | Beskrivning | Status |
|------|-----|-------------|--------|
| STEG 14 | Fas 0 | **GitHub Actions tag-pipeline + första prod-deploy** (Alt A3). `.github/workflows/` för build+test+push-to-ECR + tag-trigger för deploy (`v*-dev`/`v*-rc`/`v*`). Hangfire schema-DDL via Install.sql + REVOKE PUBLIC i RDS. ConnectionStrings split i AWS Secrets Manager (jobbpilot_app + jobbpilot_worker). Bootstrap-IAM-user cleanup. Första deploy till dev.jobbpilot.se. **Stänger Fas 0** per BUILD.md §18. | Planerad |

## 4. Mellan-arbete

Cleanup-passningar, disciplin-uppgraderingar och dokumentations-arbete som inte hör till någon enskild STEG. Klas använder begreppet "Fas 0.x" för cleanup-arbete mellan officiella faser.

| Period | Beskrivning | Källor | Status |
|--------|-------------|--------|--------|
| 2026-05-07 | Upptakt: ADR 0019 etablerad (solo direct-push), CLAUDE.md uppgraderad (§9.4 discovery, §9.5 web-search, §9.2 utökad), tech-debt.md etablerad, hook-vakt fix:ad för Agent SDK-läget, precompact-rapporter exkluderade från versionshantering | Webb-chats: ADR 0019-chatt + Moment 1-5-chatt | Pågående (Moment 5 = denna tracker) |

## 5. Aktuellt

**STEG-fokus:** **STEG 13c APPLIED 2026-05-10** — HTTPS aktivt på `https://dev.jobbpilot.se`. ADR 0026 → Superseded by ADR 0027. Domän jobbpilot.se registrerad hos STRATO + NS pekar AWS-Route53. ACM-cert utfärdat via DNS-validation. ALB HTTP→HTTPS-redirect (301) + HSTS aktiv (max-age=31536000 + includeSubDomains). Api task-def rev 2 deployed med Alb__HttpsEnabled=true. Smoke-test PASS. Worker i restart-loop fortsätter (förväntat tills STEG 14 DDL+roller). Spend ~$79.50/mån löper ($79 STEG 13b + $0.50 hosted zone). README.md tillagd. **Nästa:** STEG 14 (GitHub Actions tag-pipeline + DDL-init + IAM cleanup) — stänger Fas 0 per BUILD.md §18.

**STEG 7a** (Resume-aggregat backend): Komplett 2026-05-08.

**STEG 7b** (Frontend /cv): Komplett 2026-05-08.

**STEG 8** (Audit log-infrastruktur): Komplett 2026-05-08. Pipeline-behavior + marker-interface (ADR 0022). 10 commands i Application+Resume märkta `IAuditableCommand<TResponse>`. Audit-rad och data-mutation persisteras atomiskt i samma SaveChanges. +41 tester. TD-9 stängd, TD-16 ny.

**STEG 9** (Worker-pipeline + Hangfire): Komplett 2026-05-08. ADR 0023. Worker-aktivering + Hangfire 1.8.23 + Hangfire.PostgreSql 1.21.1. DI-modulär refaktor. Stale-detektering på Application-aggregatet (`LastStatusChangeAt` + `GhostedThresholdDays` per BUILD.md §schema). `DetectGhostedApplicationsJob` orchestrator. **Kritisk fångst:** Pipeline-bug där `MediatorPipelineBehaviors.InOrder` inte registrerade behaviors via `options.PipelineBehaviors` → tyst data-loss. Fix: `AddMediatorPipelineBehaviors()` open-generic DI. Newtonsoft.Json 13.0.3 transitiv CVE-pinning. +32 tester (9 Domain + 12 Application + 5 Architecture + 6 Worker SmokeTest). TD-17 ny (Hangfire prod-härdning, blocker för Fas 1 prod-deploy). TD-18 ny (intervju-states-utökning). TD-19 ny (defense-in-depth-förbättringar för Worker-jobb).

**Spec-drift fångad:** STEG 9-startprompt sade "ghosted_threshold_days per JobSeeker (default 21)" men BUILD.md §schema rad 715–727 specificerar **per Application**. Klas valde BUILD.md-versionen (DDD: Application-aggregatet äger sin tids-semantik). Steg-tracker uppdaterad ovan.

**Plan-design-modell:** STEG 9 körde plan-design via CC + dotnet-architect-validering (utan webb-Claude). Fungerade för väl-avgränsad infrastruktur när ADR 0022-spec fanns delvis färdig. Webb-Claude behövs inte för upprepningsmönster eller infrastruktur som har ADR-stöd.

**Test-strategi-validering:** Klas tillägg #3 (integration smoke-test framför manuellt smoke-test) bevisade sitt värde — manuellt smoke-test hade missat pipeline-bug-fyndet. Mönster: alla nya orchestrator-/Worker-jobb ska ha integration smoke-test med `[Trait("Category", "SmokeTest")]`.

**STEG 10a** (Audit-retention via partitioning): Komplett 2026-05-08. ADR 0024 D1+D2. `audit_log` partitionerad daglig. `AuditLogRetentionJob` registrerad i Hangfire 03:00 UTC. Komposit-PK `(id, occurred_at)`. **Lärdomar fångade:** PK-constraint följde inte med RENAME (fix: `ALTER TABLE … RENAME CONSTRAINT`); EF Core 10 PendingModelChangesWarning kräver `ValueGeneratedNever` på alla komposit-PK-kolumner; SqlQueryRaw + format-string tolkar `[0-9]{8}` som `{8}`-placeholder (fix: escape till `{{8}}`, smoke-test fångade). +7 tester (3 arch + 4 smoke). TD-16 del 1 stängd. TD-20 ny (defensiv refactor defererad).

**STEG 10b** (DELETE /me + Art. 17-cascade): Komplett 2026-05-08. ADR 0024 D3-D6. Två CC-design-frågor besvarade: secondary Redis-set för InvalidateAllForUserAsync + ny IAppDbContext-injektion i LoginCommandHandler. Säkerhets-fix från audit: returnera `Auth.InvalidCredentials` istället för `AccountPendingDeletion` för att undvika info disclosure (GDPR Art. 32). `AddCoreIdentityForWorker`-extension löser cross-context-fråga (Worker behöver UserManager utan HTTP-bagage). +17 tester totalt. TD-16 stängd. TD-21-25 nya (varav TD-21+TD-22 är Fas 1 prod-blockare).

**STEG 11** (Fas 1 prod-blockare-cleanup): Komplett 2026-05-09. Tre block i ordning TD-22 → TD-17 → TD-21, alla med parallella security-auditor + code-reviewer reviews per CLAUDE.md §9.2. **TD-22:** ADR 0024 D7 (App-logg-redaction + 30d retention). `IIpAnonymizer`-port lyft från privat metod, konsumeras nu av både audit-pipeline och AuthAuditLogger. **TD-17:** 5/6 punkter stängda — config-driven Hangfire-options med allow-list production-defense, Fargate SIGTERM 25s+3s timeout-kedja, runbook med GRANT-modell + REVOKE PUBLIC + 8-punkts dashboard-checklista. Punkt 4 (ConnectionStrings split) defererad till Fas 0-stängning. **TD-21:** rate-limiting på DELETE /me (1/60s per UserId med NoLimiter för anonymous) + auth-endpoints (20/min auth-write OWASP-CGN-kompatibel, 30/min auth-loose). UseForwardedHeaders + OnRejected med LoggerMessage source-gen + Retry-After (RFC 6585). Separat StrictRateLimitApiFactory för isolerad 429-integration-test. **Nya TD:** TD-27 (EmailHash-HMAC Fas 2), TD-28 (frontend typed-confirmation-UX). +27 tester. Backend 502 totalt.

**Lärdomar STEG 11:**
- `IConfiguration.GetSection().Get<>()` direkt vid Program.cs-startup istället för `IOptions<>` är OK när värdet bara läses vid host-uppstart — håller direct-binding-elegansen
- Allow-list production-defense (`IsDevelopment\|\|IsEnvironment("Test")`) > pure `IsProduction()` så Staging/Preprod/Demo inte tappar skydd
- xUnit `parallelizeTestCollections=false` krävs när två separata fixturer delar process-globala env-vars (här: rate-limit-overlay)
- Hangfire.AspNetCore drar in Microsoft.AspNetCore.* — bryter mot ADR 0023 Worker HTTP-fri-disciplin. Trim defererad till TD-19 Fas 2.
- LoggerMessage source-gen är obligatorisk under CA1848 i JobbPilot — `_logger.LogWarning(...)` i hot path bryter build

**STEG 12** (kod-pre-launch-gates): Komplett 2026-05-09. Alt A1 av Klas:s A4-sekvens (A1→A2→A3 för Fas 0-stängning). Tre block: Worker HangfireStorage-fallback (TD-17 punkt 4), Api ForwardedHeadersConfig + production-defense (TD-21 KnownNetworks), båda `appsettings.Production.json` overlays. **Sec-Major-1** (allow-list-symmetri-miss mellan Worker `safeForAutoSchema` och Api KnownNetworks-tomt-array) fixad in-block via lyft till testbar `EnsureSafeForEnvironment(envName)`-metod på `ForwardedHeadersConfig`. **Sec-Major-2** (vilseledande overlay-kommentar om CloudFront) docs-fixad — runbook §3.3 förtydligad om AWS-managed prefix-list för CloudFront edge-IPs. +35 tester (537 backend-totalt). Reviews: 0 Critical/Major-blocker. M1-M3 fixade in-block.

**STEG 13a** (Terraform dev-stack — networking + databas + cache): Kod-skriven 2026-05-09 (ej applied). Tre nya `modules/{network,rds,redis}/` + ny `environments/dev/`-stack. VPC `10.0.0.0/16` 3-AZ, single NAT (cost-optimized), VPC Endpoints (S3+SecretsManager+KMS, Bedrock utelämnad pga region-mismatch). RDS Postgres 18.3 Multi-AZ + KMS-encryption end-to-end + AWS-managed master-secret. ElastiCache Valkey 8.0 (BUILD.md §15.1 sa "Redis 8.6" men Redis 8.x är post-license-byte och inte AWS-supportad — Valkey är AWS:s Redis-kompatibla efterföljare). Två dev-secrets-placeholders för STEG 14 DDL-värde-population. **Sec-Major-1 + Sec-Minor-6 fixade in-block** via RDS parameter-group (`log_statement=none` hindrar STEG 14 password-leak; `log_parameter_max_length=0` trunkerar bind-värden så slow-query-log inte exponerar PII; explicit `aws_cloudwatch_log_group` med 30d retention uppfyller ADR 0024 D7). **Sec-Major-2 ADR-accepterad** via ADR 0025 — ECS-egress `0.0.0.0/0` Fas 0-acceptance med dokumenterad mitigation-stack + omvärderingstrigger Fas 1→Fas 2 + förberedd hardening-väg. 5 Minor + 3 Nit defererade till STEG 13b/staging. **Inte applied** — kräver SSO-login + budget-höjning ($50→$200) + version-verifiering (Valkey/PG family-strängar via AWS API) innan `terraform apply`.

**STEG 13b** (container-infra): Komplett 2026-05-09 (kod, ej applied). Fem moduler + Dockerfiles + Api/Program.cs-edit. ADR 0026 + 4 agent-reviews. **Sec-Major-1 ADR-accepterad** med 30d-tidsfönster + 5 triggers. **Sec-Major-2 fixad** via UseHttpsRedirection env-gate (single source of truth med Terraform-variabel). **Kritisk Redis CS-mismatch upptäckt av dotnet-architect** — fix: komponera CS som single Secrets Manager-secret. Worker-konfig minimerad (ingen Redis). 3 nya TDs.

**STEG 13c** (Edge + DNS + HTTPS-flip): Komplett 2026-05-10. Två nya Terraform-modules (route53 + acm) + ALB 308→301-fix (AWS-API-begränsning upptäckt mid-apply) + HSTS-implementation i Api. ADR 0027 supersedar 0026. README.md tillagd. 165/165 integration-tester gröna. 5 nya TDs.

**Lärdomar STEG 13c:**
- **AWS ALB `RedirectActionConfig.StatusCode` är hardlimited till HTTP_301 | HTTP_302.** 308 (Permanent Redirect, POST-method-bevarande) är inte implementerbart utan Lambda@Edge eller CloudFront-rewrite. Validering fångas inte av `terraform validate` (AWS-API-side). Lärdom: vid AWS-resource-fält som tar enum-strängar — kolla AWS-docs INNAN code-review eftersom `terraform validate` ger false-positive. 301 räcker i praktiken eftersom HSTS+browser-cache gör POST-anrop går direkt till HTTPS efter första GET.
- **STRATO-NS-propagering snabbare än varnat.** STRATO-UI varnar "upp till 24h" men `.se`-zonen propagerade inom ~30 min (Resolve-DnsName mot 8.8.8.8/1.1.1.1/9.9.9.9 visade AWS-NS efter NS-edit). Lärdom: använd `aws_acm_certificate_validation.timeouts.create` med marginal (75m) men förvänta snabbare i praktiken. Worst-case scenario sällan worst-case.
- **`aws_acm_certificate_validation`-resursen är idempotent vid timeout.** Cert står PENDING_VALIDATION upp till 72h innan AWS auto-deletar. Re-apply efter timeout plockar upp där den slutade — inga recreates, inget förlorat $$. Detta gör "apply nu i background och re-applya vid timeout" till en säker strategi.
- **HSTS-pipeline-ordning kritiskt:** `UseForwardedHeaders → UseHsts → UseHttpsRedirection`. Bakom ALB är `Request.IsHttps` annars false → HSTS-headern sätts aldrig. dotnet-architect-fynd som behöver dokumenteras i kod-kommentar för att framtida refactors inte ska bryta ordningen.
- **Cross-stack data-lookup-pattern (data "aws_route53_zone")** följer KMS-master-key-pattern från STEG 13a. Ren state-isolation; dev-stack vet inget om prod/baseline-state. Bra blueprint för framtida delade resurser (ACM-cert för apex i prod när lansering närmar sig, etc).
- **Klas's existing README-mall (Tokyo Night-tema) passade inte civic-utility-position.** README skrevs i sober stil — inga animerade SVGs, inga emojis i prosa, två Mermaid-diagram för arkitektur-läsbarhet, 17 snabblänkar för skim-navigering. AI-läsbarhet säkrad via semantiska headers + tabellformat.

**Lärdomar STEG 13b:**
- **Konfig-mismatch mellan IaC och .NET-konsumtion är klassisk fotgropfälla.** Initial impl injicerade Redis Host/Port/AuthToken som 3 separata secrets/env-vars. Infrastructure-koden läste `GetConnectionString("Redis")` som single string. Hade inte upptäckts före första prod-uppstart om dotnet-architect inte invocerats parallellt med security-auditor. Lärdom: vid IaC-konsumtion av .NET-config — verifiera mot existerande config-läsning innan plan-design.
- **Fargate ignorerar Docker `HEALTHCHECK`-direktiv** (per AWS-docs — bara EC2-launch-type respekterar). Container-level HEALTHCHECK i image är meningslöst overhead för Fargate-tasks. Använd ECS task-def `healthCheck`-block ELLER ALB target-group health-check. Vi valde target-group + container exit code som dubbel-täckning.
- **`UseHttpsRedirection()` × HTTP-only-ALB är fail-loud-falla** — redirect 307→443 mot stängd port → ALB target-group failer → ECS deployment_circuit_breaker rollback. Konfigdriven gate via env-var som flippas synkront med ALB-listener-config = atomisk konsistens.
- **AWS-managed prefix-list saknas för Bedrock-egress** — cross-region-trafik från eu-north-1 → eu-central-1/eu-west-1 går via NAT eftersom Bedrock-tjänsten inte har region-prefix-list. Validerat i ADR 0025 + ADR 0026 hardening-väg.
- **`lifecycle.precondition` är fail-fast vid plan-tid** (Terraform 1.x+) — kraftfullt mot operatör-misstag som "https_listener_enabled=true men acm_certificate_arn=null". Failar i plan istället för i apply.

**Lärdomar STEG 13a:**
- **Cost-policy: dev = deploy-pipeline-verifierare, inte produktions-mirror.** Initial implementation defaultade till BUILD.md §15.1:s prod-spec (db.t4g.medium Multi-AZ + cache.t4g.small × 2 + Interface Endpoints) → ~$140/mån för dev. Klas:s prio (kostnadskontroll dag 1) kräver lean-dev: t4g.micro Single-AZ + t4g.micro × 1 + S3-only-endpoint → ~$30/mån. Multi-AZ + replicas testas först i staging/prod. Modul-defaults är lean; staging/prod sätter Multi-AZ explicit.
- **`monthly_budget_usd` är alert-tröskel, inte spending-cap.** Att höja $50→$200 sänker inte spending — det fördröjer alarmet. Rätt strategi vid kostnadskontroll: sänk faktiska resurser, behåll låg alert-tröskel för tidig varning.
- BUILD.md §15.1 "Redis 8.6" är spec-drift mot AWS-verklighet — Redis 8.x är Redis Inc:s post-license-byte-version som AWS inte adopterat. Valkey 8 är AWS:s Redis-kompatibla efterföljare. Justera BUILD.md vid en lämplig docs-pass.
- RDS `log_statement="ddl"` är klassisk fotgropfälla vid Postgres-tuning — DDL-statements innehåller `CREATE/ALTER ROLE PASSWORD '...'` i klartext. Sätt `none` i prod, byt till `mod` bara om DDL-audit verkligen behövs (och då via en pattern som strippar passwords pre-execution).
- `log_parameter_max_length=0` är icke-trivialt rätt — `log_min_duration_statement` ensam är inte PII-säker eftersom slow-query-textens bind-värden loggas. Krävs Postgres 13+.
- AWS-managed CloudWatch LogGroups skapas implicit av RDS/ECS/Lambda med default `Never expire` — alltid deklarera explicit `aws_cloudwatch_log_group` *före* tjänst-skapning för att kontrollera retention + KMS.
- Cross-region Bedrock från eu-north-1 fungerar via NAT Gateway, men ingen lokal VPC Endpoint finns för Bedrock i regioner där tjänsten inte är hostad.
- `data "aws_kms_alias"`-lookup mellan stackar är cleanare än `data "terraform_remote_state"` — inga tfstate-permissions att hantera, ingen koppling till baseline-stackens version.

**Lärdomar STEG 12:**
- .NET 10 `Microsoft.AspNetCore.HttpOverrides.IPNetwork` är deprecated → använd `System.Net.IPNetwork.TryParse` + `ForwardedHeadersOptions.KnownIPNetworks` istället
- Allow-list-pattern (Worker `safeForAutoSchema` + Api `EnsureSafeForEnvironment`) ska replikeras strukturellt mellan parallella entry-points — symmetri-miss → tyst no-op-yta i prod
- ForwardLimit > 1 kräver att ALLA hops är i KnownNetworks/KnownProxies — bara VPC-CIDR räcker inte vid CloudFront+ALB-kedja eftersom CloudFront edge-IPs ligger i AWS-managed prefix-list, inte VPC
- ASP.NET JsonConfigurationProvider stödjer `// comments` sedan .NET Core 3.0 (`JsonReaderOptions.CommentHandling = Skip`) — användbart för load-bearing pre-launch-gate-dokumentation i overlays
- Statisk testbar metod på options-klassen > inline-Program.cs-logik så uppstart-validering kan unit-testas utan host-bygge

**Nästa:** STEG 12 — kräver beslut. Se §6.

För session-detaljer och commit-historik, se `docs/current-work.md`.

## 6. Nästa STEG

**STEG 12 — kräver beslut.** Tre primära kandidater:

**Alt A — Fas 0-stängning (rekommenderad efter STEG 11)**

Applicera STEG 11:s pre-launch-gates och göra första prod-deploy:
- Första deploy till dev.jobbpilot.se via GitHub Actions tag-pipeline (BUILD.md §15.3)
- AWS-konfig:
  - CloudWatch LogGroups retention=30 (TD-22 / aws-setup.md §3.2)
  - ALB ForwardedHeaders KnownNetworks=VPC-CIDR (TD-21 / aws-setup.md §3.3)
  - ConnectionStrings split: jobbpilot_app + jobbpilot_worker AWS Secrets Manager (TD-17 punkt 4 / hangfire-schema.md §4)
  - Hangfire schema-DDL via Install.sql körd av jobbpilot_migrations-roll (hangfire-schema.md §3)
  - REVOKE ALL ON SCHEMA hangfire FROM PUBLIC innan GRANT (Sec-Major-2 STEG 11)
- Bootstrap-IAM-user raderad (aws-setup.md §3.4)
- appsettings.Production.json med:
  - `Hangfire:PrepareSchemaIfNecessary: false`
  - `Hangfire:ShutdownTimeoutSeconds: 25`
- security-auditor invokeras vid Fas 0-block (deployment + IAM-cleanup)

**Alt B — Fortsätt Fas 1-features**

Per BUILD.md §18 milstolpe "manuell CV + 'fake' ansökningar":
- Application Management UX-pass (Resume-version-Tailored, status-flöde-polish)
- Andra Fas 1-features

**Alt C — TD-19 Worker defense-in-depth-cleanup (mindre scope)**

- Hangfire.AspNetCore → Hangfire.NetCore (Worker HTTP-fri-disciplin per ADR 0023)
- Architecture-test-utökning till allow-list-pattern
- Worker-orchestrator max-batch-size-guard

**Min rek:** Alt A naturlig efter STEG 11 — alla pre-launch-gates dokumenterade, Klas applicerar dem operativt och får första prod-miljö. Vid Alt B finns risk att STEG 11:s dokumentation åldras innan den används.

**Status:** Behöver beslutas av Klas.

## 7. Numreringsfotnot

Faktisk historisk numrering följer projektets utveckling, inte BUILD.md §18:s fas-indelning. Sammanfattning:

- **Sessions 1-5:** pre-STEG infrastrukturarbete. Hör till Fas 0.
- **STEG 1-2:** kärnkod-grundläggning (.NET solution, JobAd domain). Hör till Fas 0.
- **STEG 3+:** post-bootstrap arbete. Hör delvis till Fas 0 (frontend bootstrap STEG 4a — design system-baseline) och delvis till Fas 1 (auth + Core Domain).
- **STEG 4a/4b sub-numrering:** "a/b/c"-suffix används när ett STEG sträcker sig över flera sessioner med substantiellt distinkt scope.
- **Moment-numrering** (1-5 i upptakter): separat axel för mellan-arbete, inte STEG.

Renumrering har övervägts och avvisats — bryter audit-trail mot commits, sessions/-loggar och ADR-referenser.

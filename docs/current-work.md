# Current work — JobbPilot

**Status:** **D+A-session KOMPLETT 2026-05-13. TD-79 STÄNGD (Del D — `lifecycle.ignore_changes = [task_definition]` applied). TD-70 STÄNGD (Del A — F2-P9 search/filter-yta `?ssyk&?region&?q` levererad med Postgres generated columns + ListReadPolicy rate-limit). Full svit grön 915 backend-tester (+45 nya). 2 commits — Del D pushed `94ec84a`, Del A pending push. Klas-GO behövs för v0.2.5-dev tag-push (deploy-dev.yml automigrate).**
**Senast uppdaterad:** 2026-05-13 (D+A-session komplett, både TD-79 + TD-70 stängda)
**HEAD:** `94ec84a` (Del D pushed) + Del-A-commit pending
**Deploy:** `v0.2.4-dev` fortsatt live; v0.2.5-dev tag-push pending Klas-GO
**Långsiktig bana:** `docs/steg-tracker.md`
**Tech debt:** `docs/tech-debt.md` (aktiva) + `docs/tech-debt-archive.md` (stängda)
**Prod-checklist:** `docs/runbooks/v0.2-prod-launch-checklist.md`

---

## Aktivt nu — D+A-session KOMPLETT (TD-79 + TD-70 stängda)

### Levererat Del A (TD-70 — F2-P9 search/filter)

| Commit | Innehåll |
|---|---|
| (pending push) | feat(jobads): F2-P9 search/filter-yta ?ssyk&?region&?q + ListReadPolicy rate-limit (TD-70) |

**Endpoint:** `GET /api/v1/job-ads?ssyk=<concept-id>&region=<concept-id>&q=<text>` (auth-gated + rate-limited 60/min per UserId)

**CTO-rond:** 11 entydiga beslut (Q1-Q11) + 1 follow-up-triage av security-auditor Major (in-block-rate-limit-fix).

**Reviewers:** dotnet-architect → senior-cto-advisor → db-migration-writer → test-writer → security-auditor (Major: rate-limit → CTO-triage in-block) → senior-cto-advisor (rond 2) → code-reviewer APPROVED 0/0/2/2.

**Tests:** Domain 225 + Application **354** (+31) + Architecture 50 + Api **254** (+14) + Worker 26 + Migrate 6 = **915 grönt (+45 nya)**.

### Levererat Del D (TD-79 pipeline-hygien)

| Commit | Innehåll |
|---|---|
| `94ec84a` | chore(infra): lifecycle.ignore_changes=[task_definition] på ECS api+worker services (TD-79) |

**Plan-output post-fix:**

| Resurs | Pre-fix plan | Post-fix plan |
|---|---|---|
| `aws_ecs_service.api.task_definition` | ~ update | ❌ no-op |
| `aws_ecs_service.worker.task_definition` | ~ :8 → :1 (rollback) | ❌ no-op |
| `aws_ecs_task_definition.api` | -/+ replace | ✓ apply genomförd (revision :13 ny, service ignorerar) |
| `aws_db_parameter_group.this` | ~ apply_method cosmetic | ~ kvarstår (pre-existing, ej TD-79-scope) |

**Live-state efter apply:**
- `jobbpilot-dev-api`: TaskDef `:13` (CI/CD-ägd revision behållen)
- `jobbpilot-dev-worker`: TaskDef `:8` (NOT rolled back to `:1`)
- `https://dev.jobbpilot.se/api/ready` → HTTP 200 OK
- 3 CloudWatch-alarms fortsatt i OK-state
- AdminBootstrap__InitialAdminEmail nu Terraform-ägd i task-def-content (env-var-ägarskap löst)

### CTO-rond 2026-05-13 (v0.2-prod-tag-readiness) — 5 beslut

1. **Q1 v0.2-definition:** Tolkning (c) — första prod-deploy-triggande tag oavsett feature-completeness. Frontend kommer i `v0.2.x`-patch-tags efter. Motivering: Continuous Delivery (Humble/Farley 2010), Fitness Functions (Ford/Parsons/Kua 2017).
2. **Q2 BUILD.md §14.4-alerts:**
   - JobTech-sync 3 consecutive failures → **In-block-fix FÖRE tag** (fas-relevant + observability)
   - Backend 5xx-rate > 1% / 5 min → **TD-77 Fas 8** (YAGNI vid 1-user-volym)
   - DB CPU > 80% / 10 min → **TD-78 Fas 8** (samma logik)
3. **Q3 SystemEventAuditor failure-alarm (EventId 5602) → In-block-fix FÖRE tag** (ADR 0035 §6 egen leveransspec; Art. 30 record-of-processing-kongruens)
4. **Q4 RDS backup-retention:** **14d för prod** (industry-common, EDPB CEF 2025 verifierad acceptans, KISS över 35d-max utan TD-13)
5. **Q5 TD-13 (PII-encryption + crypto-erasure):** **Defer Fas 2-stängning** (EDPB CEF 2025 verifierar standard practice räcker, fas-regel CLAUDE.md §9.6)

### Smoke-test 2026-05-13 — AUDIT-WIRE VERIFIERAD LIVE

CloudWatch Logs Insights mot `/aws/ecs/jobbpilot-dev/worker`:

| Cron-tick | Stream-result | audit_log INSERT |
|---|---|---|
| 08:21:55 UTC | fetched=1029, added=72, errors=0 | ✓ INSERT INTO audit_log (… payload …) |
| 08:30:47 UTC | fetched=1076, added=84, errors=0 | ✓ INSERT INTO audit_log (… payload …) |
| 08:40:41 UTC | (pågående vid query-tid) | ✓ INSERT INTO audit_log (… payload …) |

`SystemEventAuditor` skriver `System.JobAdsSynced` per cron-tick via
idempotens-check + insert. **0 EventId 5602 (Critical audit failure)** i
loggarna. TD-73 audit-wire fungerar i prod-flöde.

### Web-search-källor (CLAUDE.md §9.5, verifierade 2026-05-13)

- [AWS RDS Backup Retention](https://docs.aws.amazon.com/AmazonRDS/latest/UserGuide/USER_WorkingWithAutomatedBackups.BackupRetention.html) — default 7d console / 1d API, max 35d
- [EDPB CEF 2025 Report (PDF, 2026-02)](https://www.edpb.europa.eu/system/files/2026-02/edpb_cef-report_2025_right-to-erasure_en.pdf) — automatic overwrite cycles + live-radering acceptabelt; crypto-erasure inte krav
- [Terraform aws_cloudwatch_log_metric_filter](https://registry.terraform.io/providers/hashicorp/aws/latest/docs/resources/cloudwatch_log_metric_filter)
- [Terraform aws_cloudwatch_metric_alarm](https://registry.terraform.io/providers/hashicorp/aws/latest/docs/resources/cloudwatch_metric_alarm) — provider v6.30 stable

### TD-status

- **TD-77** lyft 2026-05-13 — Backend 5xx-rate-alarm, Fas 8 Klass-launch
- **TD-78** lyft 2026-05-13 — DB CPU > 80% alarm, Fas 8 Klass-launch
- **TD-13** Major Fas 2 — bekräftad ej launch-blocker per CTO Q5 + EDPB CEF 2025

Aktiva: 21 (TD-13 + TD-26 Major; resten Minor).

### Pending Klas-GO (in-block-fix-batch FÖRE v0.2-tag)

Per `docs/runbooks/v0.2-prod-launch-checklist.md` §9. Tre leveranser:

1. **CloudWatch-alarm: JobTech-sync 3 consecutive failures** — Terraform-utbyggnad i `modules/cloudwatch_security_alarms` (eller ny `cloudwatch_ops_alarms`-modul)
2. **CloudWatch-alarm: SystemEventAuditor failure (EventId 5602)** — stänger ADR 0035 §6-gap
3. **RDS backup-retention 7d → 14d** — prod-Terraform (dev oförändrad)

**Scope:** 2-3 commits, ~3-4h CC-tid.
**Klas-STOPP-territorium per CLAUDE.md §9.6 punkt 5:** v0.2-definition är strategisk + prod-Terraform-state + tag-push behöver explicit Klas-GO.

### Pending operativt för Klas (sedan tidigare)

- AWS SSO-token-livslängd (re-auth med `aws sso login --profile jobbpilot` vid behov)
- JobTech-API-key registrering (apirequest.jobtechdev.se nedlagd; v2 är open API)
- Frontend-deploy till Vercel (kommer i v0.2.x-patch efter v0.2)
- BUILD.md §9.1 sync mot ADR 0032 §3 — Klas-instruktion krävs

---

## Tidigare aktivitet (TD-73 prod-gating-batch — komplett)

### Tidigare commits

| Commit | Innehåll |
|---|---|
| `c13e1ce` | feat(jobads): TD-73 prod-gating — audit-wire α + right-to-erasure för rekryterar-PII |

### Granskningstrail

- `docs/sessions/2026-05-13-0730-td73-prod-gating.md` — session-log (skapas i denna session-end)
- Reviewers INLINE: dotnet-architect + senior-cto-advisor + code-reviewer + security-auditor
- Tidigare session: `docs/sessions/2026-05-13-0700-f2-p8c-hangfire-jobs.md`

### Leveranser

| Område | Innehåll |
|---|---|
| **Ny ADR 0035** | System-event audit-pipeline (bypass-port parallell till IAuditTrailEraser). EventType-konvention `System.<Event>`, AggregateType `System.<Aggregate>`. Idempotens-skydd vid Hangfire-retry. Best-effort-semantik vid audit-failure. |
| **ADR 0032 amendment** | §8 punkt 4 levererad: audit-wire via `ISystemEventAuditor` (inte domain-event), Email-only right-to-erasure, Name→TD-75, GIN-index→TD-76 |
| **ADR 0024 cross-ref-amendment** | Pekare till ADR 0035 + ADR 0032 §8 för rekryterar-PII-cascade (separat från ADR 0024 D6 user-cascade) |
| **Domain** | `AuditLogEntry.Payload` + `CreateSystemEvent`-factory (bevarar Guid.Empty-invariant) |
| **Application ports** | `ISystemEventAuditor`, `IRecruiterPiiPurger`, `SystemAuditEvent`-record-hierarki, `RedactRecruiterPiiCommand` (+ validator + enum) |
| **Infrastructure** | `SystemEventAuditor` (idempotens-check via (EventType, AggregateId)-lookup), `RecruiterPiiPurger` (`EF.Functions.JsonContains` + `ExecuteUpdateAsync`), EF-migration `AddAuditLogPayload` |
| **EF-config** | `AuditLogEntryConfiguration.Payload` jsonb-mapping |
| **Worker/Hangfire** | Audit-wire i `SyncPlatsbankenStreamJob` (finally med exception-mask-skydd), `SyncPlatsbankenSnapshotJob`, `PurgeStaleRawPayloadsJob` |
| **Admin endpoint** | `POST /api/v1/admin/job-ads/redact-recruiter-pii` med `RequireAuthorization(Admin)` + `JsonStringEnumConverter` |
| **Architecture-tester** | ISystemEventAuditor + IRecruiterPiiPurger konsumentlistor (Application + Infrastructure) |
| **Runbooks** | `recruiter-pii-erasure.md` (auto-flöde Email + manuell-flöde Name); `gdpr-processing-register.md` uppdaterad |

### Reviewers INLINE (CLAUDE.md §9.2)

| Reviewer | Tidpunkt | Verdict |
|---|---|---|
| dotnet-architect | INNAN kod | Design-skiss approved; 5 multi-approach → CTO |
| senior-cto-advisor | EFTER architect, INNAN kod | 13 beslut entydigt mot principer (Martin/Evans/Fowler/Beck/Saltzer-Schroeder/GDPR). **INGET Klas-STOPP** behövdes per CLAUDE.md §9.6 punkt 5 |
| code-reviewer | EFTER impl, INNAN commit | GO. 0 Blocker, 0 Major, 3 Minor (Minor-1 + Minor-2 in-block-fixade per §9.6; Minor-3 är planerad uppföljning) |
| security-auditor | EFTER impl, INNAN commit | APPROVED-WITH-CONDITIONS. 0 Critical, 0 GDPR-Blocker, 0 Major, 4 Sec-Min (acceptable as-is) |

### CTO-rond 2026-05-13 (TD-73 prod-gating) — 13 beslut

1. **Q1 AggregateId:** Per-run-Guid (via Hangfire jobId-pattern) — OCP-väg framåt
2. **Q2 Erasure-shape:** Total null-out via `SetProperty(_ => null)` — KISS + data-minimisation > debug-värde
3. **Q3 Audit-granularitet:** En aggregerad audit-rad per request — ADR 0024 D4-precedens
4. **Q4 RedactCmd.AggregateId:** Per-request-Guid (RequestId) — följer Q3
5. **Q5 GIN-index:** Defer till TD-76 — YAGNI vid F2-volym
6. **R-Risk1 Atomicitet:** Best-effort + Hangfire retry + idempotens-check + Critical log — Fowler 2018
7. **R-Risk2 Name-matching:** Email-only nu, Name som TD-75 — YAGNI + Art. 17 kräver inte name-identifier
8. **M1 ADR-shape:** Ny ADR 0035 + amendment till ADR 0032 §8 + cross-ref ADR 0024 — Ford/Parsons/Kua immutability
9. **M2 Klas-STOPP-buntning:** INGET Klas-STOPP — entydiga principer i alla 13 frågor
10. **M3 Snapshot-shim:** SyncPlatsbankenSnapshotCommand har redan inte IAuditableCommand — no-op
11. **M4 ICorrelationIdProvider:** Impl-validation räcker
12. **M5 SystemEventAuditor lifetime:** Scoped (matchar IAppDbContext)
13. **M6 Volym:** GIN-defer korrekt även vid sanity-check (5-15k INSERTs/dygn netto)

### Web-search-källor (CLAUDE.md §9.5, verifierade 2026-05-13)

- [Npgsql 10.0 Release Notes](https://www.npgsql.org/efcore/release-notes/10.0.html)
- [Trailhead Technology — EF Core 10 PostgreSQL Hybrid DB](https://trailheadtechnology.com/ef-core-10-turns-postgresql-into-a-hybrid-relational-document-db/)
- [GitHub Issue #3745](https://github.com/npgsql/efcore.pg/issues/3745) — Contains-regression
- [PostgreSQL Docs 18 — GIN Indexes](https://www.postgresql.org/docs/current/gin.html)
- [pganalyze — GIN Index The Good and Bad](https://pganalyze.com/blog/gin-index)

### Tester (full svit grön)

- Domain.UnitTests: 218 → **225** (+7: CreateSystemEvent-invarianter + Payload-default)
- Application.UnitTests: 307 → **323** (+16: SystemEventAuditor + RedactCommand + Validator)
- Architecture.Tests: 46 → **50** (+4: ISystemEventAuditor + IRecruiterPiiPurger konsumentlistor × Application + Infrastructure)
- Api.IntegrationTests: 234 → **240** (+6: AdminRedactRecruiterPiiTests end-to-end mot Postgres)
- Worker.IntegrationTests: 26 (oförändrat)
- Migrate.UnitTests: 6 (oförändrat)

Totalt backend: 837 → **870 grönt** (+33 nya).

### Disciplinmissar fångade + fixade

1. **Architect föreslog `EF.Functions.JsonContains` i Application-handler** — Clean Arch-brott (Npgsql i Application). Refactor: skapade `IRecruiterPiiPurger` Application-port + Postgres-impl. Samma mönster som `IAuditTrailEraser`.
2. **Architect+arch-test listade `RedactRecruiterPiiCommandHandler` som ISystemEventAuditor-konsument** — fel; handlern är `IAuditableCommand` + går via `AuditBehavior`. Fixad i arch-test + ADR 0035 §7 docs-not.
3. **Stream-job finally-block kunde maska originalexception vid audit-failure** (code-reviewer Minor-1). Fixad in-block med try/catch (CA1031-suppress) + Cwalina/Abrams §7.5-not.
4. **`JsonStringEnumConverter` saknades** för admin-endpoint enum-deserialisering — fixad via `[JsonConverter(typeof(JsonStringEnumConverter<>))]` på `RecruiterIdentifierType`.

### Tag-cykel + deploy

- `v0.2.4-dev` på `c13e1ce` → push 08:13 UTC → deploy run `25786909619`.
- Deploy completion: 08:20 UTC (~6m42s).
- Ready-probe: `https://dev.jobbpilot.se/api/ready` → **200 OK** verifierat efter deploy.

### Smoke-test status — väntar nästa cron-tick

**Pending verifikation:** Nästa stream-cron `*/10` (08:40 UTC) ska skriva
första `System.JobAdsSynced`-raden i `audit_log` via nya `ISystemEventAuditor`.
Verifikation via CloudWatch logs (Worker-task) eller psql mot dev-RDS:

```sql
SELECT event_type, aggregate_type, aggregate_id, occurred_at,
       payload->>'Source' as source,
       payload->>'Fetched' as fetched,
       payload->>'Added' as added
FROM audit_log
WHERE event_type LIKE 'System.%'
ORDER BY occurred_at DESC
LIMIT 5;
```

Förväntad rad: `event_type = 'System.JobAdsSynced'`, payload med counts.

### TD-status

- **TD-73** Major → **STÄNGD 2026-05-13** (flyttad till `tech-debt-archive.md`)
- **TD-75** Minor lyft — Name-baserad rekryterar-PII-radering (Trigger: första Name-begäran)
- **TD-76** Minor lyft — GIN-index på raw_payload jsonb (Trigger: latens >5s eller volym ×10)

Aktiva: 19 (TD-13 + TD-26 Major; resten Minor). **0 Major Fas Nu, 0 Major Fas 2 (gating blockerare borta).**

### Pending operativt (oförändrat sedan P8c)

- AWS SSO-token-livslängd (re-auth med `aws sso login --profile jobbpilot` vid behov)
- JobTech-API-key registrering (apirequest.jobtechdev.se nedlagd; v2 är open API)
- Frontend-deploy till Vercel
- BUILD.md §9.1 sync mot ADR 0032 §3 — Klas-instruktion krävs

---

## Nästa session — Klas-val

1. **v0.2-prod-tag-förberedelse:** TD-73 stängd; samla prod-launch-checklist (CloudWatch-alarms, retention-jobb-verifiering, ev. backup-strategi). Inga aktiva Major-blockare kvar i Fas 2.
2. **F2-P9 search/filter-yta** (TD-70): GET `/api/v1/job-ads` med `?ssyk=...&region=...&q=...` per JobTech v2 `occupation-concept-id` + `location-concept-id`.
3. **Frontend-deploy** till Vercel + JobAd-katalog UI.

---

## Tidigare sessioner (kort)

- **2026-05-13 förmiddag** (denna): TD-73 prod-gating-batch — audit-wire α (ADR 0035) + right-to-erasure (ADR 0032 §8 amendment). 1 commit `c13e1ce`, tag `v0.2.4-dev` deploy success. 33 nya tester. TD-73 stängd; TD-75 + TD-76 lyfta.
- **2026-05-13 morgon:** F2-P8c JobTech Hangfire-jobben + race-säker upsert + 30d-retention. 1 commit `81dfab6`, tag `v0.2.3-dev`. 43 nya tester.
- **2026-05-13 natt:** F2-P8b JobTech Infrastructure-leverans. 5 commits, tag `v0.2.2.1-dev`.
- **2026-05-12 kväll:** F2-P7 + P8a + bootstrap + aggregate-review. 17 commits, 3 nya ADRs.

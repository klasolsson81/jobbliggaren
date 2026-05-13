# ADR 0036 — Prod-stack deferred + cloudwatch_ops_alarms-modul (v0.2-prod-launch-checklist-leverans)

**Datum:** 2026-05-13
**Status:** Accepted 2026-05-13 (senior-cto-advisor decision per CLAUDE.md §9.6 punkt 5 + Klas-GO)
**Kontext:** v0.2-prod-tag-readiness-rond — discovery 2026-05-13 ~09:00 UTC avslöjade att `infra/terraform/environments/prod/` är baseline-only och `.github/workflows/deploy-prod.yml` saknas.
**Beslutsfattare:** senior-cto-advisor 2026-05-13 (3 ronder) + Klas Olsson (godkänd 2026-05-13)
**Relaterad:** ADR 0031 (FailedAccessLogger `event_name=`-konvention + log_pipeline_health-pattern), ADR 0035 (system-event audit-pipeline — §6 best-effort + alarm-wire-spec), ADR 0024 D7 (CloudWatch retention 30d), ADR 0027 (HTTPS-aktivering), BUILD.md §14.4 (alerting), §15.1 (prod-infra-spec), §15.3 (deploy-prod.yml-spec), §18 (fas-progression)

## Kontext

`docs/runbooks/v0.2-prod-launch-checklist.md` skapades 2026-05-13 (commit `2d1f73a`) baserat på en senior-cto-advisor-rond samma dag. CTO Q1c-tolkning: "v0.2 = första prod-deploy-triggande tag" via Continuous Delivery (Humble/Farley 2010). Tre in-block-fix-leveranser specades FÖRE v0.2-tag-push:

1. CloudWatch-alarm: JobTech-sync 3 consecutive failures
2. CloudWatch-alarm: SystemEventAuditor failure (EventId 5602)
3. RDS backup-retention 7d → 14d

Klas gav GO för Alternativ A (full batch). Implementations-discovery upptäckte två blockers:

- **`infra/terraform/environments/prod/` är baseline-only.** Innehåller budgets, cloudtrail, KMS, secrets_manager, bedrock_model_access, route53, github_oidc. Saknas: RDS, ECS, ALB, ACM, cloudwatch_logs, cloudwatch_security_alarms, secrets för DB, redis, iam_ecs.
- **`.github/workflows/deploy-prod.yml` finns inte.** Bara `deploy-dev.yml`. BUILD.md §15.3 specar `deploy-prod.yml` men workflow är inte skriven.

Det betyder att CTO Q1c-tolkningen **inte var mekaniskt möjlig** — prod-pipelinen existerar inte. CTO invokerades igen, korrigerade explicit förra rondens antagande, och rekommenderade **A3**: skapa återanvändbar modul, applicera till dev, defer prod-stack-bygge till Fas 7-förberedelse.

Klas gav GO för A3. dotnet-architect verifierade Terraform-design och flaggade 4 Major + 2 Viktiga fynd, inklusive ett genuint multi-approach-val (Q1: Worker-kod-ändring för `event_name=`-pattern eller substring-fallback).

CTO invokerades en tredje gång och beslutade samtliga design-frågor entydigt mot principer. Klas-STOPP triggades inte.

## Beslut

Sex delbeslut. D1–D2 är strategiska (defer + cohesion-bundle). D3–D6 är design-justeringar från architect-rond.

### Delbeslut 1 — Prod-stack-bygge defereras till Fas 7-förberedelse

`infra/terraform/environments/prod/` förblir baseline-only fram till explicit Fas 7-förberedelse-session med egen ADR. Skäl:

- **YAGNI (Hunt/Thomas 1999):** Prod-stack (~$150-180/mån för Multi-AZ RDS + ALB + Multi-AZ Redis) för 0 användare bryter JobbPilots Budget Actions $50/mån-policy (F2-P3 / ADR 0005-amendments).
- **Continuous Delivery missapplicerat (Humble/Farley 2010, kap. 1):** CD bygger pipeline när release-behovet är reellt, inte preemptivt. Min förra rond åberopade CD för att försvara Q1c — det argumentet förutsatte att pipelinen existerade.
- **Last Responsible Moment (Poppendieck 2003):** Multi-AZ-strategi, Redis-topologi, autoscaling-thresholds blir bättre när dev har ≥1 verklig användare först.
- **Spec-konformitet:** BUILD.md §18 säger Fas 0 ger "första deploy till dev" — levererat. Prod-stack är inte spec:ad som obligatorisk innan Fas 8.

**`v0.2`-tag används inte denna fas.** Tag-progression fortsätter med `v0.2.x-dev`-tags tills prod-stack-sessionen levereras i framtida ADR. BUILD.md §15.3 ändras **inte** — A2-alternativet (omdefiniera tag-semantik) bröt CLAUDE.md §9.2-förbudet utan motiverat värde.

**Trigger för prod-stack-bygge:**

- Fas 7 internal beta (2-3 testare) närmar sig
- Eller: dev får ≥1 extern användare och vi behöver isolation
- Eller: TD-72-trigger upprepas (Identity-schema-change kräver bootstrap-mode i prod) → forcerar prod-stack-leverans

### Delbeslut 2 — Cohesion-bundle: Worker LoggerMessage + Terraform-modul samma commit

`SyncPlatsbankenStreamJob.LogEventFailed` (EventId 5303) + `SystemEventAuditor.LogAuditFailure` (EventId 5602) LoggerMessage-templates uppdateras till `event_name=<konstant>`-konvention från ADR 0031 (FailedAccessLogger). CloudWatch metric filters i ny `modules/cloudwatch_ops_alarms` matchar dessa patterns. Producer + consumer levereras i samma commit.

**Motivering (CCP — Martin 2017 kap. 13):** Klassesm som ändras tillsammans tillhör tillsammans. Log-format-konvention (producer) och CloudWatch-pattern (consumer) ändras vid samma trigger (observability-strategi-skifte). Splittad leverans skapar broken state mellan commits.

**Anti-pattern avvisat (Alt A — substring-pattern):** `event-failure` substring bryter ADR 0031-precedens, false-positive-risk (Nygard 2018 *Release It!* kap. 4: false-positives bränner operator-trust), OCP-fientlig (nästa ops-alarm måste uppfinna ny unik substring).

**Anti-pattern avvisat (Alt C — Alt A nu + TD för retro-fit):** TD som "spara så scope inte växer" är §9.6-anti-pattern. Worker-log-format-ändring är inom samma bounded context som ops-alarms-leveransen — defer skapar parallell pattern-population som divergerar över tid.

### Delbeslut 3 — `event_name=`-konvention för LoggerMessage-templates

LoggerMessage-templates uppdateras (signatur oförändrad):

```csharp
// SyncPlatsbankenStreamJob.cs:175-176 (EventId 5303)
[LoggerMessage(EventId = 5303, Level = LogLevel.Warning,
    Message = "event_name=job_event_failure job_name=SyncPlatsbankenStreamJob external_id={ExternalId} — räknas i ErrorCount, fortsätter med nästa.")]

// SystemEventAuditor.cs:83-84 (EventId 5602)
[LoggerMessage(EventId = 5602, Level = LogLevel.Critical,
    Message = "event_name=audit_write_failure event_type={EventType} aggregate_id={AggregateId} — GDPR Art. 30 record-of-processing kan vara påverkat.")]
```

**Motivering:** Ubiquitous language (Evans 2003 kap. 2). Operatör som lärt sig läsa CloudWatch för secops-alarms (`event_name=failed_access_attempt` per ADR 0031) applicerar samma mönster på ops-alarms (`event_name=job_event_failure`, `event_name=audit_write_failure`). Konsistens i log-format = konsistens i CloudWatch metric-filter-pattern.

### Delbeslut 4 — Aggregate-threshold-mappning (period=1800 × eval=1)

CloudWatch alarms använder aggregate-thresholds över single-period-fönster, inte consecutive-evaluation_periods-multiplikation:

- **JobTech-sync-failures:** `period=1800` (30 min), `evaluation_periods=1`, `threshold=3` (Sum, >=). Matchar BUILD.md §14.4 "3 gånger i rad" semantiskt aggregerat: tre eller fler failures inom valfritt 30-min-fönster → alarm.
- **SystemEventAuditor-failures:** `period=300` (5 min), `evaluation_periods=1`, `threshold=0` (Sum, >). Zero-tolerance: varje audit-failure är signal.

**Motivering (AWS Well-Architected REL06-BP02):** Aggregate-thresholds över single-period ger tydligare signal än multi-period-evaluation. Operativ enkelhet (Nygard 2018 kap. 17 "Transparency"): alarm-meaning ska kunna förklaras av operatör som blev väckt 03:00.

### Delbeslut 5 — Worker-log-pipeline-health-alarm för cohesion-paritet

Tredje alarm `${name_prefix}-worker-log-pipeline-health` läggs till i `cloudwatch_ops_alarms`-modulen som speglar `cloudwatch_security_alarms.log_pipeline_health`-pattern men för worker-log-grupp.

**Motivering (LSP — Martin 2017 kap. 9):** Ops-alarms och secops-alarms är subtypes av "log-pipeline-driven CloudWatch alarms". Båda måste uppfylla samma kontrakt: "alarm fires when failures occur AND we know log pipeline is healthy". `treat_missing_data=notBreaching` på failure-alarms blir bevisbart icke-funktionell utan parallell health-check (security-auditor Minor-3 2026-05-12 etablerade denna invariant).

Konfiguration: `metric=AWS/Logs/IncomingLogEvents` på worker-log-gruppen, `threshold <= 0` över 15 min, `treat_missing_data=breaching` (frånvaro av events = bruten pipeline, inte normal drift).

### Delbeslut 6 — ISP-justering: separat `var.ops_alert_email` + topic-suffix `-ops-anomaly`

`cloudwatch_ops_alarms`-modulen:

- Egen variabel `var.ops_alert_email` (separat från `var.secops_alert_email`). Kan vara samma adress i dev-tfvars utan att låsa designen.
- SNS-topic: `${name_prefix}-ops-anomaly` (suffix-parallellism med `${name_prefix}-secops-anomaly`).

**Motivering (ISP — Martin 2017 kap. 10):** Ops-on-call och security-on-call är distinkta triage-flöden. Att aliasa variabel-yta tvingar samma mottagar-mängd → samma noise-problem som delad topic hade gett, bara försenat ett steg. Separat variabel idag = noll friktion för framtida channel-divergens (PagerDuty-routing per kategori, dedikerad ops-on-call, etc.).

**Topic-suffix:** Ubiquitous language (Evans 2003). `<prefix>-<category>-anomaly` är etablerat mönster från ADR 0031. Avvikande suffix (`-ops-alarms`) tvingar mental-context-switch.

## Komponenter (denna leverans)

### Worker-kod (cohesion-bundle commit 1)

- `src/JobbPilot.Application/JobAds/Jobs/SyncPlatsbanken/SyncPlatsbankenStreamJob.cs` — `LogEventFailed` template uppdaterad
- `src/JobbPilot.Infrastructure/Auditing/SystemEventAuditor.cs` — `LogAuditFailure` template uppdaterad

### Terraform-modul (cohesion-bundle commit 1)

```
infra/terraform/modules/cloudwatch_ops_alarms/
  versions.tf       # hashicorp/aws ~> 5.80 (matchar existerande moduler)
  variables.tf      # 8 variabler (name_prefix, log-groups, kms, alert_email, thresholds, tags)
  main.tf           # SNS-topic + topic-policy + 3 metric filters + 3 alarms
  outputs.tf        # 5 outputs (sns-topic, 3 alarm-ARNs, metric-filter-names)
```

### Dev-env-konsumption (cohesion-bundle commit 1)

- `infra/terraform/environments/dev/main.tf` — ny `module "cloudwatch_ops_alarms"`-block
- `infra/terraform/environments/dev/variables.tf` — ny `variable "ops_alert_email"`

### Backup-retention-bump (separat commit 2 per delbeslut 4 från CTO-rond 3)

- `infra/terraform/environments/dev/main.tf` — `module "rds" { backup_retention_days = 14 }` (var 7)

**Motivering split:** SRP på commit-nivå (Martin 2017 kap. 7). Create-only modul-introduktion har annan risk-profil än live-RDS-state-modifikation. Rollback-isolation (Humble/Farley 2010 kap. 8).

## Konsekvenser

### Positiva

- **v0.2-prod-launch-checklist §9.1 + §9.2 levereras på dev** (= effektiv prod just nu). GDPR Art. 30 alarm-wire för audit-failure stänger ADR 0035 §6-gap. BUILD.md §14.4 JobTech-sync-failure-alarm satisfied.
- **Cohesion-paritet med ADR 0031.** Operatörer som lärt sig läsa secops-alarms kan applicera samma mönster på ops-alarms utan kontext-byte.
- **Återanvändbar modul.** När prod-stack-sessionen levereras (Fas 7-förberedelse) konsumeras `cloudwatch_ops_alarms` från `prod/main.tf` med prod-specifika thresholds utan kod-duplikat.
- **Inga TDs lyfta.** Samtliga design-frågor löstes inom batchen per CLAUDE.md §9.6 fas-regel.
- **`v0.2`-tag-semantik bevarad.** BUILD.md §15.3 ändras inte. Tag-progression fortsätter `v0.2.x-dev` tills prod-stack-leverans.

### Negativa

- **CTO-rond 1 (förmiddag 2026-05-13) hade ofullständig discovery.** Q1c-rekommendation föll. Korrigerad i rond 2. Lärdomen registrerad i docstring-not: framtida CTO-ronder på infra-strategiska frågor ska inkludera on-disk-state-verifiering innan rond avges.
- **Scope-expansion ~30-60 min** för Worker LoggerMessage-ändringar (Q1 Alt B vs Alt A). Acceptabelt — cohesion-vinst > tidskostnad.
- **Worker-log-pipeline-health-alarm är duplikat-pattern** till security-modulens motsvarighet (api-log-grupp). LSP-paritet motiverar duplikatet; en framtida refactor kan extrahera health-alarm till egen modul om båda envs får 3+ log-groups var.

### Mitigering

- Modul-docstring i `main.tf` dokumenterar Fas-3+-trigger för upgrade till canary/synthetic-tests (volume > 10k events/dag, ECS-task-restart > 1/vecka)
- Architecture-test-skydd är ej nödvändigt i Fas 2 (dokumentation + `terraform plan`-diff räcker som regression-skydd; CC-rond + Klas-diff-granskning fångar misalignment)

## Alternativ övervägda

### Alt A1 — Bygg hel prod-stack först

Avvisat. YAGNI-brott (~$150-180/mån för 0 användare), scope-explosion mid-batch (2-3 sessioner CC-tid), bryter Last Responsible Moment. Rätt arbete, fel tid — defereras till Fas 7-förberedelse med egen ADR.

### Alt A2 — `v0.2` = dev-deploy med "prod-säkrare config" + BUILD.md-edit

Avvisat. Tre problem:

1. Spec-omformulering för att passa nuläget (Fowler 2018 *Refactoring* kap. 3 — "Bad Smells" inverterat).
2. Tag-semantik blir tvetydig: "v0.2" på prod-flagga som mekaniskt deployar till dev = lögn-i-namn. Bryter JobbPilots civic-utility-värdering (CLAUDE.md §1).
3. CLAUDE.md §9.2: BUILD.md-edits utan Klas-instruktion förbjudet.

### Alt Q1-A — Substring-pattern `event-failure`

Avvisat per delbeslut 2. Bryter ADR 0031-precedens, false-positive-risk, OCP-fientlig.

### Alt Q1-C — Substring nu + TD för retro-fit

Avvisat. §9.6-anti-pattern: TD som "spara så scope inte växer" när arbetet hör till samma bounded context.

### Alt Q3 — Skippa worker-log-pipeline-health-alarm

Avvisat. LSP-brott + känd defekt-pattern från security-auditor Minor-3 2026-05-12.

### Alt Q5 — Återanvänd `secops_alert_email`

Avvisat. ISP-brott på notification-channel-nivå. Retroaktiv refactor när divergens kommer = sämre option än separat variabel idag.

## Implementationsstatus

| Komponent | Status |
|---|---|
| `SyncPlatsbankenStreamJob.LogEventFailed` template-uppdatering | Levererad denna batch |
| `SystemEventAuditor.LogAuditFailure` template-uppdatering | Levererad denna batch |
| `modules/cloudwatch_ops_alarms/` (versions+vars+main+outputs) | Levererad denna batch |
| `dev/main.tf` modul-konsumption | Levererad denna batch |
| `dev/variables.tf` `ops_alert_email` | Levererad denna batch |
| `dev/main.tf` `backup_retention_days = 14` | Levererad denna batch (separat commit) |
| Terraform-apply mot dev | **Pending Klas-GO** efter security-auditor + code-reviewer |
| Prod-stack-bygge | **Deferred** till Fas 7-förberedelse (egen ADR) |
| `deploy-prod.yml` workflow | **Deferred** till samma session som prod-stack |

## Validation

- `terraform validate` mot ny modul + dev-stack
- `terraform plan` granskas av Klas innan apply
- Post-apply: CloudWatch console verifierar alarm-state = `OK` (notBreaching) initialt
- Smoke-test JobTech-failure-alarm: simulera via manuell JobTech-event-failure (testbar via WireMock-stub mot resilience-test-suite, men out-of-scope för denna leverans)
- Smoke-test SystemEventAuditor-alarm: trigger via DB-disconnect under audit-write (out-of-scope; verifierat via existerande unit-tester att Critical-log emit:as vid failure)

## Referenser

- Robert C. Martin, *Clean Architecture* (2017), kap. 7 (SRP), kap. 8 (OCP), kap. 9 (LSP), kap. 10 (ISP), kap. 13 (CCP)
- Eric Evans, *Domain-Driven Design* (2003), kap. 2 (Ubiquitous Language)
- Michael Nygard, *Release It!* 2nd ed. (2018), kap. 4 (Stability Antipatterns), kap. 17 (Transparency)
- Jez Humble & David Farley, *Continuous Delivery* (2010), kap. 1 + kap. 8
- Andrew Hunt & David Thomas, *The Pragmatic Programmer* (1999) — YAGNI
- Mary Poppendieck, *Lean Software Development* (2003) — Last Responsible Moment
- Saltzer & Schroeder, *The Protection of Information in Computer Systems* (CACM 1975) — fail-safe defaults
- AWS Well-Architected Framework — Reliability Pillar REL06-BP02
- BUILD.md §14.4 (Alerting), §15.1 (prod-spec), §15.3 (deploy-prod.yml), §18 (fas-progression)
- ADR 0031 (FailedAccessLogger `event_name=`-konvention + log_pipeline_health-pattern)
- ADR 0035 §6 (system-event audit best-effort + alarm-wire-spec)
- ADR 0024 D7 (CloudWatch retention 30d)
- CLAUDE.md §2.1, §9.2, §9.6, §9.7
- senior-cto-advisor 2026-05-13 (3 ronder — Q1c-korrigering + A3-rekommendation + design-frågor)
- dotnet-architect 2026-05-13 (Terraform-design-validation, 4 Major + 2 Viktiga fynd)

## Status

**Accepted** 2026-05-13. Omvärderas vid:

- **Fas 7-förberedelse** — prod-stack-bygge-session med egen ADR (`environments/prod/` utbyggnad + `deploy-prod.yml` workflow + cloudwatch_ops_alarms-modul-konsumption i prod-env)
- **Fas 8 Klass-launch** — när TD-77 (5xx-rate-alarm) + TD-78 (DB CPU > 80%) blir relevanta vid multi-user-volym
- **Vid första prod-deploy** — modul re-tunas (thresholds + period) baserat på faktiska prod-trafik-mönster

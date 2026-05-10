# Security Audit — STEG 14b Phase 1 (JobbPilot.Migrate + IAM + ECS task-def)

**Granskat:** 2026-05-10
**Auditerare:** security-auditor
**Auktoritet:** GDPR Art. 5/32, CLAUDE.md §5.4 + §9.2, ADR 0019/0023/0025
**Status:** **APPROVE-WITH-FIXES**

Inga Sec-Critical-fynd. 2 Sec-Major bör adresseras innan första `terraform apply` + `aws ecs run-task`. 5 Sec-Minor är hardening-rekommendationer som kan tas senare. Inga GDPR-blockers — Migrate hanterar inte PII.

---

## Sec-Major (bör fixas innan första run-task)

### Sec-Major-1 — Master-secret rotation race vid mid-flow re-run

**Fil:** `src/JobbPilot.Migrate/Program.cs:67-74`

RDS-master-secret är AWS-managerad med auto-rotation. Migrate cachar pwd-1 i process-minne under hela Phase A → C. Om rotation triggas mid-flow får Phase C `28P01 password authentication failed`.

**Mitigation:** Re-fetch master-creds vid Phase C (~3 rader).

### Sec-Major-2 — Task-def saknar default network-config

**Fil:** `infra/terraform/modules/ecs/main.tf:117-166`

Operatör som kör `aws ecs run-task` utan `--network-configuration` riskerar fel SG-yta. Risk för bredare yta än avsett.

**Mitigation:** Terraform output `migrate_run_task_command` som producerar färdig CLI-string med subnets/SG inbakade.

---

## Sec-Minor (defense-in-depth)

### Sec-Minor-1 — Modulo-bias i `GenerateRandomPassword`
~189 bitar effektiv entropy istället för ~190.5. Försumbar i praktiken. **Defer som accepterad risk.**

### Sec-Minor-2 — Fingerprint exponerar 25% av pwd-tecknen
**Mitigation:** SHA256-trunc till 8 hex-chars. 0% pwd-bytes synliga, samma identifying-info.

### Sec-Minor-3 — DDL-SQL identifier-interpolation utan escape
Inte exploitable idag (env-var Terraform-konstant + hardcoded roller + charset utan `'`). **Defensiv hardening:** `format('%I', %L)` med Npgsql-parameters.

### Sec-Minor-4 — `Trust Server Certificate=true` persisteras till app/worker-CS
OK för Migrate själv (Fas 0/dev), men persisteras till Api+Worker-CS för all framtid → MITM-risk om VPC-perimeter brister.
**Mitigation:** TD-38 — RDS-CA-bundle in i container-truststore + flippa till `Trust Server Certificate=false` innan Fas 1.

### Sec-Minor-5 — Migrate-rollen har `kms:GenerateDataKey` (överrättighet)
För `PutSecretValue` på existerande secret räcker `kms:Decrypt`. ViaService-condition begränsar redan blast-radius men least-privilege säger ta bort.
**Mitigation:** Ta bort `kms:GenerateDataKey` från `task_migrate`-policy.

---

## Praise

- Separat blast-radius: `task_migrate` har `PutSecretValue` ENDAST på 2 ARN:er
- REVOKE PUBLIC defense-in-depth innan grants
- Worker har DML-only på hangfire.* — INTE CREATE/DROP
- `ALTER DEFAULT PRIVILEGES FOR ROLE jobbpilot_migrations` — future-proof
- LoggerMessage source-gen + pre-computed fingerprints (CA1848 + CA1873)
- Non-root container `USER app`
- count-pattern IaC — backwards-compat
- Egen log-group `/aws/ecs/jobbpilot-dev/migrate`, KMS-encrypted, 30d retention

---

## Sammanfattning

**APPROVE-WITH-FIXES.** Fixa Sec-Major-1 + Sec-Major-2 innan första `aws ecs run-task`. Sec-Minor-2 + Sec-Minor-5 är trivial-fix att inkludera. Sec-Minor-1 + Sec-Minor-3 + Sec-Minor-4 kan defereras (TD).

Inga GDPR-blockers.

**Re-review krävs efter Sec-Major-1 + Sec-Major-2 är applied.**

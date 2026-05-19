# TD-13 STOPP V — KMS-infra-design (dotnet-architect)

**Datum:** 2026-05-19
**Agent:** dotnet-architect (agentId a7e11bba875d58261) — read-only design; §9.2 IaC-obligatorisk, ADR 0036 CTO+architect-tandem-precedens
**Trigger:** STOPP V `v0.2.17-dev` deploy FAILED — dev-ECS (`ASPNETCORE_ENVIRONMENT=Production`) saknade `FieldEncryption:CmkKeyId`; C1-hotfix-`FieldEncryptionOptionsValidator` hård-failade boot (fail-closed KORREKT). Första deploy med TD-13 KMS-envelope (C1–C6); ingen CMK/IAM/secret provisionerad.

## Verdikt: ren IaC-impl av ADR 0049 Beslut 1 — ingen ADR-amendment

Klas kan override:a dedikerad-CMK-valet till formell amendment om CMK-granularitet bedöms besluts-substans (flaggad; default = mekanik-not-paritet §9.6 p.5).

## Beslut per fråga

**Q1 — Dedikerad TD-13-CMK** (ej återanvänd master/byok). Least-privilege/SRP/crypto-erasure-isolering (ADR 0049 Beslut 1/2; OWASP key-hierarchy; AWS KMS best practice). +~1 USD/mån. Precedens: `aws_kms_key.byok` redan dedikerad envelope-nyckel. `modules/kms`: `aws_kms_key.td13_field` + `alias/jobbpilot-td13-field-key` + outputs; befintlig `key_policy` (root-enabled) återanvänd.

**Q2 — Plain `environment`, ej Secrets Manager.** CMK-ARN ≠ secret (ger ingen access utan IAM-grant). API + Worker task-defs får `FieldEncryption__CmkKeyId`/`__AwsRegion`. **Migrate utelämnas medvetet** — verifierat: `Migrate/Program.cs` bygger `AppDbContext` via `MigrationsOptionsFactory` UTAN `AddPersistence`/`ValidateOnStart`/interceptor/KMS; Phase-E = ren DDL. (Den tidigare Migrate↔CMK-ordnings-eskaleringsfrågan avförd.)

**Q3 — Minsta IAM:** `kms:GenerateDataKey` + `kms:Decrypt` enbart (verifierat — `KmsDataKeyProvider` anropar ej Encrypt/DescribeKey/ReEncrypt; AES-GCM lokalt med plaintext-DEK, KMS rör aldrig fältdata). Resource-scoped till td13-CMK-ARN. EncryptionContext StringEquals-villkor `purpose=td13-field` + `aggregate=jobseeker` (defense-in-depth, owner-AAD-paritet C3). Separat statement (ej ViaService-Decrypt-utökning). task_api + task_worker; ej execution, ej task_migrate.

**Q4 — `eu-north-1`, ingen cross-region** (GDPR-residens, EU-guard passerar).

**Q5 — Apply-ordning:** prod/baseline (kms-modulen → CMK+alias) → dev (data-source + iam_ecs + ecs-env, ny task-def-rev api+worker) → deploy. Ingen Migrate↔CMK-koppling. Idempotent.

**Q6 — prod-paritet flaggad, scope:as ej in nu** (TD-13 STOPP V = dev-deploy; CMK skapas av delad baseline; prod-stackens delta = data-source + iam_ecs + ecs-env i `prod/main.tf` vid framtida prod-deploy).

## Implementation-utfall (CC, 2026-05-19, Klas-GO "full kedja autonomt")

- 6 Terraform-filer skrivna verbatim per skiss; pushad `fca3605`.
- **prod/baseline targeted-apply** (`-target=module.kms.aws_kms_key.td13_field` + alias) — exkluderade incidentell **github_oidc prod-drift** (→ **TD-85**). CMK verifierad: ARN `arn:aws:kms:eu-north-1:710427215829:key/26cc0074-a942-4fc9-b2eb-24f3b751c526`, `Enabled`, `SYMMETRIC_DEFAULT`.
- **dev targeted-apply** (`-target` ×4: task_api/task_worker IAM + api/worker task-def). RDS-param-group-drift (`rds.force_ssl` `apply_method: pending-reboot→immediate`, **värde oförändrat = 1**, SSL-enforcement bevarad — benign config↔state-normalisering, TD-85-syskon-klass) inkluderades medvetet (−target exkluderade ej; noll säkerhets/funktionell delta). `Apply complete! 2 added, 3 changed, 2 destroyed`. API task-def rev 28 = `FieldEncryption__CmkKeyId`/`__AwsRegion` verifierat.
- Autonom `terraform apply` classifier-blockerad (ask-grind + ingen interaktiv stdin) → Klas körde dev-apply + tag-push interaktivt; CC tar read-only deploy-watch + rök-verify.

## Klas-flaggor (STOPP V)
- Dedikerad-CMK-val: override→formell ADR 0049-amendment möjlig (default mekanik-not).
- TD-85: github_oidc prod-drift + RDS-param-group dev-normalisering — separat architect/CTO-IaC-triage (egen session; ej buntad TD-13).
- prod-paritet KMS-IaC: krävs vid framtida prod-deploy (flaggad, ej nu).

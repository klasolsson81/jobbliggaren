# dotnet-architect — Plan B implementations-design

**Datum:** 2026-05-24
**Agent:** dotnet-architect (agentId `a1513a571782c2dc0`)
**Triggrad av:** CTO-rond `a9f2e123b1080b00f` valde Plan B. Klas-GO. CC discovery visade existing Terraform-state har 4 av 5 CTO-steg redan implementerade.

---

## Kritiskt discovery-fynd

`infra/terraform/environments/dev/main.tf` har redan:
- `aws_secretsmanager_secret.db_hangfire_connection` (rad 112)
- `worker_secrets` mountar den via `ConnectionStrings__HangfireStorage` (rad 336)
- IAM `secret_arns` (rad 241-247) inkluderar redan `db_hangfire_connection.arn` för BÅDA `task_api` och `task_worker`

`docs/runbooks/hangfire-schema.md` §4: `jobbpilot_worker`-rollen är **funktionellt hangfire-only** (REVOKE PUBLIC + endast hangfire-schema-grants, ingen `jobbpilot_app`-inheritance).

**Slutsats:** CTO original "Plan B = ny dedikerad role" är **redan uppfylld** av nuvarande implementation. Namnet `jobbpilot_worker` är legacy-bagage.

## Beslut

**Alternativ A — share existing `jobbpilot_worker`-roll med Api-task. Behåll legacy-namnet. Rename-debt → TD-99 för STEG 14.**

Avvisar:
- Alt B (ny parallel role) — onödig dubbel-state + dubbel password-rotation
- Alt C (rename in-place) — ARN-byte = onödig redeploy + state-surgery

## Motivering mot principer

- **YAGNI / KISS (Martin Clean Code kap. 17 + Beck XP):** Existing infra uppfyller intent. Skapa inte ny resurs för namn-dissonans.
- **Reversibility (Fowler Continuous Delivery):** A är trivialt att rulla tillbaka (en rad). C/B kräver state-surgery.
- **Blast-radius (Beyer SRE kap. 5):** A → en task-def-revision för Api. B/C → full redeploy av båda services + secret-cutover.
- **CLAUDE.md §9.6 (in-scope vs TD):** Rename hör till STEG 14 prod-DDL-cutover (annan fas) → TD-99 legitim.

## Säkerhets-analys

`jobbpilot_worker`-rollen är hangfire-only verifierat:

```sql
-- Från runbook §4:
REVOKE ALL ON SCHEMA hangfire FROM PUBLIC;          -- ingen PUBLIC-inheritance
REVOKE ALL ON DATABASE jobbpilot FROM PUBLIC;
GRANT CONNECT ON DATABASE jobbpilot TO jobbpilot_worker;
GRANT USAGE ON SCHEMA hangfire TO jobbpilot_worker;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA hangfire TO jobbpilot_worker;
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA hangfire TO jobbpilot_worker;
```

Ingen `GRANT jobbpilot_app TO jobbpilot_worker` (rolinheritance). Ingen PUBLIC-fallback. Saltzer & Schroeder least-privilege bevarad: Api får tillgång till hangfire-schemat OCH INGENTING ANNAT via denna roll.

## Konkret Terraform-modifikation

**EN fil ändras:** `infra/terraform/environments/dev/main.tf` rad 323-326:

```hcl
# Före:
api_secrets = {
  "ConnectionStrings__Postgres" = aws_secretsmanager_secret.db_app_connection.arn
  "ConnectionStrings__Redis"    = module.redis.connection_string_secret_arn
}

# Efter:
api_secrets = {
  "ConnectionStrings__Postgres"        = aws_secretsmanager_secret.db_app_connection.arn
  # STEG 6 Plan B (2026-05-24) — Api behöver hangfire-storage-CS för
  # IBackgroundJobClient.Enqueue. Rollen jobbpilot_worker är hangfire-only
  # (PUBLIC revoke:ad, ingen jobbpilot_app-inheritance per runbook §4).
  # Se TD-99 för legacy-namn-cleanup planerat i STEG 14.
  # CTO-rond a9f2e123b1080b00f + architect-rond a1513a571782c2dc0.
  "ConnectionStrings__HangfireStorage" = aws_secretsmanager_secret.db_hangfire_connection.arn
  "ConnectionStrings__Redis"           = module.redis.connection_string_secret_arn
}
```

**INGET annat ändras:**
- `modules/iam_ecs/main.tf` orört (IAM-policy täcker redan secreten via `var.secret_arns`)
- `modules/ecs/*` orört (mountning sker via standard `secrets`-block-iteration)

## In-block-fixar

1. main.tf-edit (ovan)
2. TD-99 i `docs/tech-debt.md`: "Rename jobbpilot_worker → jobbpilot_hangfire (Postgres-roll + secret-namn)" — Minor, STEG 14
3. Runbook §1 + §4: lägg not "Från STEG 6 Plan B konsumeras HangfireStorage-CS av både Worker och Api"

## Backward-compat

Purely additive. Worker oförändrad. Api får ny env-var som Hangfire-client-bootstrapping läser via fallback `HangfireStorage → Postgres` (Program.cs rad 79-82). Existing tests/integration berörs ej.

Vid `terraform apply`: en `aws_ecs_task_definition.api`-revision + `aws_ecs_service.api` rolling deploy. Förväntad och accepterad.

## Implementationssekvens för CC

1. main.tf edit (rad 323-326) + inline-kommentar
2. TD-99 i tech-debt.md
3. Runbook §1/§4 not
4. `terraform plan` i `environments/dev/` — förvänta endast Api task-def + service rolling deploy
5. Om plan visar oväntade resurser i diff → STOPP till Klas
6. Klas-GO för `terraform apply`
7. Verifiera via `aws ecs describe-task-definition` att secrets-array innehåller `ConnectionStrings__HangfireStorage`

## Referenser

- CLAUDE.md §3.2, §5.1, §9.6, §9.7
- Robert Martin, *Clean Code* kap. 17 — YAGNI
- Fowler, *Continuous Delivery* — reversibilitet
- Beyer et al., *SRE Book* kap. 5 — blast-radius
- ADR 0019 (direct-push), ADR 0023 + 0024 (Worker/Hangfire-design)
- `docs/runbooks/hangfire-schema.md` §4 (GRANT-modell)
- CTO-rond `a9f2e123b1080b00f`

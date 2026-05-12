# ADR 0034 — DB-role privilege-separation: runtime vs migration-time creds

**Datum:** 2026-05-12
**Status:** Accepted 2026-05-12 (Klas-GO "GO på A1" 2026-05-12)
**Kontext:** F2-P8a.5d Phase E privilege-anomali — `jobbpilot_app` saknar `CREATE ON DATABASE` → AppIdentityDbContext.MigrateAsync failar med "permission denied" (Npgsql #1770/#1551 + PostgreSQL CREATE SCHEMA-policy)
**Beslutsfattare:** senior-cto-advisor 2026-05-12 (rond 4) + Klas Olsson (godkänd 2026-05-12)
**Relaterad:** ADR 0009 (no Repository), ADR 0013 (separate AppIdentityDbContext), ADR 0023 (Hangfire-infrastruktur), ADR 0033 (Migrate CLI-mode-dispatch + amendment Variant A)

## Kontext

F2-P8a.5d auto-applicerade 10 EF-migrations mot AppDbContext (public-schema, via jobbpilot_app) men Api-deploy failade på `relation "identity.AspNetRoles" does not exist` när `IdempotentAdminRoleSeeder.StartAsync` försökte query Identity-tabellen.

**Anomalin (Hypotes A bekräftad av CTO-rond 3):** Identity-schemat har aldrig applicerats på denna dev-RDS-instans. Tidigare deploy-success var false-positive eftersom ALB target-group health-check (`/api/ready`) inte slog mot Identity-tabellerna.

**CTO-rond 4 web-search-verifierat fynd ([Npgsql #1770](https://github.com/npgsql/efcore.pg/issues/1770), [#1551](https://github.com/npgsql/efcore.pg/issues/1551), [PostgreSQL CREATE SCHEMA docs](https://www.postgresql.org/docs/current/sql-createschema.html)):**

> "To create a schema, the invoking user must have the CREATE privilege for the current database. IF NOT EXISTS bypassar inte privilege-check."

EF Core MigrateAsync mot AppIdentityDbContext (med `HasDefaultSchema("identity")`) försöker köra `CREATE SCHEMA IF NOT EXISTS identity` **oavsett** om schemat redan finns. PostgreSQL evaluerar privilege-check INNAN IF-evaluering → failar om rollen saknar `CREATE ON DATABASE`.

`jobbpilot_app` (Phase A rad 271-307) har: `CONNECT` + `USAGE/CREATE ON SCHEMA public` + `GRANT ALL ON public-tabeller`. **Saknar:** `CREATE ON DATABASE jobbpilot`.

## Beslut

### Princip

**Runtime-app-credentials (`jobbpilot_app`) får aldrig `CREATE ON DATABASE`.**

Schema-DDL körs ALDRIG av app-runtime-credentials i JobbPilot. Detta är arkitekturregel, inte engångs-fix. Schema-mutationer är **release-stage-arbete** (12-Factor §V) och kräver privileged credentials separata från runtime.

### Mekanism

`JobbPilot.Migrate` får en ny `bootstrap`-mode (totalt 3 CLI-modes per ADR 0033 + denna):

| Mode | Creds | Scope | Trigger |
|---|---|---|---|
| `init` | master | Phase A-D (Hangfire-schema + 3 roller + creds-rotation + identity-schema-skapande) | Engångs-setup eller creds-rotation (~1×/år) |
| `bootstrap` | master | identity-schema + GRANTs + AppIdentityDbContext.MigrateAsync | Engångs eller vid Identity-schema-ändring (sällsynt) |
| `schema` | jobbpilot_app | AppDbContext.MigrateAsync mot public | Varje feature-batch med ny EF-migration |

### Bootstrap-mode-detaljer

1. **Step 1 (SQL via master-creds):**
   ```sql
   CREATE SCHEMA IF NOT EXISTS identity AUTHORIZATION jobbpilot_migrations;
   REVOKE ALL ON SCHEMA identity FROM PUBLIC;
   GRANT USAGE, CREATE ON SCHEMA identity TO jobbpilot_app;
   GRANT ALL ON ALL TABLES IN SCHEMA identity TO jobbpilot_app;
   GRANT ALL ON ALL SEQUENCES IN SCHEMA identity TO jobbpilot_app;
   ALTER DEFAULT PRIVILEGES IN SCHEMA identity GRANT ALL ON TABLES TO jobbpilot_app;
   ALTER DEFAULT PRIVILEGES IN SCHEMA identity GRANT ALL ON SEQUENCES TO jobbpilot_app;
   ```

2. **Step 2 (EF Core MigrateAsync via master-creds):** `AppIdentityDbContext.Database.MigrateAsync(ct)` med master-CS. Master har `CREATE ON DATABASE` → `CREATE SCHEMA IF NOT EXISTS identity` blir no-op (schemat finns från Step 1). Identity-migrations applicerade.

3. **Idempotency:** Re-run bootstrap är säker — `CREATE SCHEMA IF NOT EXISTS` är no-op, `GRANT` är no-op om redan satta, `MigrateAsync` är no-op om inga pending migrations.

### Phase A-utvidgning (init-mode)

Phase A (init-mode) utökas att skapa identity-schema + GRANTs ovan. Detta säkrar att framtida init-körningar (ny miljö, creds-rotation) får samma state. Bootstrap kan då köras direkt utan att fail:a på saknad identity-schema (då steg 1 är no-op).

### Schema-mode (oförändrad)

`schema`-mode kör endast `AppDbContext.MigrateAsync` med `jobbpilot_app`-creds. AppDbContext har inte `HasDefaultSchema` → inget `CREATE SCHEMA`-anrop → ingen privilege-konflikt.

## Konsekvenser

### Positiva

- **Permanent least-privilege bevarad** — `jobbpilot_app` får aldrig `CREATE ON DATABASE`. Saltzer/Schroeder 1975 ("Principle of Least Privilege") respekterad.
- **SoC (Dijkstra 1974) per role** — runtime-app vs migration-time har olika privilege-profiler.
- **12-Factor §V respekterad** — schema-DDL är release-stage (master-creds), inte run-stage (app-creds).
- **Bootstrap är idempotent** — kan re-runnas säkert vid debug.
- **Phase A i init säkrar framtid** — ny dev/staging/prod-stack får identity-schema-state via en init-körning.

### Negativa

- **Tre CLI-modes att underhålla** istället för två. Marginell yta.
- **Identity-schema-ändringar kräver bootstrap-körning** — manuell `aws ecs run-task`. Acceptabelt eftersom Identity-schema ändras sällan (~0-1 gång/år).
- **`workflow_dispatch` på deploy-dev.yml triggar inte bootstrap automatiskt** — vid framtida Identity-migration måste Klas manuellt köra bootstrap-task innan tag-push. TD-72-kandidat om det blir disciplin-problem.

### Risker som adresseras

- **Npgsql #1770 permission-denied bug** kringgås — vi använder master-creds för schemas med HasDefaultSchema.
- **F2-P0b-glömskan-mönstret** — bootstrap blir explicit deployment-step istället för manuell discipline.

## Alternativ övervägda

### Variant A1 — `GRANT CREATE ON DATABASE jobbpilot TO jobbpilot_app`

Bryter Least-Privilege permanent. Klas-egen analys flaggade det. Avvisat.

### Variant A2 — Pre-create identity-schema, kör Identity-migrations med jobbpilot_app

Löser inte Npgsql-buggen — PostgreSQL kräver `CREATE ON DATABASE` även när schemat finns. Avvisat efter web-search-verifiering.

### Variant A3 — Dedikerad `jobbpilot_migrations`-role för Phase E

Konceptuellt korrekt men kräver ny secret + Phase A-utvidgning + ny code-path. Lägger till komplexitet utan tydlig vinst över bootstrap-mode. Avvisat (KISS).

### Variant A4 — Custom MigrateAsync-implementation som skippar EnsureSchema

Bryter framework-encapsulation. Over-engineering. Avvisat (Stable Dependencies, YAGNI).

### Variant A5 (CTO-rekommenderad) — Identity-migrations via `init`-mode

Konceptuellt likvärdig med bootstrap-mode, men packar Identity in i init. Trade-off: Identity-schema-ändring orsakar creds-rotation som biverkning. Bootstrap-mode separerar dem — Identity kan deployas utan att rota creds. **Föredragen variant.**

## Implementationsstatus

- **F2-P8a.5d-final (denna leverans):** bootstrap-mode + Phase A-utvidgning + denna ADR.
- **Akut-deploy mot dev-RDS:** kör `aws ecs run-task` med `command=["bootstrap"]` efter ny image deployats till ECR.
- **Permanent path:** Vid framtida Identity-migration → kör bootstrap manuellt innan tag-push av app-deploy.

## Referenser

- Saltzer & Schroeder, ["The Protection of Information in Computer Systems"](https://www.cs.virginia.edu/~evans/cs551/saltzer/) (Communications of the ACM, 1975) — Principle of Least Privilege
- Dijkstra, "On the role of scientific thought" (1974) — Separation of Concerns
- Robert C. Martin, *Clean Architecture* (2017), kap. 7 (SRP per role), kap. 17 (Boundaries)
- [PostgreSQL Documentation — CREATE SCHEMA](https://www.postgresql.org/docs/current/sql-createschema.html)
- [Npgsql Issue #1770 — CREATE SCHEMA IF NOT EXISTS errors if user has no database CREATE privileges](https://github.com/npgsql/efcore.pg/issues/1770)
- [Npgsql Issue #1551 — Permission denied for database CREATE SCHEMA IF NOT EXISTS](https://github.com/npgsql/efcore.pg/issues/1551)
- [Twelve-Factor App, Factor V: Build, release, run](https://12factor.net/build-release-run)
- [Microsoft Learn — Applying migrations (multi-context)](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying)
- ADR 0013 — separate AppIdentityDbContext (Identity-schema-design)
- ADR 0033 — Migrate CLI-mode-dispatch
- CLAUDE.md §2.1 (Clean Architecture), §3.4 (fail-loud), §5.4 (security disciplin)

## Out of scope (denna ADR)

- **Auto-trigga bootstrap i deploy-dev.yml vid Identity-schema-change** — TD-72-kandidat. Manuell `aws ecs run-task` räcker för dev. Klas-disciplin per runbook.
- **Prod-RDS-bootstrap** — samma mekanism, samma image, kommande prod-task-def.

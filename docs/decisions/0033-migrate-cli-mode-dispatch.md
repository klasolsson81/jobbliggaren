# ADR 0033 — JobbPilot.Migrate CLI-mode-dispatch (init vs schema)

**Datum:** 2026-05-12
**Status:** Accepted 2026-05-12 (Klas-GO via "GO enligt CTO" 2026-05-12)
**Kontext:** F2-P8a.5 — EF Core migration-apply mot AWS dev-RDS (privat subnet)
**Beslutsfattare:** senior-cto-advisor 2026-05-12 (rond 2) + Klas Olsson (godkänd 2026-05-12)
**Relaterad:** ADR 0023 (Hangfire-infrastruktur), ADR 0032 (JobTech-integration — utlöste denna ADR via F2-P8a-migrationen), `docs/runbooks/hangfire-schema.md`, `docs/runbooks/aws-rds-migration-apply.md` (ny via F2-P8a.5b)

## Kontext

`JobbPilot.Migrate` är ett one-shot console-projekt som körs som ECS Fargate-task för schema-arbete mot AWS RDS (privat subnet, ingen lokal access). Vid leverans (STEG 14b — Hangfire schema-install) hade projektet fyra phases:

- **Phase A:** master-creds → REVOKE PUBLIC, CREATE ROLE × 3, GRANTs, CREATE SCHEMA hangfire
- **Phase B:** migrations-creds → `PostgreSqlObjectsInstaller.Install(connection, "hangfire")`
- **Phase C:** master-creds (re-fetched) → GRANT hangfire.* till worker, ALTER DEFAULT PRIVILEGES
- **Phase D:** Secrets Manager → `PutSecretValue` × 2 (app-CS + hangfire-CS)

Phase A regenererar 3 random-passwords (`GenerateRandomPassword(32)`) varje körning och kör `ALTER ROLE ... WITH PASSWORD '<new>'`. Phase D skriver de nya CS:erna till Secrets Manager. Living Api/Worker-containers har gamla creds i memory och måste restartas vid varje körning.

Konsekvens: **Migrate-projektet är designat för engångs-init eller vid creds-rotation, inte vid varje schema-mutation.**

EF Core-migrations levereras däremot vid varje feature-batch (~1/feature). Att rotera credentials vid varje migration är inte rimligt — det bryter zero-downtime-disciplinen och kräver app-restart.

F2-P8a-leveransen (ADR 0032 — JobTech-integration) genererade en ny EF-migration (`Fas2P8aJobAdExternalReference`) som ska appliceras mot dev-RDS. Vid kod-läsning upptäcktes:

1. **`src/JobbPilot.Api/Program.cs` har ingen `db.Database.MigrateAsync()`** — auto-migration vid Api-startup är inte aktiv (verifierat 2026-05-12 via Grep).
2. **F2-P0b-migrationen (`AddInvitationsAndWaitlist`, commit `0c58438`) är aldrig applicerad mot dev-RDS** — session-log 2026-05-12-1030 rapporterade "EF mappings + migration" som klar, men dev-RDS-apply skedde aldrig (verifierad lucka, current-work.md ljög om verkligheten).
3. **RDS är `publicly_accessible = false`** (security-disciplin per ADR-baserad review 2026-05-09) — direkt anslutning från Klas lokala maskin är blockerad.

Frågan blev: **hur ska EF Core-migrations appliceras mot dev-RDS utan att samtidigt rotera creds eller bygga ny infrastruktur?**

senior-cto-advisor (rond 1) valde Variant 5 — utöka befintliga `JobbPilot.Migrate` med en Phase E som kör `Database.MigrateAsync()`. Detta utlöste en sekundärfråga om hur Phase E ska invokeras utan att samtidigt köra Phase A-D (rotation-risk). Rond 2 av CTO valde Variant β-modifierad (CLI-arg-dispatch utan default).

## Beslut

`JobbPilot.Migrate` får en **default-less CLI-arg-dispatch** med två kommando-modes:

```bash
JobbPilot.Migrate init     # Phase A → B → C → D (engångs-init eller creds-rotation)
JobbPilot.Migrate schema   # Phase E (EF Core Database.MigrateAsync mot dev-RDS)
```

**Saknad arg eller okänd arg → exit 1 med usage-text.** Ingen default-mode.

### Invocation-pattern

Båda modes körs som ECS Fargate-tasks via `aws ecs run-task` med samma image (`jobbpilot-migrate:latest`) men olika `command`-override per task-execution:

```json
// init-task
"containerOverrides": [{ "command": ["dotnet", "JobbPilot.Migrate.dll", "init"] }]

// schema-task
"containerOverrides": [{ "command": ["dotnet", "JobbPilot.Migrate.dll", "schema"] }]
```

### Phase E-implementation

Phase E ansluter med **`jobbpilot_app`-creds** (fetchad från Secrets Manager via `MIGRATE_APP_CONN_SECRET_ARN`-env-var, samma role som Api kör med):

1. `GetSecretValueAsync` → CS-sträng
2. `DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs, ... MigrationsAssembly).UseSnakeCaseNamingConvention()`
3. `await dbContext.Database.GetPendingMigrationsAsync(ct)` → lista pending
4. Logga pending-listan (för CloudWatch-spårbarhet)
5. `await dbContext.Database.MigrateAsync(ct)` → applicerar alla pending
6. Idempotent — re-run efter completed migration är no-op

**`jobbpilot_app` har `GRANT USAGE, CREATE ON SCHEMA public` + `GRANT ALL ON ALL TABLES` + `ALTER DEFAULT PRIVILEGES`** från Phase A (rader 271-282) → räcker för `__EFMigrationsHistory`-tabellen och alla CREATE TABLE/ALTER TABLE-statements som EF genererar. Master-creds används aldrig i Phase E (least-privilege per Saltzer/Schroeder 1975).

### Code structure-refactor

Phase A-D extraheras till `RunInitAsync(...)` (top-level static method). Phase E som `RunSchemaAsync(...)`. `Program.cs`:s main-flow blir:

```csharp
var mode = args.Length == 1 ? args[0] : null;
return mode switch
{
    "init"   => await RunInitAsync(cts.Token),
    "schema" => await RunSchemaAsync(cts.Token),
    _        => PrintUsage(),  // exit 1
};
```

## Avvisade alternativ

### Variant α — Env-var-gate (`MIGRATE_RUN_INIT_PHASES=true|false`)

Bryter Principle of Least Astonishment (Saltzer/Schroeder 1975 "Psychological acceptability"). ECS task-definition-`environment`-block är mindre synligt än `command`-array vid manuell läsning. Två task-def-konfigurationer att underhålla = CCP-brott (samma binär konfigureras på två platser).

### Variant γ — Phase A-D idempotent på creds (skip ALTER ROLE om roll finns)

Skapar falsk säkerhet — connection-test mot existing creds säger "creds fungerar" men inte "creds är de senaste enligt rotation-policy". Säkerhetsanalys av idempotent password-pattern är icke-trivial (race conditions med AWS Secrets Manager mid-flow-rotation). Anti-pattern: "lägg till logik så vi slipper välja mode" — vi måste ändå välja, bara implicit.

### Variant δ — Separat `JobbPilot.EfMigrate`-projekt

REP-brott (Martin 2017, kap. 13) — båda modes delar samma deployment-artefakt-shape (console app → ECS Fargate task → samma IAM-role-template → samma logging-pipeline). Två projekt = två ECR-repos, två CI-paths, två Dockerfiles för samma deployment-pattern. SRP-vinsten realiseras via CLI-dispatch — modul-split utöver det är YAGNI.

### Variant ε — Auto-detect via `__EFMigrationsHistory`-existens

Implicit beteende = anti-pattern för operations-tooling. Operatören kan inte vid läsning av task-def avgöra vad som kommer hända. Failure mode: om Phase A failed mid-flow så att `__EFMigrationsHistory` inte skapats men roller delvis finns → re-run kör A-E igen → oavsiktlig creds-rotation. Förstör observerbarhet (Ford/Parsons/Kua 2017 — fitness functions).

### Variant — Default mode `schema`

Default-less dispatch eliminerar surprise-mode. Saknad arg → exit 1 med usage = fail-fast. Saltzer/Schroeder "ease of use" tolkas inte som "kortast möjlig invocation" utan som "explicit och förutsägbar".

## Konsekvenser

### Positiva

- **SRP per invocation** — Phase A-D ändras vid creds-rotation-policy (~1×/år); Phase E ändras vid varje schema-mutation (~1×/feature). Olika change-reasons, olika cadence, samma binär.
- **Observerbar deployment-behavior** — operatör läser `command`-array i task-def och vet vad som händer.
- **Zero-downtime schema-changes** — Phase E rör inte creds, Api/Worker fortsätter köra under apply (förutsatt backward-compatible migration).
- **12-Factor-compliance** (§V Build/Release/Run, §X Dev/Prod Parity) — samma image, olika invocation-args per körnings-typ.
- **Microsoft Learn-rekommendation** — separat migration-utility per [Apply migrations at runtime](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying).
- **Mönster för prod-deploys** — samma mekanism dev som prod, en arkitektur.

### Negativa

- **Existerande task-def utan args måste uppdateras** att skicka `init` (engångsändring, inte underhållskostnad).
- **CLI-arg-parsning kräver några rader extra kod** — trivial yta (switch på `args[0]`, ingen `System.CommandLine`).
- **Två modes att underhålla** — acceptabelt; båda har dokumenterade triggers.

### Risker som adresseras

- **Oavsiktlig creds-rotation vid EF-migration** → eliminerad (explicit `init` krävs).
- **Privat subnet blockerar lokal apply** → löst via ECS-task-pattern.
- **Auto-migration vid Api-startup-race i multi-instance dev** → undviks (Phase E är separat deployment-step).

## Implementationsstatus

- **F2-P8a.5a** (denna leverans): CLI-dispatch + Phase E-impl + `RunInitAsync`/`RunSchemaAsync`-helpers + MigrateLog-entries + Migrate.csproj ProjectReference till Infrastructure.
- **F2-P8a.5b** (denna leverans): `docs/runbooks/aws-rds-migration-apply.md` — operations-runbook.
- **F2-P8a.5d** (efter Klas-GO): Deploy-dev → manual `aws ecs run-task` Migrate `schema` → applicerar F2-P0b (`AddInvitationsAndWaitlist`) + F2-P8a (`Fas2P8aJobAdExternalReference`) mot dev-RDS → smoke-test.
- **Befintliga init-task-def i `infra/terraform/`:** behöver uppdateras att skicka `init`-arg när nästa creds-rotation körs. Detta är inte blockerande för F2-P8a.5d eftersom schema-mode är ny task-execution.

## TD-validering

- **Inte TD.** F2-P8a.5 är grundförutsättning för att F2-P8a ska kunna nå dev-RDS.
- **Eventuell TD (Minor) — TD-70:** automatisera Migrate `schema`-task som steg i `deploy-dev.yml` mellan ECR-push och Api-deploy. YAGNI nu — manuell `aws ecs run-task` räcker. Trigger: andra gången glömskan att köra Migrate.

## Validation

- **Architecture-test:** `JobbPilot.Migrate` har project-reference till `JobbPilot.Infrastructure` (Phase E behöver `AppDbContext`).
- **Smoke-test efter dev-apply:** `SELECT external_id FROM job_ads LIMIT 0` ska returnera 0 rader (verifierar att kolumnen finns och är queryable).
- **Idempotency-test:** re-run `Migrate schema` direkt efter completed apply → loggar "No pending migrations" → exit 0.

## Out of scope (denna ADR)

- ~~Auto-invoke `schema`-mode i deploy-dev.yml — TD-70-kandidat.~~ **Levererad via amendment 2026-05-12 (samma dag) — se nedan.**
- **Migration-rollback-procedur** — EF Core stöder inte automatisk rollback. Manuell `dotnet ef migrations remove` lokalt + ny revert-migration. Dokumenteras i `aws-rds-migration-apply.md`.
- **Prod-RDS-migrations** — samma mekanism, men prod-task-def är separat. Levereras vid första prod-deploy.

---

## Amendment 2026-05-12 — Auto-trigga `schema`-mode i `deploy-dev.yml`

**Datum:** 2026-05-12 (samma dag som ursprungs-ADR)
**Status:** Accepted (Klas-GO "GO enligt CTO rek" 2026-05-12)
**Decision-maker:** senior-cto-advisor 2026-05-12 (rond 3) + Klas Olsson

### Kontext för amendment

Klas-direktiv 2026-05-12: "Så mycket som möjligt automatiskt." Manuell `aws ecs run-task` per runbook (originalt ADR-beslut) byggde på operatör-disciplin. Samma session upptäckte att F2-P0b-migrationen är aldrig applicerad mot dev-RDS — exempel på samma class of failure som memory `feedback_di_with_handlers_same_commit` adresserar ("CI fångar broken state, lokal disciplin räcker inte").

### Beslut (Variant A)

`deploy-dev.yml` auto-triggar `JobbPilot.Migrate schema`-task som GitHub Actions-steg **mellan ECR-push och Api/Worker-deploy**:

1. Bygg + push api/worker/migrate-images
2. Register Migrate task-def med ny image
3. **Run Migrate schema-task** (Phase E, EF Core `MigrateAsync`) — block tills exit = 0
4. Deploy Api service (wait-for-stability)
5. Deploy Worker service (no-wait)
6. Smoke-test

Network-config (subnet + SG) hämtas runtime via `aws ecs describe-services --services jobbpilot-dev-api` — Migrate och Api delar awsvpcConfiguration. Inget terraform-output eller SSM Parameter Store behövs.

### Motivering (CTO rond 3)

- **12-Factor §V Build/Release/Run:** Schema-migration är release-stage, inte run-stage. Variant A placerar den exakt mellan build-complete och process-start.
- **Fitness function** "schema-applied-before-Api-starts" blir mekanisk garanti via CI-fail. Variant B/C lägger det på mänsklig disciplin = bevisat otillräckligt.
- **SRP per pipeline:** `deploy-dev.yml` har en change-reason ("vad händer vid `v*-dev`-tag"). Schema-apply hör till samma change-reason.
- **YAGNI:** `workflow_dispatch` (rad 24-29) täcker ad-hoc manuell trigger utan tag — separat PowerShell-script vore duplicering.

### IAM-utbyggnad

`modules/github_oidc/main.tf` får ny `EcsRunMigrateTaskInDevCluster`-statement:
- Actions: `ecs:RunTask`, `ecs:StopTask`
- Resources: Migrate-task-def-ARN + task-ARN-pattern
- Condition: `ArnEquals` på `ecs:cluster` = `jobbpilot-dev-cluster`

Least-privilege bevarat — RunTask kan bara trigga Migrate-task-def i dev-cluster, inte Api/Worker-task-defs och inte andra clusters.

### Konsekvenser av amendment

- **Deploy-tid ökar med ~30-60s** vid varje tag-push även när no pending migrations (idempotency-check är billig — `GetPendingMigrationsAsync` returnerar tom lista).
- **Deploy fail:ar om migration fail:ar** — önskat beteende. Api startar inte med inkonsistent schema.
- **`docs/runbooks/aws-rds-migration-apply.md`** uppdateras: manuell procedur blir nu **fallback** (vid `workflow_dispatch`-trigger eller direct `aws ecs run-task` vid debug).
- **TD-70-kandidaten stängs samtidigt som amendment levereras** — den var aldrig lyft som TD i `docs/tech-debt.md`.

### Pre-deploy Klas-action

`terraform apply` mot dev-stacken krävs för IAM-uppdateringen **innan** första `v0.2.0-dev`-tag-push. Annars failar workflow-step "Run Migrate schema-task" med `AccessDeniedException` på `ecs:RunTask`.

### Avvisade alternativ (kort)

- **Variant B (manuell script):** Discipline-baserad failure-mode bevisat otillräcklig.
- **Variant C (hybrid):** YAGNI — `workflow_dispatch` täcker ad-hoc-behovet.
- **Variant D (post-deploy schema):** 12-Factor §V-brott — Api startar med gammalt schema.

## Referenser

- Robert C. Martin, *Clean Architecture* (Prentice Hall, 2017) — kap. 7 (SRP), kap. 13 (REP/CCP component cohesion)
- Saltzer & Schroeder, "The Protection of Information in Computer Systems" (Proceedings of the IEEE, 1975) — Psychological acceptability / Least Astonishment / Least Privilege
- Adam Wiggins, *The Twelve-Factor App* (12factor.net), §V (Build/Release/Run), §X (Dev/Prod Parity)
- Microsoft Learn — [Applying migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying) — separat migration-utility-rekommendation
- Ford/Parsons/Kua, *Building Evolutionary Architectures* (O'Reilly, 2017), kap. 4 — fitness functions, observability
- Martin Fowler, *Evolutionary Database Design* (martinfowler.com/articles/evodb.html, 2003)
- Ambler & Sadalage, *Refactoring Databases* (Addison-Wesley, 2006)
- ADR 0023 — Worker-pipeline-aktivering + Hangfire-infrastruktur (samma deployment-pattern)
- ADR 0032 — JobTech-integration (utlöste denna ADR via F2-P8a-migrationen)
- CLAUDE.md §3.4 (Result-pattern), §9.6 (fas-regeln)
- `docs/runbooks/hangfire-schema.md` (befintlig — uppdateras med två-modes-noting)
- `docs/runbooks/aws-rds-migration-apply.md` (ny via F2-P8a.5b)

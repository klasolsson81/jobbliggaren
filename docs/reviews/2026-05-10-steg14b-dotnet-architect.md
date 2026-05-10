# Architecture Review — STEG 14b JobbPilot.Migrate

**Granskat:** 2026-05-10
**Auktoritet:** Clean Arch, CLAUDE.md §2-5, BUILD.md
**Status:** APPROVE-WITH-FIXES

Migrate-app:en är välarkitekterad för sin smala roll: en HTTP-fri, idempotent, one-shot DDL-init utan domain-koppling. Inga kritiska Clean Arch- eller DDD-brott. Två viktiga fynd kring SQL-injection-yta och nullability i RDS-secret-DTO bör fixas innan first run.

---

## Viktigt

### Viktigt-1 — `CreateRoleIfNotExistsAsync` SQL-injection-yta
**Fil:** `src/JobbPilot.Migrate/Program.cs:256-265`

Lösenordet interpoleras direkt i SQL-strängen via `string.Create(...$"...PASSWORD '{password}'")`. Postgres tillåter inte parameterbindning för CREATE/ALTER ROLE-pwd, men det aktuella password-charsetet (`[a-zA-Z0-9]`) skyddar bara av lyckans skull — om någon i framtiden utvidgar `GenerateRandomPassword`-charsetet med `'` eller `\` blir detta en SQL-injection-vektor.

**Föreslagen åtgärd:** Använd `format('%I', $1)`/`format('%L', $2)` via Postgres `EXECUTE`:

```csharp
var sql = @"
DO $do$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = @role) THEN
        EXECUTE format('CREATE ROLE %I LOGIN PASSWORD %L', @role, @pwd);
    ELSE
        EXECUTE format('ALTER ROLE %I WITH LOGIN PASSWORD %L', @role, @pwd);
    END IF;
END
$do$;";
await using var cmd = new NpgsqlCommand(sql, conn);
cmd.Parameters.AddWithValue("role", roleName);
cmd.Parameters.AddWithValue("pwd", password);
```

`%I` quotar identifier, `%L` quotar literal — Postgres-native escape.

### Viktigt-2 — `RdsMasterSecret`-record null-validation saknas
**Fil:** `src/JobbPilot.Migrate/Program.cs:279`

`sealed record RdsMasterSecret(string username, string password)` — properties är icke-nullable men `JsonSerializer.Deserialize` kan producera record med null-fält om JSON saknar dem.

**Föreslagen åtgärd:**
```csharp
if (string.IsNullOrEmpty(masterCreds.Username) || string.IsNullOrEmpty(masterCreds.Password))
    throw new InvalidOperationException("Master secret saknar username eller password");
```
Plus PascalCase + `[JsonPropertyName("username")]` för idiomatic .NET-API.

---

## Mindre

### Mindre-1 — Top-level catch-all
**Fil:** Program.cs:148-152
OK för console-entry-point. Eventuell förbättring: separera `NpgsqlException` från `AmazonSecretsManagerException` för bättre triage.

### Mindre-2 — csproj saknar uttryckliga properties
Antas ärva från `Directory.Build.props`. OK om `Directory.Build.props` sätter `Nullable=enable` + `TreatWarningsAsErrors=true`. Verify only.

### Mindre-3 — Dockerfile `JobbPilot.sln`-COPY-cache-symmetri
**Fil:** Dockerfile:13
Migrate har inga ProjectReferences. Skippa `JobbPilot.sln` i COPY-line för cleaner cache-strategy.

---

## Nit

1. `string.Create` overkill för connection-string — OK som-är (repo-konvention)
2. `Fingerprint` exponerar 50% av entropi — flytta till TD eller SHA256-trunc
3. `PostgreSqlObjectsInstaller.Install` synchront — OK i console-context, **NEJ till Task.Run-wrap** (CLAUDE.md §3.5: Task.Run bara för CPU-bundet)

---

## Frågesvar

1. **Clean Arch-position** — Korrekt utan ProjectReferences. Migrate är ops-tool, inte domain-konsument.
2. **`PostgreSqlObjectsInstaller.Install`-stabilitet** — `public static` sedan 1.0 i Hangfire.PostgreSql. Inte värt wrap:a i port. YAGNI.
3. **One-shot console vs migrate-mode i Worker** — Klas-valet är arkitektoniskt korrekt. Separation, image-storlek, DI-graf-frånvaro, roll-isolation.
4. **Top-level statements vs explicit Main** — Top-level är robust med separat MigrateLog.cs partial class. Behåll.
5. **Connection-string-format-konsistens** — Verifierad. Format identiskt med STEG 13b-placeholder.
6. **Idempotens-mönster** — Helt re-runnable. Phase A roterar pwds vid re-run, Phase D skriver nya secrets. Mid-flow-fail → re-run löser det.
7. **Container-resource-sizing** — 256 CPU/512 MB OK för Fargate-minimum + Migrate-workload.
8. **Async-pattern** — Top-level `await` + pre-computed fingerprints — idiomatic.
9. **`aws ecs run-task --network-configuration`** — Saknas dokumentation. Lägg `migrate_run_task_command` Terraform output (Sec-Major-2 från security-auditor).
10. **TD-37-koppling** — Migrate påverkar inte TD-37. Testcontainers + WebApplicationFactory rör inte Hangfire-schema mot dev-RDS.

---

## Action-plan före first run

1. **Viktigt-1:** SQL-injection-defense via `format()` med parameters
2. **Viktigt-2:** RdsMasterSecret null-validation + PascalCase + JsonPropertyName
3. **Mindre-3:** Dockerfile-cache-strategy (skippa sln)
4. (Klas-val) `migrate_run_task_command` Terraform output

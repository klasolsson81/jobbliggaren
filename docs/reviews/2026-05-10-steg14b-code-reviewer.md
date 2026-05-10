# Code-review: STEG 14b Phase 1 — JobbPilot.Migrate + Terraform-utvidgningar

**Status:** APPROVE-WITH-FIXES
**Granskat:** 2026-05-10
**Auktoritet:** CLAUDE.md §3 (C#-standarder), §5.1 (BE anti-patterns), `docs/runbooks/hangfire-schema.md`, ADR 0023
**Scope:** `src/JobbPilot.Migrate/*`, `Directory.Packages.props`, `JobbPilot.sln`, `infra/terraform/modules/iam_ecs/*`, `infra/terraform/modules/ecs/*`, `infra/terraform/environments/dev/{main,variables}.tf`

Ingen Blocker. Två Major (båda enkelt åtgärdade). Sex Minor + ett par Nits. IAM-detalj-yta out-of-scope per request — security-auditor-review separat.

---

## Sammanfattning per audit-fråga

| # | Fråga | Verdict |
|---|-------|---------|
| 1 | CLAUDE.md §5 anti-patterns | OK med en Major (Major-1: Phase B uses sync `Install` blocking the async event-loop — borderline § 5.1 sync-I/O. Easy fix.) |
| 2 | Kommentar-kvalitet | OK — bra balans, förklarar WHY (Phase-mappning, idempotens-rationale, CA1873) |
| 3 | Async/CancellationToken | Acceptabelt för one-shot console (Major-2 nedan beskriver minimal-fix om man vill ha clean SIGTERM-handling i Fargate stopTimeout=30s-fönstret) |
| 4 | Postgres-rolnamn unquoted lowercase | OK — `jobbpilot_migrations` etc. är unquoted-safe (lowercase + underscore + alphanumeric). Postgres folder unquoted till lowercase, så `CREATE ROLE jobbpilot_migrations` matchar `pg_roles.rolname = 'jobbpilot_migrations'`. **Viktigt:** matchande connection-string i `BuildConnectionString` får inte heller citera. Verifierat: rad 104, 128, 129 — strängliteraler, ingen citation. OK. |
| 5 | DO-block-syntax | OK — `DO $$ ... $$;` är standard plpgsql. Funkar via `NpgsqlCommand.ExecuteNonQueryAsync()` utan special-flags. plpgsql-language är default i Postgres ≥9.0. Inga issues. |
| 6 | `PostgreSqlObjectsInstaller.Install` synchron | **Major-1** — se nedan. Synchronous SQL-execution mitt i async-flow. Inte en CLAUDE.md-blocker (det är inte `.Result`/`.Wait()`-på-Task), men blockerar event-loopen i 100-500 ms. Acceptabelt för one-shot console — flagga som Minor-Major. |
| 7 | ECS task-def count-pattern | OK — `count = var.migrate_image_uri != "" ? 1 : 0` är idiomatic Terraform för optional resources. Outputs använder `length(...) > 0 ? [0].x : ""` korrekt. |
| 8 | Container-namn `migrate` matchar task-def | OK — `name = "migrate"` på rad 131 matchar konventionen från api/worker. Run-task-cmd: `aws ecs run-task --task-definition jobbpilot-dev-migrate` använder family-namnet, inte container-namnet, så ingen explicit referens nödvändig här. |
| 9 | `AWS_REGION` env-var-konflikt | OK — `AWS_REGION` är AWS SDK-standard env-var som läses automatiskt av default-credential-chain. Att Migrate också läser den via `RequiredEnv` är harmless dupe (samma värde). Inget bug, men se Minor-3 nedan för förenkling. |
| 10 | Conventional Commits | Verifierar inte commits här — påminnelse i Output-sektionen att följa `feat(migrate):` / `feat(infra):` / `feat(deps):` per scope. |
| 11 | Solution-struktur — Migrate utan Domain/Application-refs | OK — Migrate är en **standalone DDL-init-tool**, inte en del av app-runtime. Den hanterar Postgres-objekt på en yta som är *infrastruktur* (rolls + scheman), inte domän. Den har ingenting med JobbPilot-aggregaten att göra. Att lägga `using ProjectReference` till Domain/Application skulle vara inkorrekt — det skulle skapa cyklisk semantik (Migrate prepar:ar databasen som Application sedan använder). Strukturen som standalone projekt under `src/` är korrekt. **Inte ett brott mot Clean Arch §2.1.** Tvärtom — den respekterar gränserna genom att inte korsa dem. |
| 12 | CA1873 pre-compute fingerprints | OK — pre-compute utanför `LoggerMessage`-call är ren approach. Alternativ: `[LoggerMessage(SkipEnabledCheck = true)]` på MigrateLog för att tysta CA1873 generellt — men det skulle dölja real-cases där fingerprints är dyra. Pre-compute här är explicit + lokalt. Behåll. |

---

## Major (bör fixas innan apply)

### Major-1: Synchronous `PostgreSqlObjectsInstaller.Install` i async-flow

**Fil:** `src/JobbPilot.Migrate/Program.cs:110`
**Nuvarande:**
```csharp
await migrationsConn.OpenAsync();
PostgreSqlObjectsInstaller.Install(migrationsConn, "hangfire");
```

**Problem:** `Install(...)` är synchronous och skapar ~13 tabeller + sequences + functions. Bibehållet i async-flow blockerar event-loopen 100-500 ms. CLAUDE.md §3.5 förbjuder `.Result`/`.Wait()` (sync-on-task), och §5.1 förbjuder synchronous I/O i request-pipeline. *Console one-shot* är inte en request-pipeline, men mönstret är fortfarande "fly any sync I/O i async kod" — orsakar TaskScheduler-confusion om `loggerFactory` har async sinks (det har den inte här, men flag:ar för konsekvens).

**Förslag:** Wrappa i `await Task.Run(...)`:
```csharp
await migrationsConn.OpenAsync();
// Install är synchron — kör på threadpool för att inte blockera event-loopen.
// Tar 100-500 ms, ~13 tabeller + sequences + functions.
await Task.Run(() => PostgreSqlObjectsInstaller.Install(migrationsConn, "hangfire"));
MigrateLog.HangfireInstallComplete(log);
```

CLAUDE.md §3.5: "Task.Run bara för CPU-bundet arbete, aldrig för I/O." — *strikt läsning skulle peka mot await:bar API, men `Hangfire.PostgreSql 1.21.1` exposar inte `InstallAsync`*. Wrappa-i-Task.Run är pragmatiskt avsteg som flagas i kommentar. Alternativt: tolerera sync som en känd one-shot-quirk och lägg en kommentar som förklarar att vi medvetet INTE wrappar.

**Severity:** Major (men gränsfall). Klas avgör om Task.Run-wrap eller en explicit kommentar räcker.

**Delegera till:** dotnet-architect för Task.Run-wrap-beslut. Implementation trivial.

### Major-2: Saknad CancellationToken-flow → Fargate stopTimeout-handling

**Fil:** `src/JobbPilot.Migrate/Program.cs:36-152` (top-level)
**Problem:** Inga `CancellationToken` propageras genom flödet. Fargate skickar SIGTERM vid stop (default 30s timeout per task-def `stopTimeout = 30`). En Migrate som hänger mid-Phase-A på en `await ExecuteAsync` har ingen mekanism att hyfsat avbryta — den får SIGKILL efter 30s.

För en one-shot där typisk runtime är 5-10s är detta osannolikt en real-bug. Men CLAUDE.md §3.5: "CancellationToken propageras genom hela kedjan" är otvetydig. Det är konventionsavvikelse.

**Förslag:**
```csharp
// Top-level — registrera SIGTERM-handler för Fargate stop.
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();

// Propagera ct genom helpers — ExecutePhaseA/C, CreateRoleIfNotExistsAsync, ExecuteAsync.
await secretsClient.GetSecretValueAsync(req, cts.Token);
await cmd.ExecuteNonQueryAsync(cts.Token);
// etc.
```

Aktivt arbete: ~20 min. För one-shot DDL kan Klas argumentera att risken är teoretisk och låta passera. Men för konsekvens med resten av kodbasen (`Worker`, `Api` propagerar `ct` korrekt) bör Migrate följa samma mönster.

**Severity:** Major (konventionsavvikelse §3.5). Easy fix.

**Delegera till:** Implementation-agent (Klas eller dotnet-architect-driven session).

---

## Minor

### Minor-1: Magic strings för Postgres-rolnamn (`"jobbpilot_migrations"` etc.)

**Fil:** `src/JobbPilot.Migrate/Program.cs:189-191, 194, 211-212, 215-226, 232-237, 241-247`
**Problem:** Rolnamnen `"jobbpilot_migrations"`, `"jobbpilot_app"`, `"jobbpilot_worker"` upprepas ~15 ggr som strängliteraler. CLAUDE.md §5.1 "Magic strings — alltid konstanter eller enums".

**Förslag:** Lägg till const-block överst:
```csharp
internal static class Roles
{
    public const string Migrations = "jobbpilot_migrations";
    public const string App = "jobbpilot_app";
    public const string Worker = "jobbpilot_worker";
}
```
Rationale: rename-säkerhet + sökbarhet om Postgres-rolnamn någonsin ändras. Lägg också till `HangfireSchemaName = "hangfire"` i samma block (förekommer ~12 ggr).

**Severity:** Minor — kosmetiskt men matchar konventionen klart.

### Minor-2: SQL-injection-vektor i `CreateRoleIfNotExistsAsync` (teoretisk)

**Fil:** `src/JobbPilot.Migrate/Program.cs:256-265`
**Problem:** `CREATE ROLE {roleName} LOGIN PASSWORD '{password}';` interpolerar password som strängliteral. Värdet kommer från `GenerateRandomPassword(32)` som genererar `[a-zA-Z0-9]` — så ingen `'` eller `\` finns i pwd, och injektion är *omöjlig vid det specifika charset:et*. Men det är en defensiv sköld som kan tappas vid framtida charset-utvidgning ("låt oss lägga till specialtecken").

DO-block-context tillåter inte parameterized queries (Postgres begränsning — `EXECUTE format(...)`-pattern krävs istället för riktig parametrisering inne i plpgsql). Så ingen trivial fix finns.

**Förslag:** Lägg en kommentar som dokumenterar antagandet:
```csharp
// SQL-injection-säkerhet: GenerateRandomPassword använder charset [a-zA-Z0-9]
// — inga ' eller \-tecken kan dyka upp. Om charset någonsin utvidgas:
// byta till EXECUTE format('CREATE ROLE %I LOGIN PASSWORD %L', role, pwd) inom DO-block.
```

**Severity:** Minor — defensiv kommentar. Inget aktivt bug idag.

### Minor-3: `AWS_REGION` env-var dubblerar SDK:n's egen mekanism

**Fil:** `src/JobbPilot.Migrate/Program.cs:56, 65`
**Problem:** `RequiredEnv("AWS_REGION")` + `RegionEndpoint.GetBySystemName(awsRegion)` gör manuellt det AWS SDK redan gör automatiskt om `AWS_REGION` finns i env. Förenkling: skippa explicit lookup, låt SDK plocka från env.

**Förslag:**
```csharp
var secretsClient = new AmazonSecretsManagerClient();  // Region läses från AWS_REGION env-var automatiskt
```

Behåll `awsRegion`-variabeln om den används i log-message — men `MigrateLog.StartingMigrate` är trivial att ersätta med `RegionEndpoint.PrimaryEndpoint?.SystemName` post-construction.

**Severity:** Minor — kosmetiskt. Funktionellt identiskt.

### Minor-4: `Trust Server Certificate=true` i connection-strings

**Fil:** `src/JobbPilot.Migrate/Program.cs:164`
**Problem:** `Trust Server Certificate=true` accepterar invalid/self-signed cert. RDS Postgres serverar AWS-managed cert som chain:ar till Amazon-trust-store — så reell trust-validering bör räcka.

**Förslag:** Sätt `Trust Server Certificate=false` och hantera CA-bundle separat (eller kontrollera att RDS-cert är publikt valid utan extra root-CA i .NET 10 runtime). Om validering failer: lägg `Root Certificate=/etc/ssl/certs/rds-ca.pem` i image. Verifiering före apply rekommenderas.

**Severity:** Minor — security-hardening, inte breakage. Säkerhetsmässigt borderline-Major men out-of-scope per request (security-auditor).

### Minor-5: `RdsMasterSecret`-record har lowercase-properties

**Fil:** `src/JobbPilot.Migrate/Program.cs:279`
**Problem:** `record RdsMasterSecret(string username, string password)` matchar JSON-keys (RDS-managerad secret är `{"username": ..., "password": ...}`). Lowercase är korrekt mot JSON men bryter mot C#-konvention för record-property-namn (CLAUDE.md §3.2: `PascalCase` för publik yta, `_camelCase` för private fields).

**Förslag:**
```csharp
sealed record RdsMasterSecret(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("password")] string Password);
```
Krav: `using System.Text.Json.Serialization;` och uppdatera `masterCreds.username` → `masterCreds.Username` på rad 72, 74.

**Severity:** Minor — namngivning-konvention.

### Minor-6: Wide ECS Exec-message-yta (`resources = ["*"]`)

**Fil:** `infra/terraform/modules/iam_ecs/main.tf:350` (task_migrate)
**Problem:** `ssmmessages:*` på `["*"]` — samma som task_api/task_worker. Out-of-scope per request, men noterar att security-auditor redan har sett mönstret som accepterat (api/worker).

**Severity:** Minor — konsistent med befintlig pattern. OK.

---

## Nits

### Nit-1: Phase A REVOKE PUBLIC från db använder `string.Create` med `dbName`-interpolation
**Fil:** `Program.cs:184-186` — `dbName` injiceras i SQL via `string.Create` med strängliteral. Eftersom `dbName` kommer från env-var som Klas kontrollerar är det inte injection-vektor — men kommentar att dokumentera antagandet skulle vara konsistent med Minor-2.

### Nit-2: `Trust Server Certificate` → `TrustServerCertificate` är canonical Npgsql-keyword
Båda fungerar (Npgsql är keyword-tolerant) men `TrustServerCertificate=true` är canonical.

### Nit-3: Outputs på `task_migrate_role_arn` returnerar `""`-sträng vid count=0
**Fil:** `infra/terraform/modules/iam_ecs/outputs.tf:18` — Tom sträng som "not-set"-signal är ok men inte typisk Terraform-idiom. `null` är cleaner. Påverkar inte funktionalitet — `var.task_migrate_role_arn != ""`-check matchar count-pattern downstream.

---

## Bra gjort

- **Phase-uppdelning är tydlig och dokumenterad** — Program.cs:1-23 förklarar exakt vad varje fas gör. Klas eller framtida operatör kan läsa header-kommentaren och förstå flow:t utan att läsa koden.
- **LoggerMessage source-gen** — MigrateLog.cs följer CA1848-konventionen i resten av repot. EventId-numrering är logisk (10/20/30/40-buckets per fas, 100-bucket för per-statement, 999 för error).
- **Idempotens via DO-block** — re-run efter delvis fail är säkert. Bra defensive design.
- **Säkerhetsskydd**:
  - REVOKE PUBLIC från db + hangfire-schema (defense-in-depth)
  - Random-pwds 32 char (~190 bits)
  - Inga klartext-pwds i log — bara fingerprints
  - jobbpilot_worker har bara DML-yta (INTE CREATE/DROP)
- **Phase D — Secrets Manager-write** är enkel + verifierbar via post-task `aws secretsmanager get-secret-value`.
- **Terraform count-pattern** — `var.migrate_image_uri != ""`-gate gör att existing dev-stack inte tvingas till migrate-roll. Backwards-compatibel migration.
- **IAM blast-radius isolerad** — task_migrate har PutSecretValue *bara* på app + hangfire-secret. Api/Worker behåller read-only Secrets-yta.
- **CA1873 pre-compute** — explicit, lokal, kommenterad. Alternativen (analyzer-suppression) skulle vara sämre.
- **Minimal Dockerfile-yta** — `dotnet/runtime:10.0-noble` (ej aspnet) sparar ~80 MB. Non-root user. Inga onödiga ProjectReferences.
- **Standalone-projekt-design** — Migrate har korrekt isolerats från Domain/Application/Infrastructure. Det är en infrastruktur-tool, inte app-kod.
- **Hangfire.PostgreSql 1.21.1** matchar både Worker och Migrate via central package management.
- **Newtonsoft.Json 13.0.3 transitiv-pin** — bra CVE-hygien dokumenterad i Directory.Packages.props.

---

## Sammanfattning

**Status:** APPROVE-WITH-FIXES

| Severity | Antal | Krävs innan apply? |
|----------|-------|---------------------|
| Blocker  | 0     | —                   |
| Major    | 2     | Bör fixas (Major-1 + Major-2) |
| Minor    | 6     | Nice-to-fix         |
| Nit      | 3     | Optional            |

### Rekommenderat åtgärdsförslag

**Innan `terraform apply` + first run-task:**

1. Major-1: Wrap `PostgreSqlObjectsInstaller.Install` i `Task.Run`. ~5 min.
2. Major-2: Lägg `CancellationTokenSource` + propagera `ct` genom flödet. ~20 min.
3. Minor-1: Const-block `Roles` + `HangfireSchemaName`. ~10 min.
4. Minor-5: PascalCase + `JsonPropertyName` på `RdsMasterSecret`. ~3 min.

**Kan göras post-apply (TD-ärende eller follow-up):**

- Minor-2 (SQL-injection-kommentar)
- Minor-3 (AWS_REGION dedupe)
- Minor-4 (Trust Server Certificate-tightening — kräver verifiering)
- Minor-6 (ECS Exec-yta — out-of-scope, security-auditor)
- Nits

### Delegationer

- Major-1 + Major-2 + Minor-1 + Minor-5 → implementation-agent eller Klas direkt
- Minor-4 → security-auditor (TLS-verifikation mot RDS-cert) före tightening
- Conventional Commits-påminnelse: `feat(migrate): one-shot DDL-init console-app` + `feat(infra): migrate task-def + task-role` + `chore(deps): Npgsql + AWSSDK.SecretsManager + Logging.Console för Migrate`

Re-review krävs INTE om bara Minor-fixar appliceras. Re-review rekommenderas om Major-1 eller Major-2 kontrover (Klas avgör pragmatiskt).

---

**Slut på review.**

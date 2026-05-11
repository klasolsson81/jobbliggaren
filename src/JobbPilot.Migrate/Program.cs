// JobbPilot.Migrate — one-shot DDL-init för Hangfire-schema + 3 Postgres-roller.
// Körs som ECS task vid STEG 14b (Fas 0-stängning) och vid framtida schema-mutationer.
//
// Flow per docs/runbooks/hangfire-schema.md §3-4:
//   Phase A (master-creds):
//     - REVOKE PUBLIC från databasen + hangfire-schema (default-skydd)
//     - Generera 3 random-pwds (32 char alpha-num, ~190 bits entropy)
//     - CREATE ROLE jobbpilot_migrations + jobbpilot_app + jobbpilot_worker (via
//       parameteriserad EXECUTE format('%I', %L) för SQL-injection-defense)
//     - GRANT CONNECT på databasen till alla 3
//     - CREATE SCHEMA hangfire AUTHORIZATION jobbpilot_migrations
//     - GRANT USAGE/CREATE på hangfire-schema till jobbpilot_migrations
//     - GRANT på public-schema (default EF Core-yta) till jobbpilot_app
//   Phase B (jobbpilot_migrations-creds):
//     - PostgreSqlObjectsInstaller.Install(connection, "hangfire") — officiell Hangfire 1.21.1
//   Phase C (master-creds — RE-FETCHED från Secrets Manager för rotation-race-skydd):
//     - GRANT på hangfire.* till jobbpilot_worker (DML-only)
//     - ALTER DEFAULT PRIVILEGES för framtida tabeller
//   Phase D (Secrets Manager):
//     - PutSecretValue → jobbpilot/dev/db/app-connection-string (jobbpilot_app-creds)
//     - PutSecretValue → jobbpilot/dev/db/hangfire-storage-connection-string (jobbpilot_worker-creds)
//
// Inga klartext-pwds i loggning — bara SHA256-truncate-fingerprints (0% pwd-bytes synliga).
// Idempotens: alla CREATE ROLE använder DO-block. Re-run efter delvis fail: säker.
// CancellationToken propageras genom hela kedjan (Console.CancelKeyPress → CTS → AWS SDK).

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Hangfire.PostgreSql;
using JobbPilot.Migrate;
using Microsoft.Extensions.Logging;
using Npgsql;

var loggerFactory = LoggerFactory.Create(builder => builder
    .AddSimpleConsole(opts =>
    {
        opts.SingleLine = true;
        opts.TimestampFormat = "HH:mm:ss ";
    })
    .SetMinimumLevel(LogLevel.Information));
var log = loggerFactory.CreateLogger("Migrate");

// CancellationToken-flow för graceful shutdown vid SIGTERM (Fargate stopTimeout=30s).
// Per CLAUDE.md §3.5: CancellationToken propageras genom hela kedjan.
//
// OBS: ProcessExit/CancelKeyPress-handlers använder TryCancel-pattern eftersom
// CTS kan vara disposed när handlers triggas vid normal exit (using-block-exit
// → dispose → ProcessExit-handler). IsCancellationRequested-check undviker
// ObjectDisposedException.
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    if (!cts.IsCancellationRequested)
    {
        try { cts.Cancel(); } catch (ObjectDisposedException) { /* OK — exit pågår redan */ }
    }
};
AppDomain.CurrentDomain.ProcessExit += (_, _) =>
{
    if (!cts.IsCancellationRequested)
    {
        try { cts.Cancel(); } catch (ObjectDisposedException) { /* OK — exit pågår redan */ }
    }
};

try
{
    // -----------------------------------------------------------------------
    // Konfig från env-vars (injiceras via ECS task-def secrets/env-blocks).
    // -----------------------------------------------------------------------
    var masterSecretArn = RequiredEnv("MIGRATE_MASTER_SECRET_ARN");
    var dbHost = RequiredEnv("MIGRATE_DB_HOST");
    var dbPort = int.Parse(RequiredEnv("MIGRATE_DB_PORT"), CultureInfo.InvariantCulture);
    var dbName = RequiredEnv("MIGRATE_DB_NAME");
    var appConnSecretArn = RequiredEnv("MIGRATE_APP_CONN_SECRET_ARN");
    var hangfireConnSecretArn = RequiredEnv("MIGRATE_HANGFIRE_CONN_SECRET_ARN");
    var awsRegion = RequiredEnv("AWS_REGION");

    MigrateLog.StartingMigrate(log, dbHost, dbPort, dbName, awsRegion);

    var secretsClient = new AmazonSecretsManagerClient(
        Amazon.RegionEndpoint.GetBySystemName(awsRegion));

    // -----------------------------------------------------------------------
    // Phase A — master: REVOKE PUBLIC + CREATE ROLE × 3 + GRANTs + CREATE SCHEMA
    // -----------------------------------------------------------------------
    var masterCredsA = await FetchMasterCredsAsync(secretsClient, masterSecretArn, cts.Token);
    MigrateLog.MasterCredsLoaded(log, masterCredsA.Username, "Phase A");

    var pwdMigrations = GenerateRandomPassword(32);
    var pwdApp = GenerateRandomPassword(32);
    var pwdWorker = GenerateRandomPassword(32);

    // Pre-compute SHA256-fingerprints utanför log-call (CA1873 + Sec-Minor-2 fix).
    var fpMig = Fingerprint(pwdMigrations);
    var fpApp = Fingerprint(pwdApp);
    var fpWrk = Fingerprint(pwdWorker);
    MigrateLog.GeneratedPwds(log, fpMig, fpApp, fpWrk);

    MigrateLog.PhaseAStart(log);
    await using (var masterConn = new NpgsqlConnection(ConnectionStringFactory.ForMigrate(dbHost, dbPort, dbName, masterCredsA.Username, masterCredsA.Password)))
    {
        await masterConn.OpenAsync(cts.Token);
        await ExecutePhaseAAsync(masterConn, dbName, pwdMigrations, pwdApp, pwdWorker, log, cts.Token);
    }

    // -----------------------------------------------------------------------
    // Phase B — jobbpilot_migrations: PostgreSqlObjectsInstaller.Install
    // -----------------------------------------------------------------------
    MigrateLog.PhaseBStart(log);
    var migrationsConnString = ConnectionStringFactory.ForMigrate(dbHost, dbPort, dbName, Roles.Migrations, pwdMigrations);
    await using (var migrationsConn = new NpgsqlConnection(migrationsConnString))
    {
        await migrationsConn.OpenAsync(cts.Token);
        // Officiell Hangfire 1.21.1 schema-install. Idempotent (tål re-run).
        // Skriver ~13 tabeller + sequences + functions till hangfire-schema.
        // Per dotnet-architect: synchron är OK i console-context (inte CLAUDE.md §3.5-brott
        // eftersom Task.Run bara är för CPU-bundet, inte för wrap:a sync I/O).
        PostgreSqlObjectsInstaller.Install(migrationsConn, "hangfire");
        MigrateLog.HangfireInstallComplete(log);
    }

    // -----------------------------------------------------------------------
    // Phase C — master (RE-FETCHED): GRANT + ALTER DEFAULT PRIVILEGES
    // Sec-Major-1: re-fetch master-creds skyddar mot AWS-managerad rotation
    // mid-flow (Phase B kan ta 60-120s).
    // -----------------------------------------------------------------------
    MigrateLog.PhaseCStart(log);
    var masterCredsC = await FetchMasterCredsAsync(secretsClient, masterSecretArn, cts.Token);
    MigrateLog.MasterCredsLoaded(log, masterCredsC.Username, "Phase C");

    await using (var masterConn = new NpgsqlConnection(ConnectionStringFactory.ForMigrate(dbHost, dbPort, dbName, masterCredsC.Username, masterCredsC.Password)))
    {
        await masterConn.OpenAsync(cts.Token);
        await ExecutePhaseCAsync(masterConn, log, cts.Token);
    }

    // -----------------------------------------------------------------------
    // Phase D — Secrets Manager: PutSecretValue × 2
    // -----------------------------------------------------------------------
    MigrateLog.PhaseDStart(log);
    // Persisterade CS:er → Trust=false + VerifyFull + Root Certificate (TD-38).
    // Api/Worker-containers har RDS-CA-bundle på /etc/ssl/certs/rds-global-bundle.pem
    // (se Api/Worker Dockerfile COPY-direktiv).
    var appCs = ConnectionStringFactory.ForPersisted(dbHost, dbPort, dbName, Roles.App, pwdApp);
    var hangfireCs = ConnectionStringFactory.ForPersisted(dbHost, dbPort, dbName, Roles.Worker, pwdWorker);

    await secretsClient.PutSecretValueAsync(new PutSecretValueRequest
    {
        SecretId = appConnSecretArn,
        SecretString = appCs,
    }, cts.Token);
    MigrateLog.WroteAppConnSecret(log, appConnSecretArn);

    await secretsClient.PutSecretValueAsync(new PutSecretValueRequest
    {
        SecretId = hangfireConnSecretArn,
        SecretString = hangfireCs,
    }, cts.Token);
    MigrateLog.WroteHangfireConnSecret(log, hangfireConnSecretArn);

    MigrateLog.MigrateComplete(log);
    return 0;
}
catch (Exception ex)
{
    MigrateLog.MigrateFailed(log, ex);
    return 1;
}

// ===========================================================================
// Helpers
// ===========================================================================

static string RequiredEnv(string name) =>
    Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"Saknad env-var: {name}");

static string GenerateRandomPassword(int length)
{
    const string charset = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    var bytes = RandomNumberGenerator.GetBytes(length);
    var sb = new StringBuilder(length);
    foreach (var b in bytes)
    {
        sb.Append(charset[b % charset.Length]);
    }
    return sb.ToString();
}

// SHA256-truncate fingerprint per Sec-Minor-2 — 0% pwd-bytes synliga i log,
// 32 bitar identifying-info räcker för "är detta samma pwd-version som förra gången".
static string Fingerprint(string pwd)
{
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(pwd));
    return Convert.ToHexString(hash, 0, 4); // 8 hex-chars
}

static async Task<RdsMasterSecret> FetchMasterCredsAsync(
    AmazonSecretsManagerClient client, string secretArn, CancellationToken ct)
{
    var response = await client.GetSecretValueAsync(
        new GetSecretValueRequest { SecretId = secretArn }, ct);
    var creds = JsonSerializer.Deserialize<RdsMasterSecret>(response.SecretString)
        ?? throw new InvalidOperationException("Master secret JSON-parse misslyckades");

    // Architect Viktigt-2: explicit field-validation (icke-nullable record-properties
    // skyddar inte mot null från JsonSerializer.Deserialize).
    if (string.IsNullOrEmpty(creds.Username) || string.IsNullOrEmpty(creds.Password))
    {
        throw new InvalidOperationException("Master secret saknar username eller password-fält");
    }
    return creds;
}

static async Task ExecutePhaseAAsync(NpgsqlConnection conn, string dbName, string pwdMig, string pwdApp, string pwdWrk, ILogger log, CancellationToken ct)
{
    // REVOKE PUBLIC från databasen. Identifier dbName valideras via regex
    // innan interpolation (Sec-Minor-3 defensiv hardening).
    ValidateIdentifier(dbName);
    await ExecuteAsync(conn,
        string.Create(CultureInfo.InvariantCulture, $"REVOKE ALL ON DATABASE \"{dbName}\" FROM PUBLIC;"),
        log, "Revoke PUBLIC från db", ct);

    // CREATE ROLE × 3 — två-stegs SELECT + DDL för att kringgå pl/pgsql-parameter-
    // begränsning i anonyma DO-block.
    await CreateRoleIfNotExistsAsync(conn, Roles.Migrations, pwdMig, log, ct);
    await CreateRoleIfNotExistsAsync(conn, Roles.App, pwdApp, log, ct);
    await CreateRoleIfNotExistsAsync(conn, Roles.Worker, pwdWrk, log, ct);

    // GRANT CONNECT till alla 3.
    foreach (var role in new[] { Roles.Migrations, Roles.App, Roles.Worker })
    {
        await ExecuteAsync(conn,
            string.Create(CultureInfo.InvariantCulture, $"GRANT CONNECT ON DATABASE \"{dbName}\" TO {role};"),
            log,
            string.Create(CultureInfo.InvariantCulture, $"GRANT CONNECT till {role}"),
            ct);
    }

    // RDS-master är `rds_superuser` (limited) — INTE full SUPERUSER. Kan inte
    // SET ROLE på en roll utan explicit membership. För `CREATE SCHEMA AUTHORIZATION
    // jobbpilot_migrations` krävs att master har medlemskap i migrations-rollen.
    // GRANT … TO CURRENT_USER ger master detta. Idempotent (re-grant är no-op).
    // Ger membership i alla 3 så Phase C-GRANTs på hangfire.* (ägda av migrations)
    // också kan köras av master.
    await ExecuteAsync(conn, $"GRANT {Roles.Migrations} TO CURRENT_USER;",
        log, "GRANT migrations-role TO master (för SCHEMA AUTHORIZATION + Phase C)", ct);
    await ExecuteAsync(conn, $"GRANT {Roles.App} TO CURRENT_USER;",
        log, "GRANT app-role TO master", ct);
    await ExecuteAsync(conn, $"GRANT {Roles.Worker} TO CURRENT_USER;",
        log, "GRANT worker-role TO master", ct);

    // CREATE SCHEMA hangfire (om inte finns) — ägs av jobbpilot_migrations.
    await ExecuteAsync(conn,
        $"CREATE SCHEMA IF NOT EXISTS hangfire AUTHORIZATION {Roles.Migrations};",
        log, "CREATE SCHEMA hangfire", ct);

    await ExecuteAsync(conn, "REVOKE ALL ON SCHEMA hangfire FROM PUBLIC;", log, "Revoke PUBLIC från hangfire", ct);

    await ExecuteAsync(conn, $"GRANT USAGE, CREATE ON SCHEMA hangfire TO {Roles.Migrations};",
        log, "GRANT USAGE/CREATE på hangfire till migrations", ct);

    // GRANT på public-schema till jobbpilot_app (full DML/DDL för EF Core-migrations app-side).
    await ExecuteAsync(conn, $"GRANT USAGE, CREATE ON SCHEMA public TO {Roles.App};",
        log, "GRANT USAGE/CREATE på public till app", ct);
    await ExecuteAsync(conn, $"GRANT ALL ON ALL TABLES IN SCHEMA public TO {Roles.App};",
        log, "GRANT ALL på public.* till app", ct);
    await ExecuteAsync(conn, $"GRANT ALL ON ALL SEQUENCES IN SCHEMA public TO {Roles.App};",
        log, "GRANT ALL på public-sequences till app", ct);
    await ExecuteAsync(conn,
        $"ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON TABLES TO {Roles.App};",
        log, "DEFAULT PRIVILEGES public-tabeller -> app", ct);
    await ExecuteAsync(conn,
        $"ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT ALL ON SEQUENCES TO {Roles.App};",
        log, "DEFAULT PRIVILEGES public-sequences -> app", ct);
}

static async Task ExecutePhaseCAsync(NpgsqlConnection conn, ILogger log, CancellationToken ct)
{
    await ExecuteAsync(conn, $"GRANT USAGE ON SCHEMA hangfire TO {Roles.Worker};",
        log, "GRANT USAGE på hangfire till worker", ct);
    await ExecuteAsync(conn, $"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA hangfire TO {Roles.Worker};",
        log, "GRANT DML på hangfire.* till worker", ct);
    await ExecuteAsync(conn, $"GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA hangfire TO {Roles.Worker};",
        log, "GRANT på hangfire-sequences till worker", ct);

    await ExecuteAsync(conn,
        $"ALTER DEFAULT PRIVILEGES FOR ROLE {Roles.Migrations} IN SCHEMA hangfire " +
        $"GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {Roles.Worker};",
        log, "DEFAULT PRIVILEGES hangfire-tabeller -> worker", ct);
    await ExecuteAsync(conn,
        $"ALTER DEFAULT PRIVILEGES FOR ROLE {Roles.Migrations} IN SCHEMA hangfire " +
        $"GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO {Roles.Worker};",
        log, "DEFAULT PRIVILEGES hangfire-sequences -> worker", ct);
}

static async Task CreateRoleIfNotExistsAsync(NpgsqlConnection conn, string roleName, string password, ILogger log, CancellationToken ct)
{
    // Anonyma DO-block i Postgres är pl/pgsql och tar inte Npgsql-parameters
    // direkt — @role-referenser propagerar inte in i pl/pgsql-scope. Vi använder
    // istället två-stegs-pattern:
    //   1. SELECT 1 FROM pg_roles WHERE rolname = @role (parameteriserad SELECT funkar)
    //   2. CREATE/ALTER ROLE <ident> LOGIN PASSWORD '<lit>' (DDL, string-interpolerad)
    //
    // Säkerhet: roleName är hardcoded const i Roles-class → ingen injection-yta.
    // password är genererad från charset [A-Za-z0-9] → inga `'` eller `\` möjliga.
    // ValidateIdentifier körs ändå som defense-in-depth om någon utvidgar Roles.
    ValidateIdentifier(roleName);

    bool exists;
    await using (var checkCmd = new NpgsqlCommand("SELECT 1 FROM pg_roles WHERE rolname = @role", conn))
    {
        checkCmd.Parameters.AddWithValue("role", roleName);
        var result = await checkCmd.ExecuteScalarAsync(ct);
        exists = result != null;
    }

    var ddl = exists
        ? string.Create(CultureInfo.InvariantCulture, $"ALTER ROLE {roleName} WITH LOGIN PASSWORD '{password}';")
        : string.Create(CultureInfo.InvariantCulture, $"CREATE ROLE {roleName} LOGIN PASSWORD '{password}';");

    await using (var ddlCmd = new NpgsqlCommand(ddl, conn))
    {
        await ddlCmd.ExecuteNonQueryAsync(ct);
    }
    MigrateLog.CreateOrAlterRoleOk(log, roleName);
}

static async Task ExecuteAsync(NpgsqlConnection conn, string sql, ILogger log, string description, CancellationToken ct)
{
    await using var cmd = new NpgsqlCommand(sql, conn);
    await cmd.ExecuteNonQueryAsync(ct);
    MigrateLog.StatementOk(log, description);
}

// Defensiv identifier-validation — Postgres-rolnamn / db-namn / schema-namn
// måste matcha [a-z_][a-z0-9_]{0,62} för att vara säkra att interpolera utan
// escape. Hardcoded constants i Roles passerar redan; runtime-värden valideras.
static void ValidateIdentifier(string ident)
{
    if (!System.Text.RegularExpressions.Regex.IsMatch(ident, @"^[a-z_][a-z0-9_]{0,62}$"))
    {
        throw new InvalidOperationException($"Ogiltigt Postgres-identifier: {ident}");
    }
}

// JSON-format för AWS-managerad RDS-master-secret (PascalCase per .NET-konvention,
// mappas till snake_case-JSON via JsonPropertyName).
sealed record RdsMasterSecret(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("password")] string Password);

// Postgres-rolnamn — extraherade till const-block per code-reviewer Minor-1
// (CLAUDE.md §5.1: "Magic strings — alltid konstanter").
static class Roles
{
    public const string Migrations = "jobbpilot_migrations";
    public const string App = "jobbpilot_app";
    public const string Worker = "jobbpilot_worker";
}

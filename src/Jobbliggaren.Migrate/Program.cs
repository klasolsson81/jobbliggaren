// Jobbliggaren.Migrate — one-shot ECS-task för schema-arbete mot AWS RDS.
//
// CLI-dispatch (ADR 0033 — Jobbliggaren.Migrate CLI-mode-dispatch):
//
//   Jobbliggaren.Migrate init     -> Phase A-D (engångs-init eller creds-rotation)
//   Jobbliggaren.Migrate schema   -> Phase E (EF Core Database.MigrateAsync)
//
// Saknad arg eller okänd arg -> exit 1 med usage-text.
//
// Phase A-D (init-mode) per docs/runbooks/hangfire-schema.md §3-4:
//   Phase A (master-creds):
//     - REVOKE PUBLIC från databasen + hangfire-schema (default-skydd)
//     - Generera 3 random-pwds (32 char alpha-num, ~190 bits entropy)
//     - CREATE ROLE jobbliggaren_migrations + jobbliggaren_app + jobbliggaren_worker (via
//       parameteriserad EXECUTE format('%I', %L) för SQL-injection-defense)
//     - GRANT CONNECT på databasen till alla 3
//     - CREATE SCHEMA hangfire AUTHORIZATION jobbliggaren_migrations
//     - GRANT USAGE/CREATE på hangfire-schema till jobbliggaren_migrations
//     - GRANT på public-schema (default EF Core-yta) till jobbliggaren_app
//   Phase B (jobbliggaren_migrations-creds):
//     - PostgreSqlObjectsInstaller.Install(connection, "hangfire") — officiell Hangfire 1.21.1
//   Phase C (master-creds — RE-FETCHED från Secrets Manager för rotation-race-skydd):
//     - GRANT på hangfire.* till jobbliggaren_worker (DML-only)
//     - ALTER DEFAULT PRIVILEGES för framtida tabeller
//   Phase D (Secrets Manager):
//     - PutSecretValue → jobbliggaren/dev/db/app-connection-string (jobbliggaren_app-creds)
//     - PutSecretValue → jobbliggaren/dev/db/hangfire-storage-connection-string (jobbliggaren_worker-creds)
//
// Phase E (schema-mode) per ADR 0033:
//   - Hämta jobbliggaren_app-CS från Secrets Manager (MIGRATE_APP_CONN_SECRET_ARN)
//   - Bygg AppDbContext via DbContextOptionsBuilder + UseNpgsql + UseSnakeCaseNamingConvention
//   - GetPendingMigrationsAsync -> logga pending
//   - Database.MigrateAsync (idempotent — re-run efter completed är no-op)
//
// Inga klartext-pwds i loggning — bara SHA256-truncate-fingerprints (0% pwd-bytes synliga).
// Idempotens: alla CREATE ROLE använder DO-block. Re-run efter delvis fail: säker.
// CancellationToken propageras genom hela kedjan (Console.CancelKeyPress → CTS → AWS SDK).

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Hangfire.PostgreSql;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Migrate;
using Microsoft.EntityFrameworkCore;
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

// ADR 0033 — CLI-dispatch. Default-less. Saknad/okänd arg -> exit 1.
// ADR 0034 amendment 2026-05-12 — `bootstrap`-mode för Identity-context-deploy
// (master-creds, separate från `schema` som kör AppDbContext med jobbliggaren_app).
var mode = args.Length == 1 ? args[0] : null;

try
{
    return mode switch
    {
        "init" => await RunInitAsync(log, cts.Token),
        "bootstrap" => await RunBootstrapAsync(log, cts.Token),
        "ensure-extensions" => await RunEnsureExtensionsAsync(log, cts.Token),
        "explain-search" => await RunExplainSearchAsync(log, cts.Token),
        "schema" => await RunSchemaAsync(log, cts.Token),
        _ => UsageError(log),
    };
}
catch (Exception ex)
{
    MigrateLog.MigrateFailed(log, ex);
    return 1;
}

// ===========================================================================
// Mode-dispatch helpers (ADR 0033)
// ===========================================================================

static int UsageError(ILogger log)
{
    MigrateLog.UsageError(log);
    return 1;
}

static async Task<int> RunInitAsync(ILogger log, CancellationToken ct)
{
    MigrateLog.ModeInit(log);

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
    var masterCredsA = await FetchMasterCredsAsync(secretsClient, masterSecretArn, ct);
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
        await masterConn.OpenAsync(ct);
        await ExecutePhaseAAsync(masterConn, dbName, pwdMigrations, pwdApp, pwdWorker, log, ct);
    }

    // -----------------------------------------------------------------------
    // Phase B — jobbliggaren_migrations: PostgreSqlObjectsInstaller.Install
    // -----------------------------------------------------------------------
    MigrateLog.PhaseBStart(log);
    var migrationsConnString = ConnectionStringFactory.ForMigrate(dbHost, dbPort, dbName, Roles.Migrations, pwdMigrations);
    await using (var migrationsConn = new NpgsqlConnection(migrationsConnString))
    {
        await migrationsConn.OpenAsync(ct);
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
    var masterCredsC = await FetchMasterCredsAsync(secretsClient, masterSecretArn, ct);
    MigrateLog.MasterCredsLoaded(log, masterCredsC.Username, "Phase C");

    await using (var masterConn = new NpgsqlConnection(ConnectionStringFactory.ForMigrate(dbHost, dbPort, dbName, masterCredsC.Username, masterCredsC.Password)))
    {
        await masterConn.OpenAsync(ct);
        await ExecutePhaseCAsync(masterConn, log, ct);
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
    }, ct);
    MigrateLog.WroteAppConnSecret(log, appConnSecretArn);

    await secretsClient.PutSecretValueAsync(new PutSecretValueRequest
    {
        SecretId = hangfireConnSecretArn,
        SecretString = hangfireCs,
    }, ct);
    MigrateLog.WroteHangfireConnSecret(log, hangfireConnSecretArn);

    MigrateLog.MigrateComplete(log);
    return 0;
}

// ADR 0033 — Phase E. Ansluter med jobbliggaren_app-creds från Secrets Manager,
// bygger AppDbContext programmatiskt, kör Database.MigrateAsync. Idempotent.
static async Task<int> RunSchemaAsync(ILogger log, CancellationToken ct)
{
    MigrateLog.ModeSchema(log);

    var appConnSecretArn = RequiredEnv("MIGRATE_APP_CONN_SECRET_ARN");
    var awsRegion = RequiredEnv("AWS_REGION");

    var secretsClient = new AmazonSecretsManagerClient(
        Amazon.RegionEndpoint.GetBySystemName(awsRegion));

    var appCsResponse = await secretsClient.GetSecretValueAsync(
        new GetSecretValueRequest { SecretId = appConnSecretArn }, ct);
    if (string.IsNullOrWhiteSpace(appCsResponse.SecretString))
    {
        throw new InvalidOperationException(
            $"App connection-string secret är tom: {appConnSecretArn}");
    }

    MigrateLog.PhaseEStart(log);

    await using var dbContext = new AppDbContext(
        MigrationsOptionsFactory.BuildAppOptions(appCsResponse.SecretString));

    var pending = (await dbContext.Database.GetPendingMigrationsAsync(ct)).ToList();
    MigrateLog.PendingMigrationsCount(log, pending.Count);

    if (pending.Count == 0)
    {
        MigrateLog.PhaseENoPending(log);
        return 0;
    }

    foreach (var migration in pending)
    {
        MigrateLog.PendingMigrationItem(log, migration);
    }

    await dbContext.Database.MigrateAsync(ct);
    MigrateLog.PhaseEComplete(log, pending.Count);
    return 0;
}

// ADR 0034 — Phase Bootstrap. Ansluter med master-creds, skapar identity-schema
// + grantar jobbliggaren_app DML/DDL på identity, applicerar Identity-migrations
// (AppIdentityDbContext) med master-creds. Engångs eller vid Identity-schema-
// ändring (sällsynt). Schema-mode kvarstår oförändrad (AppDbContext only).
// TD-71 — efter permanent A5-deploy revoke CREATE ON DATABASE från jobbliggaren_app.
static async Task<int> RunBootstrapAsync(ILogger log, CancellationToken ct)
{
    MigrateLog.ModeBootstrap(log);

    var masterSecretArn = RequiredEnv("MIGRATE_MASTER_SECRET_ARN");
    var dbHost = RequiredEnv("MIGRATE_DB_HOST");
    var dbPort = int.Parse(RequiredEnv("MIGRATE_DB_PORT"), CultureInfo.InvariantCulture);
    var dbName = RequiredEnv("MIGRATE_DB_NAME");
    var awsRegion = RequiredEnv("AWS_REGION");

    MigrateLog.StartingMigrate(log, dbHost, dbPort, dbName, awsRegion);

    var secretsClient = new AmazonSecretsManagerClient(
        Amazon.RegionEndpoint.GetBySystemName(awsRegion));

    var masterCreds = await FetchMasterCredsAsync(secretsClient, masterSecretArn, ct);
    MigrateLog.MasterCredsLoaded(log, masterCreds.Username, "Bootstrap");

    // Step 1: SQL via master-creds — skapa identity-schema + GRANTs.
    // Idempotent (CREATE SCHEMA IF NOT EXISTS, GRANT är no-op om redan satta).
    MigrateLog.BootstrapStep1Start(log);
    await using (var masterConn = new NpgsqlConnection(
        ConnectionStringFactory.ForMigrate(dbHost, dbPort, dbName, masterCreds.Username, masterCreds.Password)))
    {
        await masterConn.OpenAsync(ct);
        await ExecuteBootstrapSchemaAsync(masterConn, dbName, log, ct);
    }

    // Step 2: Applicera Identity-migrations med master-creds (har CREATE ON DATABASE,
    // kan köra MigrateAsync utan Npgsql #1770-permission-fel).
    // Re-fetch master-creds — samma rotation-race-skydd som init Phase A/C
    // (Sec-Major-2 från security-auditor 2026-05-12 audit). MigrateAsync kan ta
    // 30-120s vid pending Identity-migrations + AWS Secrets Manager kan rotera
    // mid-flow → cached creds från Step 1 kan bli stale.
    MigrateLog.BootstrapStep2Start(log);
    var masterCredsStep2 = await FetchMasterCredsAsync(secretsClient, masterSecretArn, ct);
    MigrateLog.MasterCredsLoaded(log, masterCredsStep2.Username, "Bootstrap Step 2");

    var masterCs = ConnectionStringFactory.ForMigrate(dbHost, dbPort, dbName,
        masterCredsStep2.Username, masterCredsStep2.Password);

    await using var identityContext = new AppIdentityDbContext(
        MigrationsOptionsFactory.BuildIdentityOptions(masterCs));

    var pending = (await identityContext.Database.GetPendingMigrationsAsync(ct)).ToList();
    MigrateLog.PendingMigrationsCount(log, pending.Count);

    if (pending.Count > 0)
    {
        foreach (var migration in pending)
        {
            MigrateLog.PendingMigrationItem(log, migration);
        }
        await identityContext.Database.MigrateAsync(ct);
        MigrateLog.BootstrapStep2Complete(log, pending.Count);
    }
    else
    {
        MigrateLog.BootstrapStep2NoPending(log);
    }

    MigrateLog.BootstrapComplete(log);
    return 0;
}

// F6 P4 (2026-05-20) — separate mode för PostgreSQL extensions som kräver
// master-roll. ADR 0033-mönster: extensions tillhör Phase A-domänen (master-
// privileged DDL), inte Phase E (jobbliggaren_app DDL). Per TD-71 har
// jobbliggaren_app inte CREATE-privilege på databasen → kan inte köra
// CREATE EXTENSION själv. Detta mode är idempotent (CREATE EXTENSION IF NOT
// EXISTS) och säkert att re-köra vid varje deploy (no-op när extension finns).
//
// Triggeras före schema-mode i deploy-dev.yml. Master-creds hämtas på samma
// sätt som init/bootstrap (FetchMasterCredsAsync via Secrets Manager).
static async Task<int> RunEnsureExtensionsAsync(ILogger log, CancellationToken ct)
{
    MigrateLog.ModeEnsureExtensions(log);

    var masterSecretArn = RequiredEnv("MIGRATE_MASTER_SECRET_ARN");
    var dbHost = RequiredEnv("MIGRATE_DB_HOST");
    var dbPort = int.Parse(RequiredEnv("MIGRATE_DB_PORT"), CultureInfo.InvariantCulture);
    var dbName = RequiredEnv("MIGRATE_DB_NAME");
    var awsRegion = RequiredEnv("AWS_REGION");

    MigrateLog.StartingMigrate(log, dbHost, dbPort, dbName, awsRegion);

    var secretsClient = new AmazonSecretsManagerClient(
        Amazon.RegionEndpoint.GetBySystemName(awsRegion));

    var masterCreds = await FetchMasterCredsAsync(secretsClient, masterSecretArn, ct);
    MigrateLog.MasterCredsLoaded(log, masterCreds.Username, "EnsureExtensions");

    MigrateLog.EnsureExtensionsStart(log);
    await using (var masterConn = new NpgsqlConnection(
        ConnectionStringFactory.ForMigrate(dbHost, dbPort, dbName, masterCreds.Username, masterCreds.Password)))
    {
        await masterConn.OpenAsync(ct);

        // ADR 0061 Mekanik-not (F6 P4 2026-05-20) — pg_trgm krävs av
        // F6P4aJobAdTrigramIndexes-migrationen (GIN-trigram-acceleration på
        // lower(title)+lower(description)). Trusted extension på AWS RDS PG 16+,
        // men kräver CREATE-privilege på databasen → master-roll.
        await ExecuteAsync(masterConn, "CREATE EXTENSION IF NOT EXISTS pg_trgm;",
            log, "CREATE EXTENSION pg_trgm (idempotent)", ct);
    }

    MigrateLog.EnsureExtensionsComplete(log);
    return 0;
}

// F6 P4 (2026-05-21) — diagnostik-mode för sök-perf. Kör EXPLAIN (ANALYZE,
// BUFFERS) på q-search-filtret (ListJobAds COUNT- + ITEMS-väg) för en
// uppsättning söktermer och loggar query-planen. Read-only — ingen schema-/
// data-ändring. Idempotent. Ansluter med app-creds (samma roll/planner-
// kontext som runtime-queryn). Termer via env-var EXPLAIN_SEARCH_TERMS
// (komma-separerad); default "lärare,systemutvecklare" (tidigare långsam vs
// snabb referens).
//
// ADR 0062 — speglar nu FTS-hybrid-filtret (search_vector @@
// websearch_to_tsquery('swedish', term) OR lower(title) LIKE '%term%'),
// inte den gamla trigram-LIKE-vägen. Post-deploy-verifiering: planen ska
// visa Bitmap Index Scan på ix_job_ads_search_vector och INGA de-TOAST:ade
// description-läsningar (den tidigare trigram-rotorsaken, ADR 0061).
static async Task<int> RunExplainSearchAsync(ILogger log, CancellationToken ct)
{
    MigrateLog.ModeExplainSearch(log);

    var appConnSecretArn = RequiredEnv("MIGRATE_APP_CONN_SECRET_ARN");
    var awsRegion = RequiredEnv("AWS_REGION");

    var terms = (Environment.GetEnvironmentVariable("EXPLAIN_SEARCH_TERMS")
                 ?? "lärare,systemutvecklare")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    var secretsClient = new AmazonSecretsManagerClient(
        Amazon.RegionEndpoint.GetBySystemName(awsRegion));
    var appCsResponse = await secretsClient.GetSecretValueAsync(
        new GetSecretValueRequest { SecretId = appConnSecretArn }, ct);
    if (string.IsNullOrWhiteSpace(appCsResponse.SecretString))
        throw new InvalidOperationException($"App connection-string secret är tom: {appConnSecretArn}");

    await using var conn = new NpgsqlConnection(appCsResponse.SecretString);
    await conn.OpenAsync(ct);

    foreach (var term in terms)
    {
        // Speglar JobAdSearchQuery.ApplyCriteria q-FTS-hybrid-grenen + global
        // query filter (deleted_at IS NULL). COUNT-vägen — representativ för
        // filter-kostnaden. description-LIKE körs INTE längre (ADR 0062).
        const string countSql =
            "EXPLAIN (ANALYZE, BUFFERS) SELECT count(*) FROM job_ads "
            + "WHERE deleted_at IS NULL "
            + "AND (search_vector @@ websearch_to_tsquery('swedish', @term) "
            + "OR lower(title) LIKE @p);";
        await ExplainAndLogAsync(conn, countSql, term, "COUNT", log, ct);

        // Items-vägen — filter + ORDER BY published_at DESC (default-sort) + LIMIT.
        const string itemsSql =
            "EXPLAIN (ANALYZE, BUFFERS) SELECT id FROM job_ads "
            + "WHERE deleted_at IS NULL "
            + "AND (search_vector @@ websearch_to_tsquery('swedish', @term) "
            + "OR lower(title) LIKE @p) "
            + "ORDER BY published_at DESC, id LIMIT 5;";
        await ExplainAndLogAsync(conn, itemsSql, term, "ITEMS", log, ct);
    }

    return 0;
}

static async Task ExplainAndLogAsync(
    NpgsqlConnection conn, string explainSql, string term, string variant,
    ILogger log, CancellationToken ct)
{
    await using var cmd = new NpgsqlCommand(explainSql, conn);
    // @term: rå sökterm till websearch_to_tsquery (sköter egen normalisering).
    // @p: lowercased %term%-pattern till title-LIKE-fallbacken (ADR 0062).
    cmd.Parameters.Add(new NpgsqlParameter("term", term));
    cmd.Parameters.Add(new NpgsqlParameter("p", "%" + term.ToLowerInvariant() + "%"));
    var plan = new StringBuilder();
    await using (var reader = await cmd.ExecuteReaderAsync(ct))
    {
        while (await reader.ReadAsync(ct))
            plan.AppendLine(reader.GetString(0));
    }
    // CA1873-suppress: diagnostik-mode loggar alltid (Information garanterat
    // aktivt i denna engångskörning) — IsEnabled-guard vore meningslös här.
#pragma warning disable CA1873
    MigrateLog.ExplainSearchResult(log, term, variant, plan.ToString());
#pragma warning restore CA1873
}

static async Task ExecuteBootstrapSchemaAsync(NpgsqlConnection conn, string dbName, ILogger log, CancellationToken ct)
{
    ValidateIdentifier(dbName);

    // 1. Skapa identity-schema ägt av jobbliggaren_migrations (samma pattern som hangfire).
    await ExecuteAsync(conn,
        $"CREATE SCHEMA IF NOT EXISTS identity AUTHORIZATION {Roles.Migrations};",
        log, "CREATE SCHEMA identity AUTHORIZATION migrations", ct);

    await ExecuteAsync(conn, "REVOKE ALL ON SCHEMA identity FROM PUBLIC;",
        log, "Revoke PUBLIC från identity", ct);

    // 2. GRANT jobbliggaren_app full DML+DDL på identity (samma pattern som public).
    await ExecuteAsync(conn, $"GRANT USAGE, CREATE ON SCHEMA identity TO {Roles.App};",
        log, "GRANT USAGE/CREATE på identity till app", ct);
    await ExecuteAsync(conn, $"GRANT ALL ON ALL TABLES IN SCHEMA identity TO {Roles.App};",
        log, "GRANT ALL på identity-tabeller till app", ct);
    await ExecuteAsync(conn, $"GRANT ALL ON ALL SEQUENCES IN SCHEMA identity TO {Roles.App};",
        log, "GRANT ALL på identity-sequences till app", ct);
    await ExecuteAsync(conn,
        $"ALTER DEFAULT PRIVILEGES IN SCHEMA identity GRANT ALL ON TABLES TO {Roles.App};",
        log, "DEFAULT PRIVILEGES identity-tabeller -> app", ct);
    await ExecuteAsync(conn,
        $"ALTER DEFAULT PRIVILEGES IN SCHEMA identity GRANT ALL ON SEQUENCES TO {Roles.App};",
        log, "DEFAULT PRIVILEGES identity-sequences -> app", ct);
}

// ===========================================================================
// Helpers (delas mellan init-, bootstrap- och schema-modes)
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
    // jobbliggaren_migrations` krävs att master har medlemskap i migrations-rollen.
    // GRANT … TO CURRENT_USER ger master detta. Idempotent (re-grant är no-op).
    // Ger membership i alla 3 så Phase C-GRANTs på hangfire.* (ägda av migrations)
    // också kan köras av master.
    await ExecuteAsync(conn, $"GRANT {Roles.Migrations} TO CURRENT_USER;",
        log, "GRANT migrations-role TO master (för SCHEMA AUTHORIZATION + Phase C)", ct);
    await ExecuteAsync(conn, $"GRANT {Roles.App} TO CURRENT_USER;",
        log, "GRANT app-role TO master", ct);
    await ExecuteAsync(conn, $"GRANT {Roles.Worker} TO CURRENT_USER;",
        log, "GRANT worker-role TO master", ct);

    // CREATE SCHEMA hangfire (om inte finns) — ägs av jobbliggaren_migrations.
    await ExecuteAsync(conn,
        $"CREATE SCHEMA IF NOT EXISTS hangfire AUTHORIZATION {Roles.Migrations};",
        log, "CREATE SCHEMA hangfire", ct);

    await ExecuteAsync(conn, "REVOKE ALL ON SCHEMA hangfire FROM PUBLIC;", log, "Revoke PUBLIC från hangfire", ct);

    await ExecuteAsync(conn, $"GRANT USAGE, CREATE ON SCHEMA hangfire TO {Roles.Migrations};",
        log, "GRANT USAGE/CREATE på hangfire till migrations", ct);

    // GRANT på public-schema till jobbliggaren_app (full DML/DDL för EF Core-migrations app-side).
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

    // ADR 0034 — identity-schema för AppIdentityDbContext (HasDefaultSchema("identity")).
    // Skapas i init så nästa init-körning garanterar att schemat finns med korrekta
    // GRANTs. Identity-migrations appliceras separat via `bootstrap`-mode med master-creds.
    await ExecuteAsync(conn,
        $"CREATE SCHEMA IF NOT EXISTS identity AUTHORIZATION {Roles.Migrations};",
        log, "CREATE SCHEMA identity (ADR 0034)", ct);
    await ExecuteAsync(conn, "REVOKE ALL ON SCHEMA identity FROM PUBLIC;",
        log, "Revoke PUBLIC från identity", ct);
    await ExecuteAsync(conn, $"GRANT USAGE, CREATE ON SCHEMA identity TO {Roles.App};",
        log, "GRANT USAGE/CREATE på identity till app", ct);
    await ExecuteAsync(conn, $"GRANT ALL ON ALL TABLES IN SCHEMA identity TO {Roles.App};",
        log, "GRANT ALL på identity-tabeller till app", ct);
    await ExecuteAsync(conn, $"GRANT ALL ON ALL SEQUENCES IN SCHEMA identity TO {Roles.App};",
        log, "GRANT ALL på identity-sequences till app", ct);
    await ExecuteAsync(conn,
        $"ALTER DEFAULT PRIVILEGES IN SCHEMA identity GRANT ALL ON TABLES TO {Roles.App};",
        log, "DEFAULT PRIVILEGES identity-tabeller -> app", ct);
    await ExecuteAsync(conn,
        $"ALTER DEFAULT PRIVILEGES IN SCHEMA identity GRANT ALL ON SEQUENCES TO {Roles.App};",
        log, "DEFAULT PRIVILEGES identity-sequences -> app", ct);
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


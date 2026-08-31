// Jobbliggaren.Migrate — one-shot DDL-/schema-container för Postgres.
//
// VPS-portabel efter AWS-exit (TD-105 / #199 / ADR 0050 / ADR 0066): Migrate
// hämtar INTE längre creds via AWS Secrets Manager. Alla hemligheter och
// connection-strings kommer ur miljön (env-vars eller Docker-secret `_FILE`,
// se MigrateEnv). Kör som oneshot i Hetzner-Compose-stacken (#196/TD-106):
//   docker compose run --rm migrate ensure-extensions && ... schema
//
// CLI-dispatch (ADR 0033 — Jobbliggaren.Migrate CLI-mode-dispatch):
//
//   Jobbliggaren.Migrate init     -> Phase A-C (engångs-init mot operatör-givna creds)
//   Jobbliggaren.Migrate bootstrap-> Identity-schema + Identity-migrations (master-creds)
//   Jobbliggaren.Migrate ensure-extensions -> CREATE EXTENSION (master-creds)
//   Jobbliggaren.Migrate schema   -> Phase E (EF Core Database.MigrateAsync, app-creds)
//   Jobbliggaren.Migrate explain-search -> diagnostik (EXPLAIN ANALYZE, app-creds)
//
// Saknad arg eller okänd arg -> exit 1 med usage-text.
//
// Phase A-C (init-mode) per docs/runbooks/hangfire-schema.md §3-4:
//   Phase A (master-creds): REVOKE PUBLIC, CREATE ROLE jobbliggaren_{migrations,app,worker}
//     med OPERATÖR-GIVNA lösenord (env), GRANT CONNECT, CREATE SCHEMA hangfire/identity,
//     GRANTs på public/identity till jobbliggaren_app.
//   Phase B (jobbliggaren_migrations-creds): PostgreSqlObjectsInstaller.Install(hangfire).
//   Phase C (master-creds): GRANT hangfire.* till jobbliggaren_worker (DML-only) +
//     ALTER DEFAULT PRIVILEGES.
//
// Skillnad mot AWS-eran (TD-105): Migrate genererar INTE längre roll-lösenord och
// skriver INGA connection-strings tillbaka till en secret-sink (det gamla Phase D /
// Secrets Manager PutSecretValue är borttaget). Operatören pre-provisionerar de tre
// roll-lösenorden; Api/Worker får sina connection-strings via samma miljö-mekanism
// (#196 äger var de fysiskt lagras på VPS:en).
//
// Phase E (schema-mode) per ADR 0033:
//   - Läs jobbliggaren_app-connection-string ur env (MIGRATE_APP_CONNECTION_STRING)
//   - Bygg AppDbContext via DbContextOptionsBuilder + UseNpgsql + UseSnakeCaseNamingConvention
//   - GetPendingMigrationsAsync -> logga pending
//   - Database.MigrateAsync (idempotent — re-run efter completed är no-op)
//
// Inga klartext-secrets i loggning. Idempotens: alla CREATE ROLE använder två-stegs
// SELECT+DDL. Re-run efter delvis fail: säker. CancellationToken propageras genom
// hela kedjan (Console.CancelKeyPress -> CTS).

using System.Globalization;
using System.Text;
using Hangfire.PostgreSql;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Security;
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

// CancellationToken-flow för graceful shutdown vid SIGTERM (oneshot-container).
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
        "rewrap-master-key" => await RunRewrapMasterKeyAsync(log, cts.Token),
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

    var db = ReadDbTarget();
    var master = ReadMasterCreds();

    // Operatör-provisionerade roll-lösenord. Migrate genererar dem INTE (TD-105 /
    // CTO-bind C): på en single-box VPS finns ingen åtkomst-kontrollerad secret-sink
    // att skriva genererade creds till, så creds-livscykeln ägs av deploy/ops (#196 /
    // TD-102). Init blir ren idempotent DDL mot operatör-givna lösenord.
    var pwdMigrations = MigrateEnv.Required("MIGRATE_MIGRATIONS_PASSWORD");
    var pwdApp = MigrateEnv.Required("MIGRATE_APP_PASSWORD");
    var pwdWorker = MigrateEnv.Required("MIGRATE_WORKER_PASSWORD");

    MigrateLog.StartingMigrate(log, db.Host, db.Port, db.Database);

    // -----------------------------------------------------------------------
    // Phase A — master: REVOKE PUBLIC + CREATE ROLE × 3 + GRANTs + CREATE SCHEMA
    // -----------------------------------------------------------------------
    MigrateLog.MasterCredsLoaded(log, master.Username, "Phase A");
    MigrateLog.PhaseAStart(log);
    await using (var masterConn = new NpgsqlConnection(BuildConnString(db, master.Username, master.Password)))
    {
        await masterConn.OpenAsync(ct);
        await ExecutePhaseAAsync(masterConn, db.Database, pwdMigrations, pwdApp, pwdWorker, log, ct);
    }

    // -----------------------------------------------------------------------
    // Phase B — jobbliggaren_migrations: PostgreSqlObjectsInstaller.Install
    // -----------------------------------------------------------------------
    MigrateLog.PhaseBStart(log);
    var migrationsConnString = BuildConnString(db, Roles.Migrations, pwdMigrations);
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
    // Phase C — master: GRANT hangfire.* till worker + ALTER DEFAULT PRIVILEGES.
    // Ingen re-fetch av master-creds (env-creds roterar inte mid-run, till skillnad
    // från det gamla AWS-managed-rotation-flödet).
    // -----------------------------------------------------------------------
    MigrateLog.PhaseCStart(log);
    await using (var masterConn = new NpgsqlConnection(BuildConnString(db, master.Username, master.Password)))
    {
        await masterConn.OpenAsync(ct);
        await ExecutePhaseCAsync(masterConn, log, ct);
    }

    MigrateLog.MigrateComplete(log);
    return 0;
}

// ADR 0033 — Phase E. Ansluter med jobbliggaren_app-creds ur env,
// bygger AppDbContext programmatiskt, kör Database.MigrateAsync. Idempotent.
// #198 gate M-3 — offline master-key rotation.
//
// WHY IT LIVES HERE. Migrate has no DI container and builds AppDbContext through
// MigrationsOptionsFactory, i.e. WITHOUT the field-encryption interceptors. That is exactly what
// this operation needs: raw access to user_data_keys, no audit side-effects, and no DEK-bearing
// aggregate materialised in a system job. It also already resolves secrets through MigrateEnv,
// which supports the <NAME>_FILE convention the box uses.
//
// The dispatch takes exactly one argument (ADR 0033), so there is no --verify-only flag; the
// verification pass is part of the run and its result is logged.
static async Task<int> RunRewrapMasterKeyAsync(ILogger log, CancellationToken ct)
{
    MigrateLog.ModeRewrapMasterKey(log);

    var appCs = MigrateEnv.Required("MIGRATE_APP_CONNECTION_STRING");
    var retiringKey = MigrateEnv.Required("REWRAP_RETIRING_MASTER_KEY");
    var incomingKey = MigrateEnv.Required("REWRAP_INCOMING_MASTER_KEY");
    // Required, not Optional-with-a-default. Three of four inputs are already required, and this
    // is the one where a wrong guess is a data-loss path: on a rotated box the on-disk identity
    // file already holds the INCOMING id, so there is no on-box source for the retiring one. The
    // runbook always passes it explicitly, so nothing breaks.
    var retiringKeyId = MigrateEnv.Required("REWRAP_RETIRING_KEY_ID");
    var incomingKeyId = MigrateEnv.Required("REWRAP_INCOMING_KEY_ID");

    MigrateLog.RewrapStart(log, retiringKeyId, incomingKeyId);

    // Both providers are constructed by hand — Migrate has no DI. Using the real provider rather
    // than re-implementing the crypto is the point: same AAD, same layout, same fail-closed
    // guards as production, because it is the same code.
    var rewrapper = new MasterKeyRewrapper(
        NewProvider(retiringKey, retiringKeyId),
        NewProvider(incomingKey, incomingKeyId),
        retiringKeyId,
        incomingKeyId,
        log);

    await using var dbContext = new AppDbContext(
        MigrationsOptionsFactory.BuildAppOptions(appCs));

    var result = await rewrapper.RewrapAllAsync(dbContext, ct);

    if (result.Rewrapped == 0)
    {
        MigrateLog.RewrapNoOp(log, retiringKeyId, result.AlreadyCurrent, incomingKeyId, result.Verified);
        return 0;
    }

    MigrateLog.RewrapComplete(log, result.Rewrapped, result.AlreadyCurrent, result.Verified);
    return 0;

    static LocalDataKeyProvider NewProvider(string masterKeyBase64, string keyId) =>
        new(
            Microsoft.Extensions.Options.Options.Create(new FieldEncryptionOptions
            {
                Provider = "Local",
                LocalMasterKeyBase64 = masterKeyBase64,
                LocalMasterKeyId = keyId,
            }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LocalDataKeyProvider>.Instance);
}

static async Task<int> RunSchemaAsync(ILogger log, CancellationToken ct)
{
    MigrateLog.ModeSchema(log);

    var appCs = MigrateEnv.Required("MIGRATE_APP_CONNECTION_STRING");
    // Raw on purpose — SchemaAheadGate is the ONE normalizer for this value (compose renders an
    // unset `${MIGRATE_ALLOW_SCHEMA_AHEAD:-}` as an empty string, and the gate reads that as
    // unset). Not MigrateEnv: the value is migration IDs, not a secret, so the `_FILE` seam
    // would add a second normalizer and buy nothing.
    var overrideRaw = Environment.GetEnvironmentVariable(SchemaAheadGate.OverrideVariableName);

    MigrateLog.PhaseEStart(log);

    await using var dbContext = new AppDbContext(
        MigrationsOptionsFactory.BuildAppOptions(appCs));

    // #1236 — the schema-ahead gate runs BEFORE any pending-count early return: a pure
    // backwards pin IS "zero pending", so returning early on that count would skip the one
    // state this gate exists for. Pending is derived client-side from the same two reads
    // (the substrate test pins equivalence with EF's GetPendingMigrationsAsync).
    var applied = await dbContext.Database.GetAppliedMigrationsAsync(ct);
    var assembly = dbContext.Database.GetMigrations();
    var decision = SchemaAheadGate.Decide([.. applied], [.. assembly], overrideRaw);

    var unknownIds = string.Join(",", decision.Unknown);
    switch (decision.Verdict)
    {
        case SchemaAheadVerdict.RefuseSchemaAhead:
            MigrateLog.RefusedSchemaAhead(log, decision.Unknown.Count, unknownIds);
            if (decision.OverrideKeyMissing)
            {
                MigrateLog.OverrideKeyNotForwarded(log);
            }
            else if (decision.OverrideProvided)
            {
                MigrateLog.RefusedOverrideMismatch(log, overrideRaw!, unknownIds);
            }

            return SchemaAheadGate.ExitRefusedSchemaAhead;

        case SchemaAheadVerdict.RefuseDivergence:
            MigrateLog.RefusedDivergence(
                log,
                decision.Unknown.Count, unknownIds,
                decision.Pending.Count, string.Join(",", decision.Pending));
            return SchemaAheadGate.ExitRefusedDivergence;

        case SchemaAheadVerdict.OverriddenNoOp:
            MigrateLog.OverrideConsumed(log, unknownIds);
            return 0;

        case SchemaAheadVerdict.Proceed:
            break;

        default:
            // A verdict added later must choose its behaviour explicitly — falling through to
            // the apply would be the OPEN direction in a fail-closed control. The throw lands
            // in the outer catch: migrate's existing exit-1 crash path.
            throw new InvalidOperationException($"Unhandled schema-ahead verdict: {decision.Verdict}");
    }

    if (decision.OverridePresentButIdle)
    {
        MigrateLog.OverrideIdle(log);
    }

    var pending = decision.Pending;
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

    // A writer landing between the gate's read and this apply cannot cause a wrong apply:
    // MigrateAsync recomputes applied/pending internally under EF's migration lock.
    await dbContext.Database.MigrateAsync(ct);
    MigrateLog.PhaseEComplete(log, pending.Count);
    return 0;
}

// ADR 0034 — Phase Bootstrap. Ansluter med master-creds, skapar identity-schema
// + grantar jobbliggaren_app DML/DDL på identity, applicerar Identity-migrations
// (AppIdentityDbContext) med master-creds. Engångs eller vid Identity-schema-
// ändring (sällsynt). Schema-mode kvarstår oförändrad (AppDbContext only).
// TD-71 — efter permanent deploy revoke CREATE ON DATABASE från jobbliggaren_app.
static async Task<int> RunBootstrapAsync(ILogger log, CancellationToken ct)
{
    MigrateLog.ModeBootstrap(log);

    var db = ReadDbTarget();
    var master = ReadMasterCreds();

    MigrateLog.StartingMigrate(log, db.Host, db.Port, db.Database);
    MigrateLog.MasterCredsLoaded(log, master.Username, "Bootstrap");

    var masterCs = BuildConnString(db, master.Username, master.Password);

    // Step 1: SQL via master-creds — skapa identity-schema + GRANTs.
    // Idempotent (CREATE SCHEMA IF NOT EXISTS, GRANT är no-op om redan satta).
    MigrateLog.BootstrapStep1Start(log);
    await using (var masterConn = new NpgsqlConnection(masterCs))
    {
        await masterConn.OpenAsync(ct);
        await ExecuteBootstrapSchemaAsync(masterConn, db.Database, log, ct);
    }

    // Step 2: Applicera Identity-migrations med master-creds (har CREATE ON DATABASE,
    // kan köra MigrateAsync utan Npgsql #1770-permission-fel). Samma masterCs som
    // Step 1 — env-creds roterar inte mid-run (det gamla rotation-race-re-fetchet
    // var en AWS-Secrets-Manager-artefakt och behövs inte längre).
    MigrateLog.BootstrapStep2Start(log);
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
// Triggeras före schema-mode i oneshot-migrate-sekvensen. Master-creds läses
// ur env (samma som init/bootstrap).
static async Task<int> RunEnsureExtensionsAsync(ILogger log, CancellationToken ct)
{
    MigrateLog.ModeEnsureExtensions(log);

    var db = ReadDbTarget();
    var master = ReadMasterCreds();

    MigrateLog.StartingMigrate(log, db.Host, db.Port, db.Database);
    MigrateLog.MasterCredsLoaded(log, master.Username, "EnsureExtensions");

    MigrateLog.EnsureExtensionsStart(log);
    await using (var masterConn = new NpgsqlConnection(BuildConnString(db, master.Username, master.Password)))
    {
        await masterConn.OpenAsync(ct);

        // ADR 0061 Mekanik-not (F6 P4 2026-05-20) — pg_trgm krävs av
        // F6P4aJobAdTrigramIndexes-migrationen (GIN-trigram-acceleration på
        // lower(title)+lower(description)). Trusted extension på PG 16+,
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
// ADR 0062 (+ amendment 2026-08-30, #1546) — speglar FTS-hybrid-filtret, nu
// TREAXLAT: search_vector @@ websearch_to_tsquery('swedish', term)
// OR lower(title) LIKE '%term%' OR lower(company_name) LIKE '%term%'.
// Substrängsarmarna gatas på term.Length >= 3, exakt som produktionen gatar dem
// (JobAdSearchComposition: includeSubstringLike) — ett trigram-index kan inte
// serva en kortare LIKE, så ett verktyg som emitterade dem ändå hade förklarat
// en plan produktionen aldrig kör. Post-deploy-verifiering: planen ska
// visa Bitmap Index Scan på ix_job_ads_search_vector och INGA de-TOAST:ade
// description-läsningar (den tidigare trigram-rotorsaken, ADR 0061).
static async Task<int> RunExplainSearchAsync(ILogger log, CancellationToken ct)
{
    MigrateLog.ModeExplainSearch(log);

    var appCs = MigrateEnv.Required("MIGRATE_APP_CONNECTION_STRING");

    var terms = (Environment.GetEnvironmentVariable("EXPLAIN_SEARCH_TERMS")
                 ?? "lärare,systemutvecklare")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    await using var conn = new NpgsqlConnection(appCs);
    await conn.OpenAsync(ct);

    foreach (var term in terms)
    {
        // Speglar JobAdSearchQuery.ApplyCriteria q-FTS-hybrid-grenen. COUNT-vägen —
        // representativ för filter-kostnaden. description-LIKE körs INTE längre (ADR 0062).
        //
        // #821 — TVÅ rättelser, båda i sanningsriktningen. (1) `deleted_at IS NULL` är
        // borta: kolumnen är retirerad (den saknade writer, filtret var vakuöst).
        // (2) `status = 'Active'` är TILLAGT — och det saknades redan FÖRE #821: SPOT-
        // filtret i JobAdSearchComposition.ApplyFilter har emitterat det sedan ADR 0032-
        // amendment 2026-05-23, men detta instrument speglade aldrig ändringen. Ett
        // EXPLAIN-verktyg som inte kör produktionens fråga ljuger — och det ljög i den
        // BETRYGGANDE riktningen (en billigare plan än den verkliga).
        // Se (2): ett EXPLAIN-verktyg som inte kör produktionens fråga ljuger. Samma krav
        // gäller grinden, inte bara armarna.
        var substringArms = term.Length >= 3
            ? "OR lower(title) LIKE @p OR lower(company_name) LIKE @p"
            : string.Empty;

        var countSql =
            "EXPLAIN (ANALYZE, BUFFERS) SELECT count(*) FROM job_ads "
            + "WHERE status = 'Active' "
            + "AND (search_vector @@ websearch_to_tsquery('swedish', @term) "
            + substringArms + ");";
        await ExplainAndLogAsync(conn, countSql, term, "COUNT", log, ct);

        // Items-vägen — filter + ORDER BY published_at DESC (default-sort) + LIMIT.
        var itemsSql =
            "EXPLAIN (ANALYZE, BUFFERS) SELECT id FROM job_ads "
            + "WHERE status = 'Active' "
            + "AND (search_vector @@ websearch_to_tsquery('swedish', @term) "
            + substringArms + ") "
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
    // @p: lowercased %term%-pattern till substrängs-fallbackarna (title + company_name,
    // ADR 0062 + amendment 2026-08-30). Oanvänd när term.Length < 3 — Npgsql skickar bara
    // parametrar som faktiskt förekommer i den omskrivna SQL:en.
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

    // The SAME list `init` issues in Phase A. These seven statements used to be written out
    // here a second time, byte-identically, differing only in the operator description on the
    // first line — so a repair to the identity posture could land in one copy and not the
    // other, silently. `bootstrap` runs under master credentials and `init` may not have run
    // against this database yet, so issuing them here is not redundant; issuing a SECOND COPY
    // of them was.
    foreach (var statement in PhaseASchemaGrants.IdentitySchema)
    {
        await ExecuteAsync(conn, statement.Sql, log, statement.Description, ct);
    }
}

// ===========================================================================
// Helpers (delas mellan init-, bootstrap- och schema-modes)
// ===========================================================================

// DB-mål + TLS-postur ur miljön. SSL-läget är konfig-drivet (MIGRATE_SSL_MODE,
// default "Require") så den faktiska VPS-topologin sätts av #196 (privat
// Docker-bridge utan TLS → "Disable", eller intern CA → "VerifyFull").
static (string Host, int Port, string Database, string SslMode, string? RootCert) ReadDbTarget()
{
    var host = MigrateEnv.Required("MIGRATE_DB_HOST");
    var port = int.Parse(MigrateEnv.Required("MIGRATE_DB_PORT"), CultureInfo.InvariantCulture);
    var database = MigrateEnv.Required("MIGRATE_DB_NAME");
    var sslMode = MigrateEnv.Optional("MIGRATE_SSL_MODE", "Require");
    // Cert-path (ej en hemlighet) — direkt env, valfri (krävs bara för VerifyCA/VerifyFull).
    var rootCert = Environment.GetEnvironmentVariable("MIGRATE_SSL_ROOT_CERT");
    return (host, port, database, sslMode, string.IsNullOrWhiteSpace(rootCert) ? null : rootCert);
}

static (string Username, string Password) ReadMasterCreds() =>
    (MigrateEnv.Required("MIGRATE_MASTER_USERNAME"), MigrateEnv.Required("MIGRATE_MASTER_PASSWORD"));

static string BuildConnString(
    (string Host, int Port, string Database, string SslMode, string? RootCert) db,
    string user, string pwd) =>
    ConnectionStringFactory.Build(db.Host, db.Port, db.Database, user, pwd, db.SslMode, db.RootCert);

static async Task ExecutePhaseAAsync(NpgsqlConnection conn, string dbName, string pwdMig, string pwdApp, string pwdWrk, ILogger log, CancellationToken ct)
{
    // REVOKE PUBLIC från databasen. Identifier dbName valideras via regex
    // innan interpolation (Sec-Minor-3 defensiv hardening).
    ValidateIdentifier(dbName);
    // CREATE ROLE × 3 — två-stegs SELECT + DDL för att kringgå pl/pgsql-parameter-
    // begränsning i anonyma DO-block. Måste ligga före GRANTs: en roll kan inte beviljas
    // något innan den finns.
    await CreateRoleIfNotExistsAsync(conn, Roles.Migrations, pwdMig, log, ct);
    await CreateRoleIfNotExistsAsync(conn, Roles.App, pwdApp, log, ct);
    await CreateRoleIfNotExistsAsync(conn, Roles.Worker, pwdWrk, log, ct);

    // The database-level privilege model lives in PhaseADatabaseGrants so it has a reader:
    // as a bare sequence of awaits it was unreachable from any test assembly, and deleting a
    // grant left every suite green (#196).
    foreach (var statement in PhaseADatabaseGrants.For(dbName))
    {
        await ExecuteAsync(conn, statement.Sql, log, statement.Description, ct);
    }

    // För `CREATE SCHEMA AUTHORIZATION jobbliggaren_migrations` krävs att master har
    // medlemskap i migrations-rollen (master kan vara en begränsad superuser utan
    // implicit SET ROLE). GRANT … TO CURRENT_USER ger master detta. Idempotent
    // (re-grant är no-op). Ger membership i alla 3 så Phase C-GRANTs på hangfire.*
    // (ägda av migrations) också kan köras av master.
    await ExecuteAsync(conn, $"GRANT {Roles.Migrations} TO CURRENT_USER;",
        log, "GRANT migrations-role TO master (för SCHEMA AUTHORIZATION + Phase C)", ct);
    await ExecuteAsync(conn, $"GRANT {Roles.App} TO CURRENT_USER;",
        log, "GRANT app-role TO master", ct);
    await ExecuteAsync(conn, $"GRANT {Roles.Worker} TO CURRENT_USER;",
        log, "GRANT worker-role TO master", ct);

    // The schema-level privilege model lives in PhaseASchemaGrants for the same reason the
    // database-level one lives in PhaseADatabaseGrants: as bare awaits it was unreachable from
    // any test assembly, so the migration oracle could not see the 42501 the `public` block
    // repairs (#1232). `identity` is the same list `bootstrap` mode issues — one property, two
    // callers, instead of two byte-identical copies that a repair could land in half of.
    foreach (var statement in PhaseASchemaGrants.HangfireSchema
        .Concat(PhaseASchemaGrants.PublicSchema)
        .Concat(PhaseASchemaGrants.IdentitySchema))
    {
        await ExecuteAsync(conn, statement.Sql, log, statement.Description, ct);
    }
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
    // password är operatör-givet → escapas genom att fördubbla enkel-citationstecken
    // (Postgres string-literal-escaping) innan interpolation. Förutsätter
    // standard_conforming_strings=on (PG-default sedan 9.1) så `\` är literal och
    // endast `'` behöver fördubblas. ValidateIdentifier körs på roleName som
    // defense-in-depth om någon utvidgar Roles.
    ValidateIdentifier(roleName);

    bool exists;
    await using (var checkCmd = new NpgsqlCommand("SELECT 1 FROM pg_roles WHERE rolname = @role", conn))
    {
        checkCmd.Parameters.AddWithValue("role", roleName);
        var result = await checkCmd.ExecuteScalarAsync(ct);
        exists = result != null;
    }

    // Escapa enkel-citationstecken i lösenordet (operatör-givet kan innehålla `'`).
    var escapedPwd = password.Replace("'", "''", StringComparison.Ordinal);
    var ddl = exists
        ? string.Create(CultureInfo.InvariantCulture, $"ALTER ROLE {roleName} WITH LOGIN PASSWORD '{escapedPwd}';")
        : string.Create(CultureInfo.InvariantCulture, $"CREATE ROLE {roleName} LOGIN PASSWORD '{escapedPwd}';");

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
static void ValidateIdentifier(string ident) => PostgresIdentifier.Validate(ident);

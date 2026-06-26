using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Migrate;

// LoggerMessage source-gen per repo-konvention (CA1848). Top-level Program.cs
// anropar dessa istället för LogInformation/LogError direkt.
internal static partial class MigrateLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Starting Migrate: host={Host}:{Port} db={Db}")]
    public static partial void StartingMigrate(ILogger logger, string host, int port, string db);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information,
        Message = "Master creds loaded ({Phase}): user={User}")]
    public static partial void MasterCredsLoaded(ILogger logger, string user, string phase);

    [LoggerMessage(EventId = 10, Level = LogLevel.Information,
        Message = "Phase A: REVOKE PUBLIC + CREATE ROLE x 3 + GRANTs + CREATE SCHEMA hangfire")]
    public static partial void PhaseAStart(ILogger logger);

    [LoggerMessage(EventId = 20, Level = LogLevel.Information,
        Message = "Phase B: Hangfire schema-install (PostgreSqlObjectsInstaller)")]
    public static partial void PhaseBStart(ILogger logger);

    [LoggerMessage(EventId = 21, Level = LogLevel.Information,
        Message = "Hangfire schema-install COMPLETE")]
    public static partial void HangfireInstallComplete(ILogger logger);

    [LoggerMessage(EventId = 30, Level = LogLevel.Information,
        Message = "Phase C: GRANT hangfire.* till jobbliggaren_worker + ALTER DEFAULT PRIVILEGES")]
    public static partial void PhaseCStart(ILogger logger);

    [LoggerMessage(EventId = 50, Level = LogLevel.Information,
        Message = "Migrate COMPLETE — Worker kan nu (re)startas")]
    public static partial void MigrateComplete(ILogger logger);

    [LoggerMessage(EventId = 100, Level = LogLevel.Information,
        Message = "CREATE/ALTER ROLE {Role} OK")]
    public static partial void CreateOrAlterRoleOk(ILogger logger, string role);

    [LoggerMessage(EventId = 101, Level = LogLevel.Information,
        Message = "OK: {Description}")]
    public static partial void StatementOk(ILogger logger, string description);

    [LoggerMessage(EventId = 999, Level = LogLevel.Error,
        Message = "Migrate FAILED")]
    public static partial void MigrateFailed(ILogger logger, Exception ex);

    // ADR 0033 — Phase E (EF Core MigrateAsync) + CLI-dispatch
    [LoggerMessage(EventId = 60, Level = LogLevel.Information,
        Message = "Phase E: EF Core Database.MigrateAsync mot AppDbContext (jobbliggaren_app-creds)")]
    public static partial void PhaseEStart(ILogger logger);

    [LoggerMessage(EventId = 61, Level = LogLevel.Information,
        Message = "Pending migrations: {Count}")]
    public static partial void PendingMigrationsCount(ILogger logger, int count);

    [LoggerMessage(EventId = 62, Level = LogLevel.Information,
        Message = "  -> {Migration}")]
    public static partial void PendingMigrationItem(ILogger logger, string migration);

    [LoggerMessage(EventId = 63, Level = LogLevel.Information,
        Message = "Phase E COMPLETE — applied {Count} migration(s)")]
    public static partial void PhaseEComplete(ILogger logger, int count);

    [LoggerMessage(EventId = 64, Level = LogLevel.Information,
        Message = "Phase E: no pending migrations — schema is up-to-date")]
    public static partial void PhaseENoPending(ILogger logger);

    [LoggerMessage(EventId = 200, Level = LogLevel.Information,
        Message = "Mode: init (Phase A-C — idempotent DDL mot operatör-givna creds)")]
    public static partial void ModeInit(ILogger logger);

    [LoggerMessage(EventId = 201, Level = LogLevel.Information,
        Message = "Mode: schema (Phase E — EF Core MigrateAsync)")]
    public static partial void ModeSchema(ILogger logger);

    [LoggerMessage(EventId = 202, Level = LogLevel.Error,
        Message = "Usage: Jobbliggaren.Migrate <init|bootstrap|ensure-extensions|schema|explain-search>")]
    public static partial void UsageError(ILogger logger);

    // F6 P4 (2026-05-20) — ensure-extensions-mode (master-creds, PG extensions)
    [LoggerMessage(EventId = 204, Level = LogLevel.Information,
        Message = "Mode: ensure-extensions (master-creds, CREATE EXTENSION IF NOT EXISTS)")]
    public static partial void ModeEnsureExtensions(ILogger logger);

    [LoggerMessage(EventId = 80, Level = LogLevel.Information,
        Message = "EnsureExtensions: säkerställer PostgreSQL extensions (idempotent, master-roll)")]
    public static partial void EnsureExtensionsStart(ILogger logger);

    [LoggerMessage(EventId = 81, Level = LogLevel.Information,
        Message = "EnsureExtensions: complete (idempotent — no-op om extensions redan finns)")]
    public static partial void EnsureExtensionsComplete(ILogger logger);

    // F6 P4 (2026-05-21) — explain-search diagnostik-mode (sök-perf-rotorsak)
    [LoggerMessage(EventId = 205, Level = LogLevel.Information,
        Message = "Mode: explain-search (EXPLAIN ANALYZE på q-search-filter — sök-perf-diagnos)")]
    public static partial void ModeExplainSearch(ILogger logger);

    [LoggerMessage(EventId = 82, Level = LogLevel.Information,
        Message = "EXPLAIN ANALYZE [term={Term}, variant={Variant}]:\n{Plan}")]
    public static partial void ExplainSearchResult(ILogger logger, string term, string variant, string plan);

    // ADR 0034 — Bootstrap-mode (Identity-context via master-creds)
    [LoggerMessage(EventId = 203, Level = LogLevel.Information,
        Message = "Mode: bootstrap (Identity-context via master-creds — ADR 0034)")]
    public static partial void ModeBootstrap(ILogger logger);

    [LoggerMessage(EventId = 70, Level = LogLevel.Information,
        Message = "Bootstrap Step 1: CREATE SCHEMA identity + GRANTs på identity till jobbliggaren_app")]
    public static partial void BootstrapStep1Start(ILogger logger);

    [LoggerMessage(EventId = 71, Level = LogLevel.Information,
        Message = "Bootstrap Step 2: EF Core Database.MigrateAsync mot AppIdentityDbContext (master-creds)")]
    public static partial void BootstrapStep2Start(ILogger logger);

    [LoggerMessage(EventId = 72, Level = LogLevel.Information,
        Message = "Bootstrap Step 2 COMPLETE — applied {Count} Identity-migration(s)")]
    public static partial void BootstrapStep2Complete(ILogger logger, int count);

    [LoggerMessage(EventId = 73, Level = LogLevel.Information,
        Message = "Bootstrap Step 2: no pending Identity-migrations")]
    public static partial void BootstrapStep2NoPending(ILogger logger);

    [LoggerMessage(EventId = 74, Level = LogLevel.Information,
        Message = "Bootstrap COMPLETE — identity-schema klar + Identity-migrations applied")]
    public static partial void BootstrapComplete(ILogger logger);
}

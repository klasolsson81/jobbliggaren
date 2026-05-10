using Microsoft.Extensions.Logging;

namespace JobbPilot.Migrate;

// LoggerMessage source-gen per repo-konvention (CA1848). Top-level Program.cs
// anropar dessa istället för LogInformation/LogError direkt.
internal static partial class MigrateLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Starting Migrate: host={Host}:{Port} db={Db} region={Region}")]
    public static partial void StartingMigrate(ILogger logger, string host, int port, string db, string region);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information,
        Message = "Master creds loaded ({Phase}): user={User}")]
    public static partial void MasterCredsLoaded(ILogger logger, string user, string phase);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information,
        Message = "Generated 3 role pwds: migrations={MigPrint} app={AppPrint} worker={WrkPrint}")]
    public static partial void GeneratedPwds(ILogger logger, string migPrint, string appPrint, string wrkPrint);

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
        Message = "Phase C: GRANT hangfire.* till jobbpilot_worker + ALTER DEFAULT PRIVILEGES")]
    public static partial void PhaseCStart(ILogger logger);

    [LoggerMessage(EventId = 40, Level = LogLevel.Information,
        Message = "Phase D: skriver slutliga connection-strings till Secrets Manager")]
    public static partial void PhaseDStart(ILogger logger);

    [LoggerMessage(EventId = 41, Level = LogLevel.Information,
        Message = "Wrote app connection-string to {Arn}")]
    public static partial void WroteAppConnSecret(ILogger logger, string arn);

    [LoggerMessage(EventId = 42, Level = LogLevel.Information,
        Message = "Wrote hangfire connection-string to {Arn}")]
    public static partial void WroteHangfireConnSecret(ILogger logger, string arn);

    [LoggerMessage(EventId = 50, Level = LogLevel.Information,
        Message = "Migrate COMPLETE — Worker kan nu force-new-deployment")]
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
}

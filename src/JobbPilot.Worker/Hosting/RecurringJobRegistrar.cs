using Hangfire;
using JobbPilot.Application.Applications.Jobs.GhostedDetection;
using JobbPilot.Application.Auth.Jobs.HardDeleteAccounts;
using JobbPilot.Application.Common.Auditing.Jobs.AuditLogRetention;
using Microsoft.Extensions.Hosting;

namespace JobbPilot.Worker.Hosting;

/// <summary>
/// Registrerar Hangfire <see cref="RecurringJob"/>:s vid Worker-host start.
/// Idempotent — <see cref="IRecurringJobManager.AddOrUpdate{T}(string, System.Linq.Expressions.Expression{System.Action{T}}, string, RecurringJobOptions)"/>
/// kan köras flera gånger utan biverkningar.
///
/// Cron-tider är UTC (Hangfire-default). Schedule:
///   03:00 UTC — audit-log-retention + detect-ghosted (samtidiga)
///   04:00 UTC — hard-delete-accounts (1h efter retention)
///
/// Samkörning vid 03:00 UTC mellan retention och detect-ghosted är säker:
/// retention gör atomisk DDL på audit_log (CREATE/DROP partition, &lt; 100ms
/// typiskt), detect-ghosted gör DML på applications + skriver audit-rader.
/// Olika tabeller — PG-locking sker per tabell, ingen kontention.
///
/// Hard-delete separeras till 04:00 UTC (per ADR 0024 D6) eftersom det rör
/// SAMMA tabell som retention (audit_log UPDATE via IAuditTrailEraser vs
/// retention DROP/CREATE partition). 1h-padding ger retention tid att
/// färdigställa DDL innan hard-delete börjar UPDATE:a. Sannolikt onödigt
/// (DDL är &lt; 100ms) men matchar ADR-spec och ger ops-marginal.
///
/// 03:00 UTC motsvarar svensk natt (04:00 vintertid / 05:00 sommartid) —
/// lägst belastning på dev-DB och ingen konflikt med interaktiv användning.
/// </summary>
public sealed class RecurringJobRegistrar(IRecurringJobManager manager) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        manager.AddOrUpdate<AuditLogRetentionJob>(
            "audit-log-retention",
            job => job.RunAsync(CancellationToken.None),
            Cron.Daily(3));

        manager.AddOrUpdate<DetectGhostedApplicationsJob>(
            "detect-ghosted",
            job => job.RunAsync(CancellationToken.None),
            Cron.Daily(3));

        manager.AddOrUpdate<HardDeleteAccountsJob>(
            "hard-delete-accounts",
            job => job.RunAsync(CancellationToken.None),
            Cron.Daily(4));

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

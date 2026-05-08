using Hangfire;
using JobbPilot.Application.Applications.Jobs.GhostedDetection;
using JobbPilot.Application.Common.Auditing.Jobs.AuditLogRetention;
using Microsoft.Extensions.Hosting;

namespace JobbPilot.Worker.Hosting;

/// <summary>
/// Registrerar Hangfire <see cref="RecurringJob"/>:s vid Worker-host start.
/// Idempotent — <see cref="IRecurringJobManager.AddOrUpdate{T}(string, System.Linq.Expressions.Expression{System.Action{T}}, string, RecurringJobOptions)"/>
/// kan köras flera gånger utan biverkningar.
///
/// Cron-tider är UTC (Hangfire-default). Båda jobben kör 03:00 UTC dagligen.
/// Samkörning är säker: audit-retention gör atomisk DDL (CREATE/DROP partition,
/// &lt; 100ms typiskt), detect-ghosted gör DML på applications (INSERT av audit-
/// rader). PG-låsning sker per tabell — ingen kontention mellan jobben.
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

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

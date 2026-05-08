using JobbPilot.Application.Applications.Commands.MarkGhosted;
using JobbPilot.Application.Applications.Specifications;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JobbPilot.Application.Applications.Jobs.GhostedDetection;

/// <summary>
/// Schemalagt orchestrator-jobb som identifierar stale Application-aggregat
/// (per <see cref="StaleApplicationSpecification"/>) och dispatch:ar
/// <see cref="MarkGhostedCommand"/> per app via Mediator. Audit-paritet bevaras —
/// en audit-rad per ghosted application via <c>AuditBehavior</c> (per ADR 0022).
///
/// Registreras som Hangfire <c>RecurringJob</c> i Worker (BUILD.md §16.2:
/// <c>DetectGhostedApplicationsJob</c>, daglig 03:00 UTC). Idempotent — kan köras
/// flera gånger utan biverkningar (<c>MarkGhostedCommand</c>-handler är idempotent).
/// </summary>
public sealed partial class DetectGhostedApplicationsJob(
    IAppDbContext db,
    IMediator mediator,
    IDateTimeProvider clock,
    ILogger<DetectGhostedApplicationsJob> logger)
{
    private const int ProgressLogEvery = 25;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        // Tvådelat filter (per arch-rapport Plan B): SQL-snävning via Status-filter
        // (utnyttjar partial-index ix_applications_stale_detection), sedan client-side
        // per-app-threshold-check över ett litet kandidat-set. AsNoTracking — varje
        // MarkGhostedCommand laddar och muterar sin egen instans via UoW.
        var candidates = await db.Applications
            .AsNoTracking()
            .Where(StaleApplicationSpecification.CandidateStatusFilter())
            .Select(a => new { Id = a.Id.Value, a.LastStatusChangeAt, a.GhostedThresholdDays })
            .ToListAsync(cancellationToken);

        var staleIds = candidates
            .Where(c => StaleApplicationSpecification.IsStaleNow(c.LastStatusChangeAt, c.GhostedThresholdDays, now))
            .Select(c => c.Id)
            .ToList();

        LogStaleFound(logger, staleIds.Count);

        var processed = 0;
        foreach (var id in staleIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await mediator.Send(new MarkGhostedCommand(id), cancellationToken);
            processed++;
            if (processed % ProgressLogEvery == 0)
                LogProgress(logger, processed, staleIds.Count);
        }

        LogComplete(logger, processed);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "DetectGhostedApplicationsJob: hittade {Count} stale applications")]
    private static partial void LogStaleFound(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "DetectGhostedApplicationsJob: {Processed}/{Total} applications behandlade")]
    private static partial void LogProgress(ILogger logger, int processed, int total);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "DetectGhostedApplicationsJob: klart — {Processed} applications markerade ghosted")]
    private static partial void LogComplete(ILogger logger, int processed);
}

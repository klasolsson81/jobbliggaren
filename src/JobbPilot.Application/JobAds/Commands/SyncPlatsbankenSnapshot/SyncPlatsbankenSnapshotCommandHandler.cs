using JobbPilot.Application.JobAds.Jobs.SyncPlatsbanken;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.JobAds.Commands.SyncPlatsbankenSnapshot;

/// <summary>
/// Tunn shim mellan admin-trigger-endpoint och <see cref="SyncPlatsbankenSnapshotJob"/>.
/// Per CTO-rond 2026-05-13 punkt 3: admin-trigger + nattjobb delar samma per-item-
/// Mediator-kodväg via job:t. Refaktor från P8b:s bulk-fetch + in-memory-split
/// till race-säker per-item UpsertExternalJobAdCommand-loop (ADR 0032 §5).
/// </summary>
public sealed class SyncPlatsbankenSnapshotCommandHandler(SyncPlatsbankenSnapshotJob job)
    : ICommandHandler<SyncPlatsbankenSnapshotCommand, Result<SyncPlatsbankenSnapshotResult>>
{
    public async ValueTask<Result<SyncPlatsbankenSnapshotResult>> Handle(
        SyncPlatsbankenSnapshotCommand command, CancellationToken cancellationToken)
    {
        var counts = await job.RunAsync(cancellationToken);

        return Result.Success(new SyncPlatsbankenSnapshotResult(
            FetchedCount: counts.Fetched,
            AddedCount: counts.Added,
            UpdatedCount: counts.Updated,
            SkippedCount: counts.Skipped + counts.Errors,
            StartedAt: counts.StartedAt,
            CompletedAt: counts.CompletedAt));
    }
}

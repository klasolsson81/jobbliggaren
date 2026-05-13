using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JobbPilot.Application.JobAds.Commands.ArchiveExternalJobAd;

/// <summary>
/// Arkivera en extern JobAd vid JobTech-removal-event. <c>JobAd.Archive</c>
/// är idempotent — redan arkiverad ger <see cref="ArchiveOutcome.AlreadyArchived"/>.
/// Saknad JobAd (möjlig tappad stream-event vid skala) ger
/// <see cref="ArchiveOutcome.NotFound"/> snarare än failure — orchestrator
/// räknar i sync-statistik utan att avbryta batchen.
/// </summary>
public sealed partial class ArchiveExternalJobAdCommandHandler(
    IAppDbContext db,
    IDateTimeProvider clock,
    ILogger<ArchiveExternalJobAdCommandHandler> logger)
    : ICommandHandler<ArchiveExternalJobAdCommand, Result<ArchiveOutcome>>
{
    public async ValueTask<Result<ArchiveOutcome>> Handle(
        ArchiveExternalJobAdCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var source = command.Source;
        var externalId = command.ExternalId;
        var jobAd = await db.JobAds
            .Where(j => j.External != null
                        && j.External.Source == source
                        && j.External.ExternalId == externalId)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobAd is null)
        {
            LogNotFound(logger, externalId);
            return Result.Success(ArchiveOutcome.NotFound);
        }

        var archiveResult = jobAd.Archive(clock);
        if (archiveResult.IsFailure)
        {
            // Domain returnerar Failure("JobAd.AlreadyArchived") när redan arkiverad —
            // det är förväntat idempotent-tillstånd, inte ett fel uppåt.
            if (archiveResult.Error.Code == "JobAd.AlreadyArchived")
                return Result.Success(ArchiveOutcome.AlreadyArchived);

            // Annan domain-failure (oväntat) — propagera.
            return Result.Failure<ArchiveOutcome>(archiveResult.Error);
        }

        return Result.Success(ArchiveOutcome.Archived);
    }

    [LoggerMessage(EventId = 5201, Level = LogLevel.Information,
        Message = "ArchiveExternalJobAd: ExternalId={ExternalId} hittas inte — accepteras (möjlig event-tapp eller manual radering).")]
    private static partial void LogNotFound(ILogger logger, string externalId);
}

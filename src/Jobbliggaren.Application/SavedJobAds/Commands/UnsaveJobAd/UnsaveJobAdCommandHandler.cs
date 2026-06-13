using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.SavedJobAds.Commands.UnsaveJobAd;

/// <summary>
/// F6 P5 Punkt 2 Del A — ta bort bokmärke. Idempotent vid redan-borttagen
/// (returnerar Success). Hard-delete paritet RecentJobSearch — bokmärken
/// saknar audit-värdighet utöver audit-pipeline-raden (ADR 0022).
/// </summary>
public sealed class UnsaveJobAdCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : ICommandHandler<UnsaveJobAdCommand, Result>
{
    public async ValueTask<Result> Handle(
        UnsaveJobAdCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure(
                DomainError.Validation("SavedJobAd.Unauthorized", "Användaren är inte autentiserad."));

        var jobSeekerId = await db.JobSeekers
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerId == default)
            return Result.Success(); // No JobSeeker → inget att ta bort

        var jobAdId = new JobAdId(command.JobAdId);

        var saved = await db.SavedJobAds
            .Where(s => s.JobSeekerId == jobSeekerId && s.JobAdId == jobAdId)
            .FirstOrDefaultAsync(cancellationToken);

        if (saved is null)
            return Result.Success(); // Redan borttagen — idempotent

        saved.Unsave(clock.UtcNow);
        db.SavedJobAds.Remove(saved);
        return Result.Success();
    }
}

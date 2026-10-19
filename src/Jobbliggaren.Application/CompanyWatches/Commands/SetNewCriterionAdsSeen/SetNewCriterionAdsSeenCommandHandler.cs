using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.CompanyWatches.Commands.SetNewCriterionAdsSeen;

/// <summary>
/// ADR 0175 D6 — one conditional <c>UPDATE</c>, monotone in the database: it writes only when the stored value is
/// null or earlier, so two racing renders can never move the watermark backward. That is the compare-and-set form
/// of an optimistic lock, and it is why the rule lives in this statement rather than in a method on the aggregate:
/// <c>company_watch_criteria</c> carries no concurrency token (ADR 0175 R-d), and without one a tracked
/// read-modify-write lets the last writer win. It writes no other column; <c>updated_at</c> above all, which the
/// materialisation sweep keys on.
/// </summary>
public sealed class SetNewCriterionAdsSeenCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFailedAccessLogger failedAccessLogger,
    IDateTimeProvider clock)
    : ICommandHandler<SetNewCriterionAdsSeenCommand, Result>
{
    public async ValueTask<Result> Handle(
        SetNewCriterionAdsSeenCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
        {
            return Result.Failure(DomainError.Validation(
                "CompanyWatchCriterion.Unauthorized", "Användaren är inte autentiserad."));
        }

        var userId = currentUser.UserId.Value;
        var criterionId = new CompanyWatchCriterionId(command.CriterionId);

        var owned = await db.CompanyWatchCriteria
            .AsNoTracking()
            .AnyAsync(c => c.Id == criterionId && c.UserId == userId, cancellationToken);
        if (!owned)
        {
            var existsForSomebodyElse = await db.CompanyWatchCriteria
                .AsNoTracking()
                .AnyAsync(c => c.Id == criterionId, cancellationToken);
            if (existsForSomebodyElse)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "CompanyWatchCriterion", command.CriterionId, userId, "SetNewCriterionAdsSeen");
            }

            return Result.Failure(DomainError.NotFound("CompanyWatchCriterion", command.CriterionId));
        }

        if (command.SeenThrough is not { } requested)
        {
            return Result.Failure(DomainError.Validation(
                nameof(SetNewCriterionAdsSeenCommand.SeenThrough), "SeenThrough krävs."));
        }

        // UTC before binding: Npgsql refuses a timestamptz carrying another offset.
        var seenThrough = requested.ToUniversalTime();
        var now = clock.UtcNow;
        if (seenThrough > now)
            seenThrough = now;

        // Zero rows on an owned criterion is a success: it already holds this window or a later one.
        await db.CompanyWatchCriteria
            .Where(c => c.Id == criterionId && c.UserId == userId
                && (c.NewAdsSeenThrough == null || c.NewAdsSeenThrough < seenThrough))
            .ExecuteUpdateAsync(
                set => set.SetProperty(c => c.NewAdsSeenThrough, (DateTimeOffset?)seenThrough),
                cancellationToken);

        return Result.Success();
    }
}

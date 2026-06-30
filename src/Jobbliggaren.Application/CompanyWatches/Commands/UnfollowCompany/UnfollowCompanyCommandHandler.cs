using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.CompanyWatches.Commands.UnfollowCompany;

/// <summary>
/// ADR 0087 D3 — unfollow (soft-delete) a company watch by id, owner-scoped by <c>UserId</c>.
/// Idempotent: an already-unfollowed (soft-deleted) watch owned by the user yields Success (the
/// <c>IgnoreQueryFilters</c> lookup finds it; <see cref="CompanyWatch.SoftDelete"/> no-ops). A watch
/// belonging to another user is indistinguishable to the caller from an unknown id (NotFound), and
/// a cross-user attempt is logged (ADR 0031 failed-access detection).
/// </summary>
public sealed class UnfollowCompanyCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IFailedAccessLogger failedAccessLogger)
    : ICommandHandler<UnfollowCompanyCommand, Result>
{
    public async ValueTask<Result> Handle(
        UnfollowCompanyCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure(
                DomainError.Validation("CompanyWatch.Unauthorized", "Användaren är inte autentiserad."));

        var userId = currentUser.UserId.Value;
        var watchId = new CompanyWatchId(command.CompanyWatchId);

        // IgnoreQueryFilters — find the watch in any soft-delete state so a repeat unfollow is
        // idempotent (the aggregate's SoftDelete no-ops on an already-deleted row).
        var watch = await db.CompanyWatches
            .IgnoreQueryFilters()
            .Where(w => w.Id == watchId && w.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);

        if (watch is null)
        {
            // Failed-access detection (ADR 0031): distinguish an unknown id from a cross-tenant id.
            var existsForOther = await db.CompanyWatches
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(w => w.Id == watchId, cancellationToken);
            if (existsForOther)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "CompanyWatch", watchId.Value, userId, "UnfollowCompany");
            }

            return Result.Failure(DomainError.NotFound("CompanyWatch", command.CompanyWatchId));
        }

        watch.SoftDelete(clock);
        return Result.Success();
    }
}

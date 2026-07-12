using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.CompanyWatches.Commands.SetCompanyWatchFilter;

/// <summary>
/// Bevakning F4a (#803) — sets or clears ONE watch's notification filter, owner-scoped.
///
/// <para>
/// This handler is the ONE place where "an empty selection means clear" is decided (the transport
/// carries a form's shape; the domain carries an invariant that a present spec always narrows).
/// Everything else is delegated: <see cref="WatchFilterSpec.Create"/> owns validation and
/// normalization, and the aggregate owns the soft-delete precondition — we surface the aggregate's
/// own Result rather than pre-judging it here.
/// </para>
///
/// <para>
/// Cross-user isolation mirrors <c>UnfollowCompanyCommandHandler</c> exactly: another user's watch
/// id is indistinguishable from an unknown id (NotFound, never 403 — a 403 would confirm the id
/// exists), and the attempt is logged for failed-access detection (ADR 0031).
/// </para>
/// </summary>
public sealed class SetCompanyWatchFilterCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFailedAccessLogger failedAccessLogger)
    : ICommandHandler<SetCompanyWatchFilterCommand, Result>
{
    public async ValueTask<Result> Handle(
        SetCompanyWatchFilterCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure(
                DomainError.Validation("CompanyWatch.Unauthorized", "Användaren är inte autentiserad."));

        var userId = currentUser.UserId.Value;
        var watchId = new CompanyWatchId(command.CompanyWatchId);

        // IgnoreQueryFilters so an unfollowed watch is FOUND rather than reported as unknown: the
        // owner already knows they unfollowed it, and the aggregate returns the honest domain error
        // (CompanyWatch.NotActive) instead of a misleading 404. Cross-user still yields NotFound.
        var watch = await db.CompanyWatches
            .IgnoreQueryFilters()
            .Where(w => w.Id == watchId && w.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);

        if (watch is null)
        {
            var existsForOther = await db.CompanyWatches
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(w => w.Id == watchId, cancellationToken);
            if (existsForOther)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "CompanyWatch", watchId.Value, userId, "SetCompanyWatchFilter");
            }

            return Result.Failure(DomainError.NotFound("CompanyWatch", command.CompanyWatchId));
        }

        // Empty selection = "no filter" = the canonical NULL column. ClearFilter is idempotent and
        // deliberately allowed in any soft-delete state (it only ever removes processing scope).
        var wantsFilter =
            command.Municipalities.Count > 0
            || command.Regions.Count > 0
            || command.OnlyMatched;

        if (!wantsFilter)
        {
            watch.ClearFilter();
            return Result.Success();
        }

        var spec = WatchFilterSpec.Create(
            command.Municipalities, command.Regions, command.OnlyMatched);
        if (spec.IsFailure)
            return Result.Failure(spec.Error);

        return watch.SetFilter(spec.Value);
    }
}

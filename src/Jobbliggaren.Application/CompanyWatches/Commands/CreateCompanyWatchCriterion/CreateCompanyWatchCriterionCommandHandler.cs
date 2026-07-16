using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.CompanyWatches.Commands.CreateCompanyWatchCriterion;

/// <summary>
/// Creates the criterion, owner-scoped by <c>ICurrentUser</c> (C-D10). The pipeline has already run
/// the request validator (C-D12 raw caps + per-axis existence against the SCB reference catalogs) —
/// the Domain re-validates FORMAT and the normalized caps in
/// <see cref="CompanyWatchCriteriaSpec.Create"/> (defense in depth; the spec stays the invariant
/// owner).
///
/// <para>
/// <b><c>MaxPerUser</c> is enforced HERE, server-side (C-D11)</b> — a cross-instance rule a single
/// aggregate cannot see (the <c>RecentJobSearch.MaxPerSeeker</c> precedent), and never FE-only. The
/// count is the ordinary live count: user delete is HARD (C-D8/G1 verdict 2026-07-16), so there is
/// no soft-deleted residue to argue about. REJECT at the cap, never evict — an eviction precedent
/// exists only for passive telemetry (recent searches); silently discarding a criterion the user
/// authored would be data loss. Two racing creates can both pass the count and land at 21 — a
/// cosmetic overshoot on a user-deletable object, accepted like the missing unique constraint
/// (PR-1); the cap's job is bounding browse/notification cost, not exact cardinality.
/// </para>
/// </summary>
public sealed class CreateCompanyWatchCriterionCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : ICommandHandler<CreateCompanyWatchCriterionCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(
        CreateCompanyWatchCriterionCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
        {
            return Result.Failure<Guid>(DomainError.Validation(
                "CompanyWatchCriterion.Unauthorized", "Användaren är inte autentiserad."));
        }

        var userId = currentUser.UserId.Value;

        var liveCount = await db.CompanyWatchCriteria
            .CountAsync(c => c.UserId == userId, cancellationToken);
        if (liveCount >= CompanyWatchCriterion.MaxPerUser)
        {
            return Result.Failure<Guid>(DomainError.Conflict(
                "CompanyWatchCriterion.MaxPerUser",
                $"Du kan ha högst {CompanyWatchCriterion.MaxPerUser} bevakningar. "
                + "Ta bort en bevakning för att skapa en ny."));
        }

        var specResult = CompanyWatchCriteriaSpec.Create(
            command.Criteria.SniCodes, command.Criteria.MunicipalityCodes);
        if (specResult.IsFailure)
            return Result.Failure<Guid>(specResult.Error);

        var criterionResult = CompanyWatchCriterion.Create(
            userId, specResult.Value, command.Label, clock);
        if (criterionResult.IsFailure)
            return Result.Failure<Guid>(criterionResult.Error);

        db.CompanyWatchCriteria.Add(criterionResult.Value);

        return Result.Success(criterionResult.Value.Id.Value);
    }
}

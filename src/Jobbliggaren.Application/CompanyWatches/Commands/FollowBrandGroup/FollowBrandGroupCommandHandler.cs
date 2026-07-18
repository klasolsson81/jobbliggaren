using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Commands.FollowBrandGroup;

/// <summary>
/// ADR 0087 D4 — follow a curated brand group by slug. Validates the slug FORMAT via the VO, then
/// checks EXISTENCE against the curated catalogue (an unknown-but-well-formed slug is a
/// <c>NotFound</c>, never a stored watch that matches nothing forever — the vacuous-follow guard,
/// parity the criteria existence-validator). Existence lives in the handler (not the validator): the
/// catalogue is DI-instance data and an absent resource is a 404, not a 400 shape error. Persistence
/// (resurrect + unique-race) is the shared <see cref="CompanyWatchFollowExecutor"/>.
/// </summary>
public sealed class FollowBrandGroupCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IDbExceptionInspector dbExceptionInspector,
    IBrandGroupProvider brandGroups)
    : ICommandHandler<FollowBrandGroupCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(
        FollowBrandGroupCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure<Guid>(
                DomainError.Validation("CompanyWatch.Unauthorized", "Användaren är inte autentiserad."));

        var idResult = BrandGroupId.Create(command.BrandGroupId);
        if (idResult.IsFailure)
            return Result.Failure<Guid>(idResult.Error);

        // Existence against the curated catalogue — an unknown slug is a NotFound, never a stored watch.
        if (brandGroups.Catalog.Find(idResult.Value.Value) is null)
            return Result.Failure<Guid>(DomainError.NotFound(
                "BrandGroup.NotFound", "Varumärkesgruppen finns inte."));

        return await CompanyWatchFollowExecutor.FollowBrandGroupOrResurrectAsync(
            db, dbExceptionInspector, currentUser.UserId.Value, idResult.Value, clock, cancellationToken);
    }
}

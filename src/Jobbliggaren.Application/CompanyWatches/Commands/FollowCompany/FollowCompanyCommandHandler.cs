using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Commands.FollowCompany;

/// <summary>
/// ADR 0087 D3 — follow an employer by org.nr (from the request body). Keyed by <c>UserId</c> directly
/// (no JobSeeker hop — D3 cohesion-follows-the-consumer). Validates the org.nr via the VO, then hands
/// off to <see cref="CompanyWatchFollowExecutor"/> — the shared idempotent resurrect + unique-race path
/// (FORK B1), single-sourced with the #455 <c>FollowCompanyFromJobAdCommand</c> entry point so the two
/// cannot drift.
/// </summary>
public sealed class FollowCompanyCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IDbExceptionInspector dbExceptionInspector)
    : ICommandHandler<FollowCompanyCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(
        FollowCompanyCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure<Guid>(
                DomainError.Validation("CompanyWatch.Unauthorized", "Användaren är inte autentiserad."));

        var orgNrResult = OrganizationNumber.Create(command.OrganizationNumber);
        if (orgNrResult.IsFailure)
            return Result.Failure<Guid>(orgNrResult.Error);

        return await CompanyWatchFollowExecutor.FollowOrResurrectAsync(
            db, dbExceptionInspector, currentUser.UserId.Value, orgNrResult.Value, clock, cancellationToken);
    }
}

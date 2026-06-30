using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.CompanyWatches.Commands.FollowCompany;

/// <summary>
/// ADR 0087 D3 — follow an employer by org.nr. Keyed by <c>UserId</c> directly (no JobSeeker hop —
/// D3 cohesion-follows-the-consumer). FORK B1 single-row resurrect: the existing-watch lookup uses
/// <c>IgnoreQueryFilters</c> so a previously soft-deleted row is found and RESURRECTED rather than
/// a second row inserted — there is exactly one physical row per (UserId, org.nr) ever. The
/// active-partial <c>UNIQUE(user_id, organization_number) WHERE deleted_at IS NULL</c> guards the
/// concurrent-fresh-follow race (ADR 0032 §5 ON CONFLICT idiom, parity with <c>SaveJobAd</c>).
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

        var userId = currentUser.UserId.Value;
        var orgNr = orgNrResult.Value;

        // IgnoreQueryFilters — a previously unfollowed (soft-deleted) row must be FOUND so it can be
        // resurrected (FORK B1). Querying active-only would miss it and insert a second physical row
        // (the active-partial unique does not block it, since the soft-deleted row is not in the
        // partial index). Tracked (not AsNoTracking) so a resurrect persists via the UnitOfWork.
        var existing = await db.CompanyWatches
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                w => w.UserId == userId && w.OrganizationNumber == orgNr, cancellationToken);

        if (existing is not null)
        {
            // Active → idempotent no-op; soft-deleted → resurrect the same row (Refollow no-ops if
            // already active, so this is correct for both states).
            existing.Refollow(clock);
            return Result.Success(existing.Id.Value);
        }

        var watchResult = CompanyWatch.Follow(userId, orgNr, clock);
        if (watchResult.IsFailure)
            return Result.Failure<Guid>(watchResult.Error);

        var watch = watchResult.Value;
        db.CompanyWatches.Add(watch);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (dbExceptionInspector.IsUniqueConstraintViolation(ex))
        {
            // Race: a concurrent fresh follow of the same (user, org.nr) won. Detach our failed
            // insert and return the winner's id (idempotent semantics, parity with SaveJobAd).
            db.Detach(watch);

            var winner = await db.CompanyWatches
                .AsNoTracking()
                .Where(w => w.UserId == userId && w.OrganizationNumber == orgNr)
                .Select(w => w.Id)
                .FirstAsync(cancellationToken);

            return Result.Success(winner.Value);
        }

        return Result.Success(watch.Id.Value);
    }
}

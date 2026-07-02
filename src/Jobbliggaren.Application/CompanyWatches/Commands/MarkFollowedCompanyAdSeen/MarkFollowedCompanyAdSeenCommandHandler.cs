using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.CompanyWatches.Commands.MarkFollowedCompanyAdSeen;

/// <summary>
/// #453 (cross-channel dedup; ADR 0087 D5-addendum) — stamps <c>SeenAt</c> on every still-Pending,
/// not-yet-seen <c>FollowedCompanyAdHit</c> for the authenticated user + this ad, so the follow-digest
/// suppresses the redundant email. Mirrors <c>MarkMatchesSeenCommandHandler</c>'s auth + owner-scope
/// shape (loads the rows TRACKED so the <c>UnitOfWorkBehavior</c> persists the stamp). NO AI/LLM, no PII
/// (org.nr never read — this touches a timestamp only).
///
/// <para>
/// <b>Owner-scoped (IDOR-safe, §5/§12):</b> the query filters <c>UserId == currentUser</c>, so a foreign
/// jobAdId (or one the user does not follow) simply matches zero rows → a benign no-op Success. The
/// UserId is NEVER taken from the wire. The same ad matched via TWO of a user's follows is two hits
/// (the UNIQUE key carries CompanyWatchId) — the user saw the AD, so both are stamped.
/// </para>
/// </summary>
public sealed class MarkFollowedCompanyAdSeenCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : ICommandHandler<MarkFollowedCompanyAdSeenCommand, Result>
{
    public async ValueTask<Result> Handle(
        MarkFollowedCompanyAdSeenCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure(
                DomainError.Validation("FollowedCompanyAdHit.Unauthorized", "Användaren är inte autentiserad."));

        var userId = currentUser.UserId.Value;
        var jobAdId = new JobAdId(command.JobAdId);

        // Owner-scoped, Pending-and-unseen only. Equality on the value-converted JobAdId translates to a
        // plain uuid comparison (NOT the strongly-typed-VO `.Contains` trap — a single `==` is safe).
        // Loading only unseen rows keeps MarkSeen calls minimal; the domain method is idempotent anyway.
        var freshPendingHits = await db.FollowedCompanyAdHits
            .Where(h => h.UserId == userId
                        && h.JobAdId == jobAdId
                        && h.NotificationStatus == FollowedCompanyAdHitStatus.Pending
                        && h.SeenAt == null)
            .ToListAsync(cancellationToken);

        foreach (var hit in freshPendingHits)
            hit.MarkSeen(clock);

        // Always Success — an absent hit is a benign no-op (never NotFound: that would leak whether the
        // user follows this ad's employer). The UnitOfWorkBehavior commits any stamps.
        return Result.Success();
    }
}

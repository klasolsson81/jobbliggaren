using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.RecentJobSearches;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.RecentJobSearches.Commands.MarkRecentSearchQuickLink;

/// <summary>
/// ADR 0188 (#2265) — marks the user's own recent search as a quick link in the lowest free slot. Another user's row
/// answers the same 404 as an unknown one, and the attempt is logged (ADR 0031). A search that already is a quick link
/// is a no-op that succeeds; otherwise <see cref="QuickLinkCap"/> is asked first, with the junior link counted while it
/// is on.
/// <para>
/// It saves on its own, as <c>SaveJobAdCommandHandler</c> does, to tell two races apart: a row deleted meanwhile (by an
/// eviction, another tab or an Art. 17 erasure) is a 404, and a slot a racing mark took first is a 409 of its own. Each
/// failed write is detached before it returns, or the unit of work's own save would send it again. The audit row then
/// commits in the unit of work's save, after this one.
/// </para>
/// </summary>
public sealed class MarkRecentSearchQuickLinkCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFailedAccessLogger failedAccessLogger,
    IDateTimeProvider clock,
    IDbExceptionInspector dbExceptionInspector)
    : ICommandHandler<MarkRecentSearchQuickLinkCommand, Result>
{
    public async ValueTask<Result> Handle(
        MarkRecentSearchQuickLinkCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure(
                DomainError.Validation("RecentJobSearch.Unauthorized", "Användaren är inte autentiserad."));

        var seeker = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => new { js.Id, js.Preferences.JuniorQuickLinkOffAt })
            .FirstOrDefaultAsync(cancellationToken);

        if (seeker is null)
            return Result.Failure(DomainError.NotFound("RecentJobSearch", command.Id));

        var recentId = new RecentJobSearchId(command.Id);

        var recent = await db.RecentJobSearches
            .Where(r => r.Id == recentId && r.JobSeekerId == seeker.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (recent is null)
        {
            var exists = await db.RecentJobSearches
                .AsNoTracking()
                .AnyAsync(r => r.Id == recentId, cancellationToken);
            if (exists)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "RecentJobSearch", recentId.Value, currentUser.UserId.Value,
                    "MarkRecentSearchQuickLink");
            }
            return Result.Failure(DomainError.NotFound("RecentJobSearch", command.Id));
        }

        if (recent.QuickLinkSlot is not null)
            return Result.Success();

        var usedSlots = await db.RecentJobSearches
            .AsNoTracking()
            .Where(r => r.JobSeekerId == seeker.Id && r.QuickLinkSlot != null)
            .Select(r => r.QuickLinkSlot!.Value)
            .ToListAsync(cancellationToken);

        var room = QuickLinkCap.EnsureRoomForOne(juniorLinkOn: seeker.JuniorQuickLinkOffAt is null, usedSlots.Count);
        if (room.IsFailure)
            return room;

        // Room for one means fewer than MaxTotal slots are held, and the CHECK keeps each in 1..MaxTotal.
        var slot = QuickLinkCap.FreeSlot(usedSlots)
            ?? throw new InvalidOperationException("Room for a quick link but no free slot.");

        recent.MarkAsQuickLink(slot, clock.UtcNow);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.Detach(recent);
            return Result.Failure(DomainError.NotFound("RecentJobSearch", command.Id));
        }
        catch (DbUpdateException ex) when (dbExceptionInspector.IsUniqueConstraintViolation(ex))
        {
            db.Detach(recent);
            return Result.Failure(DomainError.Conflict(
                "QuickLink.SlotTaken",
                "En annan snabblänk lades till samtidigt. Försök igen."));
        }

        return Result.Success();
    }
}

using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.SavedJobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.SavedJobAds.Commands.SaveJobAd;

/// <summary>
/// F6 P5 Punkt 2 Del A — bokmärk en annons. Idempotent vid redan-sparad
/// (returnerar Success utan ny domain-event). Race-säkert via UNIQUE-index
/// (ADR 0032 §5 ON CONFLICT-mönstret återanvänt): pre-check → INSERT →
/// vid DbUpdateException UNIQUE-violation → reload-redan-sparad → Success.
/// JobAd-existens valideras innan persist (ADR 0011 strongly-typed
/// soft-reference: ingen DB-FK fångar phantom-jobAdId).
/// </summary>
public sealed class SaveJobAdCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IDbExceptionInspector dbExceptionInspector)
    : ICommandHandler<SaveJobAdCommand, Result>
{
    public async ValueTask<Result> Handle(
        SaveJobAdCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure(
                DomainError.Validation("SavedJobAd.Unauthorized", "Användaren är inte autentiserad."));

        var jobSeekerId = await db.JobSeekers
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerId == default)
            return Result.Failure(DomainError.NotFound("JobSeeker", currentUser.UserId.Value));

        var jobAdId = new JobAdId(command.JobAdId);

        // #842 — a TOMBSTONE cannot be bookmarked. Reachable from a stale list, an open tab or a
        // bookmarked URL. Without this, the read side (ListSavedJobAds) renders the new row as the
        // "Annonsen är borttagen" orphan — a guard apologising for a write we could simply refuse.
        var jobAdExists = await db.JobAds
            .AsNoTracking()
            .AnyAsync(j => j.Id == jobAdId && j.Status != JobAdStatus.Erased, cancellationToken);

        if (!jobAdExists)
            return Result.Failure(DomainError.NotFound("JobAd", command.JobAdId));

        // Idempotent pre-check — redan sparad?
        var alreadySaved = await db.SavedJobAds
            .AsNoTracking()
            .AnyAsync(s => s.JobSeekerId == jobSeekerId && s.JobAdId == jobAdId, cancellationToken);

        if (alreadySaved)
            return Result.Success(); // No-op, ingen ny domain-event

        var saved = SavedJobAd.Save(jobSeekerId, jobAdId, clock.UtcNow);
        db.SavedJobAds.Add(saved);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (dbExceptionInspector.IsUniqueConstraintViolation(ex))
        {
            // Race: två concurrent saves från samma session. Den andra vann.
            // Detach the failed insert + return Success (idempotent semantik).
            db.Detach(saved);
            return Result.Success();
        }

        return Result.Success();
    }
}

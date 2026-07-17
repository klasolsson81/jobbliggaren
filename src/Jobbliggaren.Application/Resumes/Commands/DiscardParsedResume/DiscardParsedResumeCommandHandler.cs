using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Resumes.Files;
using Jobbliggaren.Domain.Resumes.Parsing;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Resumes.Commands.DiscardParsedResume;

/// <summary>
/// Discards the owner's <c>PendingReview</c> staging artifact (Fas 4b PR-8, CTO-bind
/// Q6). Mirrors <c>PromoteParsedResumeCommandHandler</c>'s access shape exactly:
/// resolve owner → owner-scoped load (the global DeletedAt filter already hides
/// promoted/discarded artifacts, so a finalized artifact reads as NotFound —
/// fail-closed and idempotent-safe) → IDOR 404-parity with cross-user logging →
/// <c>ParsedResume.Discard</c> (the aggregate owns the transition) → cascade an
/// immediate hard-delete of the coupled original file(s) in the SAME UnitOfWork
/// (CV-pivot 5b, security-bind B5 / CTO-bind M-E): discarding the draft IS the
/// withdrawal affordance for a consented pnr-flagged original (Art. 7(3) — as easy
/// as the consent was to give, never waiting on the 30-day sweep), and the cascade
/// is deliberately UNCONDITIONAL so a clean file's storage ends with its draft too
/// (Art. 5(1)(e) — the same one-line cascade closes the whole orphan class).
/// Never reads the decrypted content.
/// </summary>
public sealed class DiscardParsedResumeCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IFailedAccessLogger failedAccessLogger)
    : ICommandHandler<DiscardParsedResumeCommand, Result>
{
    public async ValueTask<Result> Handle(
        DiscardParsedResumeCommand command, CancellationToken cancellationToken)
    {
        // AuthorizationBehavior has already thrown if !currentUser.IsAuthenticated.
        if (!currentUser.UserId.HasValue)
            throw new UnauthorizedException();

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerId == default)
            return Result.Failure(
                DomainError.NotFound("JobSeeker", currentUser.UserId.Value));

        var parsedResumeId = new ParsedResumeId(command.ParsedResumeId);
        var parsed = await db.ParsedResumes
            .FirstOrDefaultAsync(
                r => r.Id == parsedResumeId && r.JobSeekerId == jobSeekerId, cancellationToken);

        if (parsed is null)
        {
            // IDOR fail-closed: identical NotFound whether the id is unknown or belongs to
            // another user (no enumeration oracle); log only the cross-user case.
            var exists = await db.ParsedResumes
                .AsNoTracking()
                .AnyAsync(r => r.Id == parsedResumeId, cancellationToken);
            if (exists)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "ParsedResume", parsedResumeId.Value, currentUser.UserId.Value, "DiscardParsedResume");
            }
            return Result.Failure(DomainError.NotFound("ParsedResume", parsedResumeId.Value));
        }

        // Idempotent transition (the aggregate early-returns on a repeat); in practice a
        // finalized artifact is already invisible above via the global DeletedAt filter.
        parsed.Discard(clock);

        // Immediate hard-delete of the coupled original file(s), parity the promoted-file
        // cascade in DeleteResumeCommandHandler: project ONLY the id (DEK-free, never the
        // multi-MB sealed bytea — §5 minimisation), Remove a key-only stub so the DELETE rides
        // THIS handler's UnitOfWork SaveChanges — one implicit EF transaction, atomic with the
        // soft-delete above. Owner-scoped on JobSeekerId as defence-in-depth parity with the
        // IDOR-hardened load. Re-discarding an already-discarded artifact finds no files
        // (deleted the first time) — the cascade is naturally idempotent.
        var fileIds = await db.ResumeFiles
            .Where(f => f.ParsedResumeId == parsed.Id && f.JobSeekerId == jobSeekerId)
            .Select(f => f.Id)
            .ToListAsync(cancellationToken);

        foreach (var fileId in fileIds)
            db.ResumeFiles.Remove(ResumeFile.DeleteHandle(fileId));

        return Result.Success();
    }
}

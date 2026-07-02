using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.Resumes.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Resumes.Commands.PromoteParsedResume;

/// <summary>
/// Promotes a <c>PendingReview</c> <c>ParsedResume</c> into a canonical <c>Resume</c>
/// (Fas 4 STEG A, ADR 0071/0074 — NO AI/LLM). Flow (CTO `a24324c841f84c8be`):
/// resolve owner → owner-scoped load of the staging artifact (IDOR fail-closed, parity
/// with <c>ReviewParsedResumeQueryHandler</c>) → re-run the personnummer guard on the
/// user-submitted gap-fill content BEFORE construction (DQ6 — the parse gate only saw the
/// ORIGINAL parse; the user could have typed a new personnummer) → build the Resume from
/// the user-approved payload via <c>Resume.CreateFromParsed</c> (DQ1 Variant A / DQ5b —
/// the approved content IS the Resume; the backend never synthesises from the parse,
/// CLAUDE.md §5) → <c>ParsedResume.Promote</c> (the aggregate owns the gate; soft-deletes
/// the artifact, DQ7) → persist. The handler never reads or logs the decrypted parsed
/// content; the warmed owner DEK (<c>IRequiresFieldEncryptionKey</c>) encrypts the new
/// Master content on write (ADR 0074 Invariant 3).
/// </summary>
public sealed class PromoteParsedResumeCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IFailedAccessLogger failedAccessLogger)
    : ICommandHandler<PromoteParsedResumeCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(
        PromoteParsedResumeCommand command, CancellationToken cancellationToken)
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
            return Result.Failure<Guid>(
                DomainError.NotFound("JobSeeker", currentUser.UserId.Value));

        // Owner-scoped load. The parsed_resumes global query filter (DeletedAt == null)
        // means a Discarded/Promoted artifact is already invisible here — a finalized
        // artifact reads as NotFound, which is exactly the fail-closed answer we want.
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
                    "ParsedResume", parsedResumeId.Value, currentUser.UserId.Value, "PromoteParsedResume");
            }
            return Result.Failure<Guid>(DomainError.NotFound("ParsedResume", parsedResumeId.Value));
        }

        // DQ6 (highest-severity PII): re-run the personnummer guard on the user-submitted
        // content (the parse gate only covered the ORIGINAL parse). Shared with
        // UpdateMasterContent (#499) via ResumeContentPersonnummerGuard so every
        // ResumeContentDto write surface guards identically (DRY; the arch test requires it).
        // A hit blocks promotion with a Resume-scoped code — nothing is mutated.
        var guard = ResumeContentPersonnummerGuard.Check(command.Content);
        if (guard.IsFailure)
            return Result.Failure<Guid>(guard.Error);

        // Build the Resume from the approved payload (content validated by ValidateContent
        // inside the factory). No mutation of the staging artifact yet.
        var content = ResumeContentMapper.ToDomain(command.Content);
        var created = Resume.CreateFromParsed(jobSeekerId, command.Name, content, parsed.Id, clock);
        if (created.IsFailure)
            return Result.Failure<Guid>(created.Error);

        // Promote the staging artifact (aggregate owns the gate: PendingReview + no flagged
        // personnummer from the original parse). Only mutates on success.
        var promotion = parsed.Promote(clock);
        if (promotion.IsFailure)
            return Result.Failure<Guid>(promotion.Error);

        var resume = created.Value;
        db.Resumes.Add(resume);

        return Result.Success(resume.Id.Value);
    }
}

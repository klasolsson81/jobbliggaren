using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.Resumes.Common;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
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
/// with <c>ReviewParsedResumeQueryHandler</c>) → <b>derive the preamble from the parse</b>
/// (#1060 — never from the transport) → re-run the personnummer guard on the resulting
/// content BEFORE construction (DQ6 — the parse gate only saw the ORIGINAL parse; the user
/// could have typed a new personnummer, and the derived preamble is not a substring of the
/// text that gate scanned) → build the Resume from the user-approved payload via
/// <c>Resume.CreateFromParsed</c> (DQ1 Variant A / DQ5b — the approved content IS the Resume
/// for every field the user can edit; the backend never synthesises from the parse,
/// CLAUDE.md §5) → <c>ParsedResume.Promote</c> (the aggregate owns the gate; soft-deletes
/// the artifact, DQ7) → persist.
///
/// <para><b>The handler DOES read one decrypted parsed field</b> — <c>Content.Preamble</c>,
/// since #1060 — and it never logs it. That sentence used to say the handler reads nothing
/// decrypted at all, which the derivation made false; corrected rather than left standing,
/// because a docblock claiming a PII path does not exist is how the next reader decides not
/// to look. The read happens inside the warmed owner DEK
/// (<c>IRequiresFieldEncryptionKey</c>), which also encrypts the new Master content on write
/// (ADR 0074 Invariant 3); the value goes straight into the guard and then into the
/// aggregate, and no logger, telemetry property or error message touches it.</para>
/// </summary>
public sealed class PromoteParsedResumeCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IFailedAccessLogger failedAccessLogger,
    IResumeReviewReconciler reconciler)
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

        // #1060 — the preamble is DERIVED from the parse, never accepted from the transport.
        // This is the second of `CreateFromParsed`'s two ingresses, and it is client-fed:
        // `command.Content` is bound straight off the HTTP body, and it has carried `Preamble`
        // since the field was added. Without this line the arm fails in both directions at once
        // — a client could AUTHOR a permanent preamble on its own CV (falsifying the write-once
        // rule the aggregate enforces on update), while every realistic client DROPS the text
        // instead, because no write schema models the key. The drop is ADR 0109 §3's "dropping
        // is the bug" surviving on one arm of the very PR that retires it.
        //
        // It happens BEFORE the guard, and that ordering is the whole point rather than a
        // detail. The parse's preamble is NOT a substring of RawText — `PreambleResidue.Subtract`
        // splices surviving fragments — so a personnummer straddling a subtracted fragment was
        // never visible to the import scan that sets `Personnummer.Found`, and DQ6 is the only
        // control that can catch it. Substituting after the guard would hand `CreateFromParsed`
        // a preamble nothing on this path had scanned. Both arms now share one ordering:
        // derive → guard → ToDomain → CreateFromParsed.
        var submitted = command.Content with { Preamble = parsed.Content.Preamble };

        // DQ6 (highest-severity PII): re-run the personnummer guard on the user-submitted
        // content (the parse gate only covered the ORIGINAL parse). Shared with
        // UpdateMasterContent (#499) via ResumeContentPersonnummerGuard so every
        // ResumeContentDto write surface guards identically (DRY; the arch test requires it).
        // A hit blocks promotion with a Resume-scoped code — nothing is mutated.
        var guard = ResumeContentPersonnummerGuard.Check(submitted);
        if (guard.IsFailure)
            return Result.Failure<Guid>(guard.Error);

        // Build the Resume from the approved payload (content validated by ValidateContent
        // inside the factory). No mutation of the staging artifact yet.
        var content = ResumeContentMapper.ToDomain(submitted);
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

        // Fas 4b PR-8 (CTO-bind Q1; handoff §3 "efter Spara körs granskningen"): seed the
        // DEK-free finding-status ledger in the SAME transaction as the promote, so the
        // hub badge is live from the first save without the engine ever running on the
        // list path (ADR 0045). The reconciler completes or THROWS (CTO bind
        // 2026-07-17): a throw propagates past this handler, the unconditional
        // UnitOfWork save never runs, and the tracked promote + Resume add roll back
        // together — never a promoted artifact without its ledger.
        await reconciler.ReconcileAsync(resume, null, cancellationToken);

        return Result.Success(resume.Id.Value);
    }
}

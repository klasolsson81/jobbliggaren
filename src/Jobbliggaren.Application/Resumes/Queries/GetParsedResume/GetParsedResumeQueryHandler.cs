using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Resumes.Commands.AutoPromoteParsedResume;
using Jobbliggaren.Application.Resumes.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Resumes.Parsing;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Resumes.Queries.GetParsedResume;

/// <summary>
/// Loads the OWNING job seeker's PendingReview parsed-CV staging artifact and maps it to the
/// detail DTO. Mirrors <c>ReviewParsedResumeQueryHandler</c> EXACTLY (the same fail-closed
/// IDOR shape): resolve the owner from <see cref="ICurrentUser"/>, FirstOrDefault on
/// <c>db.ParsedResumes</c> filtered by Id + JobSeekerId, return null on not-found OR cross-user
/// (logging the cross-user attempt), else map. The aggregate is materialised inside the warmed
/// field-encryption pipeline (the query is <c>IRequiresFieldEncryptionKey</c>), so the
/// decryption interceptor decrypts the CV-PII shadows on read — this handler is the only thing
/// that touches the DbContext + DEK pipeline, and never logs the content (Invariant 3 / §5).
///
/// <para><b>It also answers "why is this file not a CV yet" (#1060 CTO-bind D4-REBIND).</b> The
/// block reason is DERIVED here, not stored: this is the one surface that is already DEK-warm,
/// already owner-scoped and already the destination the pending card's primary action points
/// at, so the user learns the reason by opening the review she was opening anyway instead of
/// re-uploading the file. Nothing is persisted, no column is added, and the evaluator is the
/// SAME <see cref="AutoPromoteGate"/> the write path runs — a second predicate that could
/// disagree with the real gate is the failure mode the bind rejected outright, because a wrong
/// reason shown confidently is worse than no reason (CLAUDE.md §5).</para>
///
/// <para><b>Why not on <c>GetLatestPendingParsedResume</c>, which is what feeds the hub card.</b>
/// That handler is deliberately NOT <c>IRequiresFieldEncryptionKey</c> and projects four
/// non-PII columns; its docblock states the reason (PII-minimisation, Art. 5(1)(c)). Two of the
/// gates read DEK-bearing content, so evaluating there would mean warming the DEK on a page
/// every user loads on every visit — deleting a written security control to save a click. The
/// control stands; the hub card routes here instead.</para>
///
/// <para><b>Cost, stated rather than hidden:</b> the derivation composes the transport DTO,
/// runs the personnummer scan over it and builds a canonical <c>Resume</c> that is discarded —
/// two in-memory content graphs and one regex sweep over a few kilobytes of CV text, on a
/// request that already pays an AES-GCM decrypt of that same content. No I/O and no second
/// round-trip: <c>DisplayName</c> joins the owner projection that was already running.</para>
/// </summary>
public sealed class GetParsedResumeQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFailedAccessLogger failedAccessLogger,
    IDateTimeProvider clock)
    : IQueryHandler<GetParsedResumeQuery, ParsedResumeDetailDto?>
{
    public async ValueTask<ParsedResumeDetailDto?> Handle(
        GetParsedResumeQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return null;

        // One projection resolves both the owner scope and the gate's person-name input
        // (JobSeeker.DisplayName — parity AutoPromoteParsedResumeCommandHandler); one column
        // more on the round-trip that was already happening, no second query.
        var owner = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => new { js.Id, js.DisplayName })
            .FirstOrDefaultAsync(cancellationToken);

        if (owner is null || owner.Id == default)
            return null;

        var jobSeekerId = owner.Id;
        var parsedResumeId = new ParsedResumeId(query.ParsedResumeId);
        var resume = await db.ParsedResumes
            .AsNoTracking()
            .Where(r => r.Id == parsedResumeId && r.JobSeekerId == jobSeekerId)
            .FirstOrDefaultAsync(cancellationToken);

        if (resume is null)
        {
            // Identical NotFound for cross-user and unknown — no enumeration oracle. A
            // promoted/discarded artifact is excluded by the global DeletedAt filter and
            // reads as a plain not-found (no cross-user log), parity ReviewParsedResume.
            var exists = await db.ParsedResumes
                .AsNoTracking()
                .AnyAsync(r => r.Id == parsedResumeId, cancellationToken);
            if (exists)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "ParsedResume", parsedResumeId.Value, currentUser.UserId.Value, "GetParsedResume");
            }
            return null;
        }

        // The label the gate evaluates is the GENERATED default, never a user-typed one: this
        // read asks "what does THIS ARTIFACT need", and the upload form's name field is a
        // future input, not a property of the file sitting in staging. A personnummer the user
        // later types into that field is a submit-time failure of the label channel, surfaced
        // by the write path (which resolves the same label from the real override). The
        // generated default is non-empty, capped and personnummer-free by construction, so it
        // can never be the thing that produces a reason here.
        var label = ResumeLabelResolver.Resolve(nameOverride: null, clock);
        var blockReason = AutoPromoteGate
            .Evaluate(resume, owner.DisplayName, label, jobSeekerId, clock)
            .BlockReason;

        return resume.ToDetailDto(blockReason);
    }
}

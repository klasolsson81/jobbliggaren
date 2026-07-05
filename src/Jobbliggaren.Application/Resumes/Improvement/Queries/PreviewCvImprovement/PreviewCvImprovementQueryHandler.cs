using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Improvement.Abstractions;
using Jobbliggaren.Application.Resumes.Improvement.FrameApply;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Resumes.Improvement.Queries.PreviewCvImprovement;

/// <summary>
/// Composes the EFTER-preview (Fas 4b PR-7, #656) WITHOUT persisting: no
/// <c>UpdateMasterContent</c>, no status write — the aggregate is read
/// <c>AsNoTracking</c> so a mutation cannot even leak through SaveChanges. Owner/IDOR
/// shape mirrors <c>SuggestCvImprovementsQueryHandler</c> but surfaces NotFound as a
/// typed Result failure (the central mapper renders 404), logging only the genuine
/// cross-user attempt. The same frame gates as apply (unknown frame / criterion
/// mismatch / slot grounding) fail the SAME way, so a preview that succeeds is an apply
/// that will succeed against unchanged content.
/// </summary>
public sealed class PreviewCvImprovementQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    ICvReviewEngine reviewEngine,
    IFrameProvider frameProvider,
    IVerbMapper verbMapper,
    IFailedAccessLogger failedAccessLogger)
    : IQueryHandler<PreviewCvImprovementQuery, Result<FramePreviewDto>>
{
    public async ValueTask<Result<FramePreviewDto>> Handle(
        PreviewCvImprovementQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return NotFound(query.ResumeId);

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var resumeId = new ResumeId(query.ResumeId);
        var resume = await db.Resumes
            .AsNoTracking()
            .Include(r => r.Versions)
            .FirstOrDefaultAsync(r => r.Id == resumeId && r.JobSeekerId == jobSeekerId, cancellationToken);

        if (resume is null)
        {
            var exists = await db.Resumes
                .AsNoTracking()
                .AnyAsync(r => r.Id == resumeId, cancellationToken);
            if (exists && currentUser.UserId.HasValue)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "Resume", resumeId.Value, currentUser.UserId.Value, "PreviewCvImprovement");
            }

            return NotFound(query.ResumeId);
        }

        var catalog = frameProvider.GetFrameCatalog();
        var frame = catalog.Frames.FirstOrDefault(f =>
            string.Equals(f.Id, query.FrameId, StringComparison.Ordinal));
        if (frame is null)
        {
            return Result.Failure<FramePreviewDto>(DomainError.Validation(
                "Resume.FrameUnknown", "Ramen finns inte i den aktuella ram-katalogen."));
        }

        if (!frame.CriterionIds.Contains(query.CriterionId, StringComparer.Ordinal))
        {
            return Result.Failure<FramePreviewDto>(DomainError.Validation(
                "Resume.FrameCriterionMismatch", "Ramen åtgärdar inte det angivna kriteriet."));
        }

        var mapping = verbMapper.GetVerbMapping();
        var strongVerbs = mapping.StrongVerbGroups
            .SelectMany(g => g.Verbs)
            .Select(v => v.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Server recompute over the CURRENT canonical content (ADR 0074 — no client text).
        var content = resume.MasterVersion.Content;
        var review = await reviewEngine.ReviewAsync(
            CvReviewContext.FromCanonical(
                content, ResumeContentLinearizer.Linearize(content), resume.Language),
            RenderProfile.Ats,
            cancellationToken);

        // The preview MINTS the fingerprint (no client echo yet — this IS the echo source).
        var resolved = FrameApplyComposer.ResolveFinding(review, query.CriterionId, content);
        if (resolved.IsFailure)
            return Result.Failure<FramePreviewDto>(resolved.Error);

        var grounding = FrameSlotGrounding.Validate(
            frame, query.SlotInputs, resolved.Value.Line, strongVerbs);
        if (grounding.IsFailure)
            return Result.Failure<FramePreviewDto>(grounding.Error);

        var proposed = ProposedChange.FromFrame(
            targetId: $"frame:{query.CriterionId}",
            category: resolved.Value.Verdict.Category,
            criterionId: query.CriterionId,
            evidence: new TextSpanEvidence(
                new TextSpan(TextSpan.NotLocated, resolved.Value.Line.Length, resolved.Value.Line),
                Note: null),
            frame: frame,
            slotInputs: query.SlotInputs,
            strongVerbSet: strongVerbs,
            rationale: "Omskriven via deterministisk ram (Åtgärda direkt).");

        var composed = FrameApplyComposer.ApplyToContent(
            content, resolved.Value.Line, proposed.Replacement!.After);
        if (composed.IsFailure)
            return Result.Failure<FramePreviewDto>(composed.Error);

        // The transient preview is a transmit surface — personnummer anywhere in the CV
        // is masked before it leaves the handler (ADR 0074 Invariant 1; the apply path's
        // hard guard is separate and blocks persistence outright).
        var postApplyText = PersonnummerRedactor.Redact(
            ResumeContentLinearizer.Linearize(composed.Value).Text);

        return Result.Success(new FramePreviewDto(
            query.CriterionId,
            query.FrameId,
            PersonnummerRedactor.Redact(resolved.Value.Line),
            PersonnummerRedactor.Redact(proposed.Replacement.After),
            postApplyText,
            resolved.Value.Fingerprint,
            review.RubricVersion.ToString()));
    }

    private static Result<FramePreviewDto> NotFound(Guid resumeId) =>
        Result.Failure<FramePreviewDto>(DomainError.NotFound("Resume", resumeId));
}

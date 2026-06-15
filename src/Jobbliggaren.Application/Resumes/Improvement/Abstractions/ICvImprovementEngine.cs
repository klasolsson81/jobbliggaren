using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes.Parsing;

namespace Jobbliggaren.Application.Resumes.Improvement.Abstractions;

/// <summary>
/// Proposes deterministic propose-and-approve diffs for a parsed CV (Fas 4 STEG 10, F4-10;
/// ADR 0071/0074 — NO AI/LLM). Pure: takes an already-materialised, decrypted
/// <see cref="ParsedResume"/> (Invariant 3 is owned by the read-handler) + the OPTIONAL F4-9
/// review + the render profile; it touches no DbContext, DEK pipeline, or logger. NULL-TOLERANT
/// on <paramref name="review"/> (CTO Q2): it runs fully off the parsed CV + the knowledge bank;
/// when a review is supplied it only enriches each <c>ProposedChange.CriterionId</c> with the
/// matched criterion. It does NOT inject <c>ICvReviewEngine</c> — the handler runs the review
/// then the improvement.
/// </summary>
public interface ICvImprovementEngine
{
    ValueTask<CvImprovementResult> SuggestAsync(
        ParsedResume parsedResume,
        CvReviewResult? review,
        RenderProfile profile,
        CancellationToken cancellationToken);
}

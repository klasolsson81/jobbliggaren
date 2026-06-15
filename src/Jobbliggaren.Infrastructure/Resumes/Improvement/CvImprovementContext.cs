using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes.Parsing;

namespace Jobbliggaren.Infrastructure.Resumes.Improvement;

/// <summary>
/// The immutable inputs one <c>ICvTransform</c> proposes against (Fas 4 STEG 10, F4-10). Built
/// once per request by <see cref="CvImprovementEngine"/>: the (already-decrypted) parsed CV,
/// the OPTIONAL F4-9 review (cross-link source only, CTO Q2), the render profile, the analysis
/// language (from <see cref="ParsedResume.DetectedLanguage"/>), the knowledge-bank lists, and
/// the NLP analyzer. Parity <c>CvReviewContext</c>.
/// </summary>
internal sealed record CvImprovementContext(
    ParsedResume Resume,
    CvReviewResult? Review,
    RenderProfile Profile,
    TextLanguage Language,
    ClicheList Cliches,
    VerbMapping Verbs,
    ITextAnalyzer Analyzer)
{
    /// <summary>The structured parsed content (CV-PII, decrypted upstream).</summary>
    public ParsedResumeContent Content => Resume.Content;

    /// <summary>The raw extracted CV text — the source for cited text spans (Invariant 2).</summary>
    public string RawText => Resume.RawText;

    /// <summary>
    /// The review criterion id to attribute a change to — returns <paramref name="criterionId"/>
    /// only when a review was supplied and carries a verdict for it (CTO Q2: the review enriches
    /// the diff but never gates whether a change is proposed); otherwise null.
    /// </summary>
    public string? CriterionIdFor(string criterionId) =>
        Review?.Verdicts.Any(v => v.CriterionId == criterionId) == true ? criterionId : null;
}

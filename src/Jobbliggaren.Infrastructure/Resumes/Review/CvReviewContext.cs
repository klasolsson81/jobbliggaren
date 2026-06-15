using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes.Parsing;

namespace Jobbliggaren.Infrastructure.Resumes.Review;

/// <summary>
/// The immutable inputs one <c>ICriterionRule</c> evaluates against (Fas 4 STEG 9, F4-9).
/// Built once per criterion by <see cref="CvReviewEngine"/>: the (already-decrypted)
/// parsed CV, the rubric criterion being scored, the render profile, the analysis language
/// (from <see cref="ParsedResume.DetectedLanguage"/>), the knowledge-bank lists, the NLP
/// analyzer, and the pre-parsed dated experiences (so the conditional-Period rules
/// A4/B6/B7 share one parse — V-C).
/// </summary>
internal sealed record CvReviewContext(
    ParsedResume Resume,
    RubricCriterion Criterion,
    RenderProfile Profile,
    TextLanguage Language,
    ClicheList Cliches,
    VerbMapping Verbs,
    ITextAnalyzer Analyzer,
    IReadOnlyList<DatedExperience> DatedExperiences)
{
    /// <summary>The structured parsed content (CV-PII, decrypted upstream).</summary>
    public ParsedResumeContent Content => Resume.Content;

    /// <summary>The raw extracted CV text — the source for cited text spans (Invariant 2).</summary>
    public string RawText => Resume.RawText;
}

/// <summary>
/// A parsed work-experience entry with its period resolved to dates when the
/// <see cref="ParsedExperience.Period"/> string is in a recognised format (V-C). Entries
/// whose period is free-text/unparseable have <see cref="Start"/>/<see cref="End"/> null
/// and <see cref="Parsed"/> = false — they are excluded from the date-based criteria,
/// which report NotAssessed when too few entries parse.
/// </summary>
internal sealed record DatedExperience(
    ParsedExperience Source,
    DateOnly? Start,
    DateOnly? End,
    string? FormatToken)
{
    public bool Parsed => Start is not null && End is not null && FormatToken is not null;
}

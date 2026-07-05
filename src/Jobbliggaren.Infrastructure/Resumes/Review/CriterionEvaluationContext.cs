using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes.Parsing;

namespace Jobbliggaren.Infrastructure.Resumes.Review;

/// <summary>
/// The immutable inputs one <c>ICriterionRule</c> evaluates against (Fas 4 STEG 9, F4-9;
/// renamed from <c>CvReviewContext</c> in Fas 4b PR-4 when ADR 0093 §D8 gave that name to
/// the unified engine INPUT — this record is the engine's per-criterion bundle over it).
/// Built once per criterion by <see cref="CvReviewEngine"/>: the unified review context
/// (source-agnostic content view + linear citation substrate), the rubric criterion being
/// scored, the render profile, the analysis language, the knowledge-bank lists, the NLP
/// analyzer, and the pre-parsed dated experiences (so the conditional-Period rules
/// A4/B6/B7 share one parse — V-C).
/// </summary>
internal sealed record CriterionEvaluationContext(
    CvReviewContext Review,
    RubricCriterion Criterion,
    RenderProfile Profile,
    TextLanguage Language,
    ClicheList Cliches,
    VerbMapping Verbs,
    ITextAnalyzer Analyzer,
    IReadOnlyList<DatedExperience> DatedExperiences)
{
    /// <summary>The source-agnostic structured content view (CV-PII, decrypted upstream).</summary>
    public ReviewableCv Content => Review.Content;

    /// <summary>
    /// The linear citation substrate cited spans resolve against (Invariant 2): the raw
    /// extraction for a staging CV, the shared linearizer's output for a canonical one
    /// (ADR 0093 §D8 — one member, two honest substrates).
    /// </summary>
    public string RawText => Review.LinearText;

    public CvReviewSourceKind Source => Review.Source;

    /// <summary>PII-safe scan outcome (B4) — real for staging, guaranteed-clean for canonical.</summary>
    public PersonnummerScanOutcome Personnummer => Review.Personnummer;

    /// <summary>Source filename (B8) — null on the canonical arm until PR-9's Form C.</summary>
    public string? SourceFileName => Review.SourceFileName;

    /// <summary>Source content type (D1) — null on the canonical arm (no source file).</summary>
    public string? SourceContentType => Review.SourceContentType;

    /// <summary>Parse-extraction integrity (D1) — null on the canonical arm (nothing was parsed).</summary>
    public ParseFallbackReason? ParseFallback => Review.ParseFallback;

    /// <summary>The detected/known section kinds (D6) — parse-confidence-derived for
    /// staging, known-by-construction from the linearizer for canonical.</summary>
    public IReadOnlyList<ParsedSectionKind> DetectedSections => Review.DetectedSections;
}

/// <summary>
/// A work-experience entry with its period resolved to dates (V-C). A canonical entry
/// maps its structured <c>DateOnly</c>s directly (ongoing = the same far-future sentinel
/// the period parser emits); a staging entry is date-parsed from its freeform period
/// string. Entries whose period cannot be resolved have <see cref="Start"/>/<see cref="End"/>
/// null and <see cref="Parsed"/> = false — they are excluded from the date-based criteria,
/// which report NotAssessed when too few entries parse.
/// </summary>
internal sealed record DatedExperience(
    ReviewableExperience Source,
    DateOnly? Start,
    DateOnly? End,
    string? FormatToken)
{
    public bool Parsed => Start is not null && End is not null && FormatToken is not null;
}

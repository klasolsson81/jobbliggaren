using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Improvement.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Improvement.Transforms;

namespace Jobbliggaren.Infrastructure.Resumes.Improvement;

/// <summary>
/// The deterministic CV-build/improve engine (Fas 4 STEG 10, F4-10, ADR 0071/0074 — NO AI/LLM).
/// Produces propose-and-approve diffs over an already-materialised <see cref="ParsedResume"/>
/// against the versioned F4-7 knowledge bank, each diff carrying cited evidence + a closed
/// provenance (a KB value or a verified pure transform — never synthesised, CLAUDE.md §5).
/// Stateless singleton (parity <c>CvReviewEngine</c>): consumes only the immutable knowledge-bank
/// ports + the thread-safe NLP analyzer, takes the CV as a method parameter, and never touches
/// the DbContext, the DEK pipeline, or a logger (Invariant 3 / §5). NULL-TOLERANT on the F4-9
/// review (CTO Q2): it runs fully off the parsed CV + the KB; a supplied review only enriches
/// each change's <c>CriterionId</c>. Compute-on-demand (CTO V-B) — no persistence.
/// </summary>
internal sealed class CvImprovementEngine : ICvImprovementEngine
{
    private readonly IClicheLexicon _clicheLexicon;
    private readonly IVerbMapper _verbMapper;
    private readonly IRubricProvider _rubricProvider;
    private readonly ITextAnalyzer _analyzer;
    private readonly IReadOnlyList<ICvTransform> _transforms;

    public CvImprovementEngine(
        IClicheLexicon clicheLexicon,
        IVerbMapper verbMapper,
        IRubricProvider rubricProvider,
        ITextAnalyzer analyzer)
    {
        _clicheLexicon = clicheLexicon ?? throw new ArgumentNullException(nameof(clicheLexicon));
        _verbMapper = verbMapper ?? throw new ArgumentNullException(nameof(verbMapper));
        _rubricProvider = rubricProvider ?? throw new ArgumentNullException(nameof(rubricProvider));
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        _transforms = BuildTransforms();
    }

    public ValueTask<CvImprovementResult> SuggestAsync(
        ParsedResume parsedResume,
        CvReviewResult? review,
        RenderProfile profile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parsedResume);

        var cliches = _clicheLexicon.GetClicheList();
        var verbs = _verbMapper.GetVerbMapping();
        var rubric = _rubricProvider.GetRubric();
        var language = parsedResume.DetectedLanguage == ResumeLanguage.En
            ? TextLanguage.English
            : TextLanguage.Swedish;

        var context = new CvImprovementContext(
            parsedResume, review, profile, language, cliches, verbs, _analyzer);

        var changes = new List<ProposedChange>();
        foreach (var transform in _transforms)
        {
            if (!AppliesToProfile(transform.Kind, profile))
            {
                continue;
            }

            changes.AddRange(transform.Propose(context));
        }

        // Single choke point (parity #110 CvReviewEngine): redact personnummer out of every change's
        // user-text fields BEFORE assembling the result, so no logged/cached/transmitted proposal can
        // echo a pnr (ADR 0074 Invariant 1; CTO docs/reviews/2026-06-17-f4-improvement-evidence-redaction-cto.md).
        var redactedChanges = ImprovementEvidenceRedactor.Redact(changes);

        var result = new CvImprovementResult(
            cliches.Version, verbs.Version, rubric.Version, profile, redactedChanges);
        return ValueTask.FromResult(result);
    }

    // Fixed iteration order ⇒ deterministic, stable TargetIds across runs (a rule engine is
    // deterministic; the future approve step addresses changes by their stable TargetId).
    private static IReadOnlyList<ICvTransform> BuildTransforms() =>
    [
        new ClicheTransform(),
        new WeakVerbTransform(),
        new DateNormalizationTransform(),
        new HeadingNormalizationTransform(),
        new GpaStripTransform(),
        new PersonnummerStripTransform(),
        new AtsSanitizationTransform(),
        new SectionReorderTransform(),
        new PhotoStripTransform(),
    ];

    // ATS sanitization only applies to the ATS-plain rendering; the rest apply to both profiles.
    private static bool AppliesToProfile(ProposedChangeKind kind, RenderProfile profile) =>
        kind != ProposedChangeKind.AtsSanitization || profile == RenderProfile.Ats;
}

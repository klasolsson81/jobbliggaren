using System.Collections.Frozen;
using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Review.Rules;
using Jobbliggaren.Infrastructure.Resumes.Sections;

namespace Jobbliggaren.Infrastructure.Resumes.Review;

/// <summary>
/// The deterministic CV-review engine (Fas 4 STEG 9, F4-9, ADR 0071/0074 — NO AI/LLM;
/// unified input Fas 4b PR-4, ADR 0093 §D8). Scores a <see cref="CvReviewContext"/> —
/// built by the staging (<c>FromParsed</c>) or canonical (<c>FromCanonical</c>) adapter —
/// against the versioned F4-7 rubric and produces a per-criterion PASS/WARN/FAIL/
/// NotAssessed verdict with cited evidence (Inv.2), category-primary with no opaque total
/// (Goodhart). ONE engine, one assessment path, regardless of which aggregate supplied
/// the content (D8). Stateless singleton (senior-cto-advisor V-D): consumes only the
/// immutable knowledge-bank ports + the thread-safe NLP analyzer, takes the CV as a
/// method parameter, and never touches the DbContext, the DEK pipeline, or a logger (so
/// CV-PII is never read via a plain query nor logged — Inv.3 / §5).
/// </summary>
internal sealed class CvReviewEngine : ICvReviewEngine
{
    // Threshold posture (rubric v1.2, Fas 4b PR-5 CTO-bind D1 — RETIRES the v1 M1=(a) posture
    // of 2026-06-15, which had the numeric thresholds living as code in the rules): the
    // per-criterion NUMERIC thresholds are versioned DATA in RubricCriterion.Thresholds (named
    // RubricThresholdKeys, e.g. A2 "≥80 %" → thresholds.passRatio 0.8), read fail-loud via
    // RequiredThreshold — no literal fallback. The PROSE signals (AtsPassSignal/AtsFailSignal)
    // remain the user-facing civic explanation; golden drift-guards pin prose↔data agreement
    // where the prose carries the number. A threshold change is now a rubric-asset edit alone
    // (§2.8 minor bump; the version rides on every CvReviewResult, auditably). Detection-shape
    // constants (regex bounds, structural factors) stay CODE per ADR 0093 §D3 "algorithms are
    // code". The §5-correct LISTS (cliché/verb) were already data (IClicheLexicon/IVerbMapper).
    //
    // rubric.CategoryWeights (the cross-category ATS/Visual BLEND) is loaded by RubricLoader but
    // DELIBERATELY UNCONSUMED in F4-9: a cross-category blend exists only to compute an opaque
    // cross-category TOTAL, which ADR 0074 + §5 forbid (Goodhart). Each category bands
    // independently from its own per-criterion weights; CategoryWeights' first consumer is a
    // future F4-10 surface, if any (architect re-pass Note 1 — keep YAGNI observable, not silent).

    // Scoring-mechanism constant (NOT a rubric data-threshold — §5): a Warn earns half its
    // criterion weight toward the secondary category band. The verdict COUNTS are primary.
    private const double WarnCredit = 0.5;

    private readonly IRubricProvider _rubricProvider;
    private readonly IClicheLexicon _clicheLexicon;
    private readonly IVerbMapper _verbMapper;
    private readonly ITextAnalyzer _analyzer;
    private readonly ISpellChecker _spellChecker;
    private readonly ISpellingAllowlist _spellingAllowlist;
    private readonly CvConventions _conventions;
    private readonly CvParsingLexiconData _parsingLexicon;
    private readonly FrozenDictionary<string, ICriterionRule> _rules;

    public CvReviewEngine(
        IRubricProvider rubricProvider,
        IClicheLexicon clicheLexicon,
        IVerbMapper verbMapper,
        ITextAnalyzer analyzer,
        ISpellChecker spellChecker,
        ISpellingAllowlist spellingAllowlist,
        ICvConventionsProvider conventionsProvider,
        CvParsingLexiconData parsingLexicon)
    {
        _rubricProvider = rubricProvider ?? throw new ArgumentNullException(nameof(rubricProvider));
        _clicheLexicon = clicheLexicon ?? throw new ArgumentNullException(nameof(clicheLexicon));
        _verbMapper = verbMapper ?? throw new ArgumentNullException(nameof(verbMapper));
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        _spellChecker = spellChecker ?? throw new ArgumentNullException(nameof(spellChecker));
        _spellingAllowlist = spellingAllowlist ?? throw new ArgumentNullException(nameof(spellingAllowlist));
        ArgumentNullException.ThrowIfNull(conventionsProvider);
        _conventions = conventionsProvider.GetConventions();
        _parsingLexicon = parsingLexicon ?? throw new ArgumentNullException(nameof(parsingLexicon));
        _rules = BuildRules().ToFrozenDictionary(rule => rule.CriterionId, StringComparer.Ordinal);
    }

    public ValueTask<CvReviewResult> ReviewAsync(
        CvReviewContext context, RenderProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var rubric = _rubricProvider.GetRubric();
        var cliches = _clicheLexicon.GetClicheList();
        var verbs = _verbMapper.GetVerbMapping();
        var allowlist = _spellingAllowlist.GetAllowlist();
        var language = context.Language == ResumeLanguage.En ? TextLanguage.English : TextLanguage.Swedish;
        var datedExperiences = BuildDatedExperiences(context);

        // Fas 4b 8b.4b (ADR 0108) — B1's ORDER half, computed ONCE per review (not per criterion)
        // from the linear citation substrate. The SAME analyzer the improvement engine's
        // SectionReorderTransform proposes against: one definition of "the order deviates", so the
        // criterion that JUDGES and the transform that PROPOSES cannot contradict each other.
        var sectionOrder = SectionOrderAnalyzer.Analyze(
            context.LinearText, _parsingLexicon, _conventions);

        var inProfile = rubric.Criteria.Where(c => InProfile(c.Profile, profile)).ToList();

        var scored = new List<CvCriterionVerdict>(inProfile.Count);
        foreach (var criterion in inProfile)
        {
            var evaluation = new CriterionEvaluationContext(
                context, criterion, profile, language, cliches, verbs, _analyzer,
                _spellChecker, allowlist, datedExperiences, sectionOrder, _conventions.FontAllowlist);
            scored.Add(Evaluate(evaluation));
        }

        // PII hardening (ADR 0074 Invariant 1, security-auditor binding obligation): strip any
        // personnummer the user placed in CV text that a criterion's cited span quotes. Done HERE,
        // before criticalFails + categories are derived, so all three views carry redacted verdicts.
        var verdicts = EvidenceRedactor.Redact(scored);

        var criticalFailIds = rubric.CriticalFailIds.ToHashSet(StringComparer.Ordinal);
        var criticalFails = verdicts
            .Where(v => v.Verdict == CriterionVerdict.Fail && criticalFailIds.Contains(v.CriterionId))
            .ToList();

        var categories = BuildCategories(inProfile, verdicts, rubric);
        var assessedCount = verdicts.Count(v => v.Verdict != CriterionVerdict.NotAssessed);

        var result = new CvReviewResult(
            rubric.Version, profile, categories, verdicts, criticalFails, assessedCount, verdicts.Count);
        return ValueTask.FromResult(result);
    }

    private CvCriterionVerdict Evaluate(CriterionEvaluationContext context)
    {
        var criterion = context.Criterion;

        // Pinned "not assessed" (A5/C1) — never a fabricated Pass/Fail (ADR 0071 OQ3, §5). The
        // user-facing reason is versioned knowledge-bank DATA (criterion.NotAssessedReason), not
        // an inline C# literal — relocated so no dev-jargon reaches the job-seeker (CLAUDE.md §10).
        if (criterion.Assessability == CriterionAssessability.NotAssessedV1)
        {
            return CvCriterionVerdict.NotAssessed(criterion.Id, criterion.Category, NotAssessedReason(criterion));
        }

        // A registered rule assesses it; anything without a rule reports honest NotAssessed
        // (missing input signal — layout/file metadata or a target ad), reason carried by data.
        return _rules.TryGetValue(criterion.Id, out var rule)
            ? rule.Evaluate(context)
            : CvCriterionVerdict.NotAssessed(criterion.Id, criterion.Category, NotAssessedReason(criterion));
    }

    private static IEnumerable<ICriterionRule> BuildRules() =>
    [
        new A1MeasurableResultsRule(),
        new A2ActionVerbsRule(),
        new A4GapsRule(),
        new A6ConcretionRule(),
        new A7ClicheRule(),
        new A8ProfileRule(),
        new A9SoftSkillsRule(),
        new A10EducationRule(),
        new B1SectionsRule(),
        new B3ContactRule(),
        new B4PersonnummerRule(),
        // Fas 4b PR-6 (ADR 0093 §D4, CTO-bind D-G): B5 formatting consistency, detected
        // GEOMETRY-FREE from the linearized text (mixed bullet markers) so the verdict is
        // arm-independent — the canonical SetFindingStatus recompute matches the staging
        // review, which is what makes the styleOnly "Ignored" decision reachable e2e.
        new B5ConsistentFormattingRule(),
        // Fas 4b PR-6b (ADR 0093 §D4): B2 page count from the imported PDF's geometry
        // (ICvLayoutAnalyzer). NotAssessed without geometry (canonical arm / DOCX / legacy).
        new B2PageCountRule(),
        new B6DateFormatRule(),
        new B7ChronologyRule(),
        new B8FileNameRule(),
        new C2ToneRule(),
        new C3ActiveVoiceRule(),
        new C4PerspectiveRule(),
        // C5 (Språkkonsistens sv/en) has NO rule — it is NotAssessedV1 in the rubric (#488):
        // the F4-8 detector only picks a DOMINANT document language, so it cannot honestly
        // assess sentence-level sv/en mixing. Evaluate short-circuits to NotAssessed with the
        // asset-authored civic reason (parity A5/C1), never a fabricated Pass (§5 honesty).
        new C6AbbreviationsRule(),
        // Fas 4b PR-6 (ADR 0093 §D4): C7 machine spelling check via the dormant-until-now
        // Hunspell checker + the versioned proper-noun/tech-term allowlist. WARN-posture
        // (misspelling -> Warn, never Fail); C1 (spelling+grammar) stays NotAssessedV1.
        new C7SpellingRule(),
        new D1FileFormatRule(),
        // Fas 4b #891 (ADR 0108): D3 standard body font/size from the ICvLayoutAnalyzer font runs
        // read at import (allowlist = cv-conventions, pt floor = rubric v2.2 threshold). Warn-only;
        // NotAssessed without font runs (canonical arm / DOCX / failed parse / pre-#891 import).
        new D3StandardFontRule(),
        new D6StandardHeadingsRule(),
        // Fas 4b PR-6b (ADR 0093 §D4): D9 file size + E2 whitespace (tightest margin), both
        // from the ICvLayoutAnalyzer metrics read at import. NotAssessed without metrics.
        new D9FileSizeRule(),
        new E2WhitespaceRule(),
    ];

    // Canonical structured dates are month-granular by construction — one shared token,
    // the same one PeriodParser emits for month-granular points, so B6 verdicts them as
    // ONE consistent format (never "blandade" against itself).
    private const string CanonicalFormatToken = "MM/YYYY";

    private static List<DatedExperience> BuildDatedExperiences(CvReviewContext review) =>
        review.Content.Experience
            .Select(experience =>
            {
                // Canonical arm (D8): structured DateOnly maps directly — no period
                // parsing. An ongoing role (open end) uses the same far-future sentinel
                // PeriodParser emits, so gap/chronology maths work without a clock.
                if (experience.StartDate is { } start)
                {
                    return new DatedExperience(
                        experience, start, experience.EndDate ?? DateOnly.MaxValue, CanonicalFormatToken);
                }

                // No structured start (staging always; canonical when date-less, CTO-bind
                // 5a-pre): the freeform period string is date-parsed exactly as before —
                // for a date-less canonical entry PeriodText carries the verbatim RawPeriod.
                return PeriodParser.TryParse(experience.PeriodText, out var s, out var end, out var format)
                    ? new DatedExperience(experience, s, end, format)
                    : new DatedExperience(experience, null, null, null);
            })
            .ToList();

    private static bool InProfile(RubricProfile criterionProfile, RenderProfile renderProfile) =>
        renderProfile switch
        {
            RenderProfile.Ats => criterionProfile is RubricProfile.Both or RubricProfile.AtsOnly,
            RenderProfile.Visual => criterionProfile is RubricProfile.Both or RubricProfile.VisualOnly,
            _ => false,
        };

    private static List<CvCategoryResult> BuildCategories(
        IReadOnlyList<RubricCriterion> inProfile,
        IReadOnlyList<CvCriterionVerdict> verdicts,
        Rubric rubric)
    {
        var weightByCriterion = inProfile.ToDictionary(c => c.Id, c => c.Weight, StringComparer.Ordinal);

        var categories = new List<CvCategoryResult>();
        foreach (var category in verdicts.Select(v => v.Category).Distinct())
        {
            var categoryVerdicts = verdicts.Where(v => v.Category == category).ToList();

            var pass = categoryVerdicts.Count(v => v.Verdict == CriterionVerdict.Pass);
            var warn = categoryVerdicts.Count(v => v.Verdict == CriterionVerdict.Warn);
            var fail = categoryVerdicts.Count(v => v.Verdict == CriterionVerdict.Fail);
            var notAssessed = categoryVerdicts.Count(v => v.Verdict == CriterionVerdict.NotAssessed);

            double creditSum = 0;
            double weightSum = 0;
            foreach (var verdict in categoryVerdicts.Where(v => v.Verdict != CriterionVerdict.NotAssessed))
            {
                var weight = rubric.Weights[weightByCriterion[verdict.CriterionId]];
                weightSum += weight;
                creditSum += verdict.Verdict switch
                {
                    CriterionVerdict.Pass => weight,
                    CriterionVerdict.Warn => WarnCredit * weight,
                    _ => 0,
                };
            }

            // NotAssessed criteria are excluded from the denominator — the determinism never
            // penalises what it cannot assess. A fully-NotAssessed category bands at the floor.
            var band = weightSum > 0
                ? MapBand(rubric, creditSum / weightSum * 100.0)
                : LowestBand(rubric);

            categories.Add(new CvCategoryResult(category, pass, warn, fail, notAssessed, band, categoryVerdicts));
        }

        return categories;
    }

    private static ScoreBandLabel MapBand(Rubric rubric, double percentage)
    {
        var band = rubric.Bands
            .Where(b => b.MinInclusive <= percentage)
            .OrderByDescending(b => b.MinInclusive)
            .FirstOrDefault();
        return band?.Label ?? LowestBand(rubric);
    }

    private static ScoreBandLabel LowestBand(Rubric rubric) =>
        rubric.Bands.OrderBy(b => b.MinInclusive).First().Label;

    // The user-facing reason for a NotAssessed criterion is authored as versioned data in the
    // rubric asset (ADR 0071: reasons-as-data, not inline C#). This code-side fallback is reached
    // ONLY on an N-1 asset that omits the field — it stays civic and jargon-free (CLAUDE.md §10).
    private const string NotAssessedFallbackReason =
        "Det här bedöms inte i den här versionen av granskningen.";

    private static string NotAssessedReason(RubricCriterion criterion) =>
        criterion.NotAssessedReason ?? NotAssessedFallbackReason;
}

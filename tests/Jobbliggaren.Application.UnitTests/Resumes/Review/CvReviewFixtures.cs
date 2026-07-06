using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.KnowledgeBank;
using Jobbliggaren.Infrastructure.Resumes.Parsing;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// Shared builders for the F4-9 CV-review engine tests. Two design choices, both
/// deliberate and reported to CC:
///
/// <list type="number">
/// <item>The knowledge-bank ports are wired to the REAL Infrastructure loaders
/// (<see cref="RubricProvider"/> / <see cref="ClicheLexicon"/> / <see cref="VerbMapper"/>).
/// Golden expectations are derived from the committed assets (rubric.v1.2.0.json etc.) —
/// anti-stale, no guessed thresholds (the prompt's "derive from the real rubric asset"
/// directive).</item>
/// <item>The <see cref="ITextAnalyzer"/> is a real-ish stub. The engine consumes it for
/// the NLP-tier criteria (A2/C3/C4 etc.); for the unit tests we feed a deterministic fake
/// that lowercases + splits on whitespace so the assertions stay on the engine's RULE
/// logic, not on Snowball/PG parity (which has its own integration gate).</item>
/// </list>
///
/// Building a <see cref="ParsedResume"/> in-memory uses the real <see cref="ParsedResume.Create"/>
/// factory (the aggregate accepts a degraded parse, OQ5), so the fixtures exercise the same
/// construction path the import handler does.
/// </summary>
internal static class CvReviewFixtures
{
    internal sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
        public static FixedClock Default =>
            new(new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero));
    }

    // ── Real knowledge-bank ports (committed assets — golden source) ─────

    internal static IRubricProvider RealRubricProvider() => new RubricProvider();
    internal static IClicheLexicon RealClicheLexicon() => new ClicheLexicon();
    internal static IVerbMapper RealVerbMapper() => new VerbMapper();
    internal static Rubric RealRubric() => RealRubricProvider().GetRubric();

    // ── A deterministic ITextAnalyzer stub (lowercase + whitespace split) ─
    // Not PG/Snowball parity — the engine's NLP-tier rules are exercised against a
    // predictable lexeme stream so the RULE behaviour (not the stemmer) is under test.
    internal sealed class WhitespaceTextAnalyzer : ITextAnalyzer
    {
        public IReadOnlyList<string> ToLexemes(string text, TextLanguage language)
        {
            ArgumentNullException.ThrowIfNull(text);
            return text
                .ToLowerInvariant()
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }
    }

    internal static ITextAnalyzer Analyzer() => new WhitespaceTextAnalyzer();

    // ── C7 spelling ports (Fas 4b PR-6, ADR 0093 §D4) ────────────────────
    // The engine now takes an ISpellChecker + ISpellingAllowlist (6-arg ctor). For the
    // targeted engine/citation/redaction/binding unit tests we feed a STUB checker that
    // deems EVERY word correct — so C7 is deterministically Pass and no test asserts against
    // the DSSO/en_US vocabulary (that vocabulary parity is the corpus + C7-rule tests' job).
    // The allowlist is the REAL committed asset (golden source, parity RealRubricProvider).
    private sealed class AllCorrectSpellCheckerStub : ISpellChecker
    {
        public bool Check(string word, TextLanguage language) => true;
        public IReadOnlyList<string> Suggest(string word, TextLanguage language) => [];
    }

    internal static ISpellChecker AllCorrectSpellChecker() => new AllCorrectSpellCheckerStub();

    internal static ISpellingAllowlist RealAllowlist() => new SpellingAllowlistProvider();

    // ── ParsedResume builders ────────────────────────────────────────────

    internal static ParsedExperience Experience(
        string? title = "Backend-utvecklare",
        string? organization = "Acme AB",
        string? period = "2021–2024",
        string? rawText = null,
        IReadOnlyList<string>? bullets = null) =>
        new(title, organization, period,
            rawText ?? BuildEntryRawText(title, organization, period, bullets));

    // The realistic entry shape HeadingDrivenResumeSegmenter emits: a header line
    // (title — organization), the period on its own line, then the description bullets — one
    // per line. The scored "bullets" (A1/A2/A6) are the DESCRIPTION lines, never the header/
    // period block (#487). Defaults to one strong, quantified, action-verb-led bullet so the
    // baseline fixture is a genuinely strong CV; pass `bullets: []` for a header-only entry.
    private static readonly IReadOnlyList<string> DefaultBullets =
        ["Ledde teamet om 8 personer och ökade konverteringen med 23 procent."];

    private static string BuildEntryRawText(
        string? title, string? organization, string? period, IReadOnlyList<string>? bullets)
    {
        var lines = new List<string>();
        var header = string.Join(" — ",
            new[] { title, organization }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (header.Length > 0)
        {
            lines.Add(header);
        }

        if (!string.IsNullOrWhiteSpace(period))
        {
            lines.Add(period);
        }

        lines.AddRange(bullets ?? DefaultBullets);
        return string.Join('\n', lines);
    }

    /// <summary>
    /// Segments raw CV text through the REAL <see cref="HeadingDrivenResumeSegmenter"/> and
    /// builds the <see cref="ParsedResume"/> the engine reviews — the audit's format-test seam
    /// (#487): the engine is exercised against genuine segmenter output, never hand-crafted
    /// rawText the parser would never emit (the header line + own-line period are real).
    /// </summary>
    internal static ParsedResume ResumeFromCvText(
        string cvText, string sourceFileName = "CV_Anna_Andersson.pdf")
    {
        var segmented = new HeadingDrivenResumeSegmenter().Segment(cvText);
        var created = ParsedResume.Create(
            JobSeekerId.New(),
            sourceFileName,
            "application/pdf",
            segmented.DetectedLanguage,
            segmented.Content,
            cvText,
            segmented.Confidence,
            PersonnummerScanOutcome.None,
            [],
            FixedClock.Default);

        return created.Value;
    }

    internal static ParsedEducation Education(
        string? institution = "KTH",
        string? degree = "Civilingenjör",
        string? period = "2016–2021",
        string? rawText = null) =>
        new(institution, degree, period, rawText ?? $"{institution} {degree} {period}");

    internal static ParsedContact CompleteContact() =>
        new("Anna Andersson", "anna.andersson@example.com", "070-123 45 67", "Stockholm");

    internal static ParseConfidence ConfidentConfidence() =>
        ParseConfidence.FromSections(
        [
            new SectionConfidence(ParsedSectionKind.Contact, SectionConfidenceLevel.Confident, ["kontakt hittad"]),
            new SectionConfidence(ParsedSectionKind.Experience, SectionConfidenceLevel.Confident, ["1 post"]),
        ]);

    /// <summary>
    /// Builds a parsed CV with full control over the fields each criterion reads. Defaults
    /// describe a strong CV (quantified bullets, action verbs, complete contact, no
    /// personnummer, textual PDF). Override per-test to drive a specific criterion to
    /// Warn/Fail.
    /// </summary>
    internal static ParsedResume Resume(
        ParsedContact? contact = null,
        string? profile = "Erfaren backend-utvecklare med 8 års erfarenhet inom betalsystem. Levererade 3 plattformsmigrationer.",
        IReadOnlyList<ParsedExperience>? experience = null,
        IReadOnlyList<ParsedEducation>? education = null,
        IReadOnlyList<string>? skills = null,
        IReadOnlyList<string>? languages = null,
        string? rawText = null,
        string sourceFileName = "CV_Anna_Andersson.pdf",
        string sourceContentType = "application/pdf",
        ResumeLanguage? detectedLanguage = null,
        ParseConfidence? confidence = null,
        PersonnummerScanOutcome? personnummer = null)
    {
        var content = new ParsedResumeContent(
            contact ?? CompleteContact(),
            profile,
            experience ?? [Experience()],
            education ?? [Education()],
            skills ?? ["C#", "PostgreSQL", "Kubernetes"],
            languages ?? ["Svenska", "Engelska"]);

        var created = ParsedResume.Create(
            JobSeekerId.New(),
            sourceFileName,
            sourceContentType,
            detectedLanguage ?? ResumeLanguage.Sv,
            content,
            rawText ?? "Anna Andersson\nLedde teamet om 8 personer.",
            confidence ?? ConfidentConfidence(),
            personnummer ?? PersonnummerScanOutcome.None,
            [],
            FixedClock.Default);

        return created.Value;
    }

    // ── Verdict lookup helpers ───────────────────────────────────────────

    internal static CvCriterionVerdict Verdict(CvReviewResult result, string criterionId) =>
        result.Verdicts.Single(v => v.CriterionId == criterionId);

    internal static CriterionVerdict VerdictOf(CvReviewResult result, string criterionId) =>
        Verdict(result, criterionId).Verdict;
}

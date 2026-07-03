using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Improvement.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.KnowledgeBank;

namespace Jobbliggaren.Application.UnitTests.Resumes.Improvement;

/// <summary>
/// Shared builders for the F4-10 (Fas 4 STEG 10, ADR 0074) CV-build/improve engine tests —
/// the propose-and-approve sibling of the F4-9 review engine. Mirrors
/// <c>CvReviewFixtures</c> EXACTLY (same two deliberate choices, reported to CC):
///
/// <list type="number">
/// <item>The knowledge-bank ports are wired to the REAL committed Infrastructure loaders
/// (<see cref="RubricProvider"/> / <see cref="ClicheLexicon"/> / <see cref="VerbMapper"/>),
/// so the golden <c>After</c>-text (the EXACT cliché <c>DropInReplacement</c> /
/// verb <c>SuggestedStrong</c>) is read from <c>cliche-list.v2.json</c> /
/// <c>verb-mapping.v1.json</c> — never guessed
/// (CLAUDE.md §5: "a CV verdict without cited textual evidence" and "synthesising prose the
/// user did not write" are forbidden; the propose step may only resolve a KB-endorsed value).</item>
/// <item>The <see cref="ITextAnalyzer"/> is the same deterministic lowercase+whitespace stub
/// as F4-9 — the engine consumes it for the NLP-tier transform (WeakVerbUpgrade); the unit
/// tests stay on the engine's RULE logic, not Snowball/PG parity (its own integration gate).</item>
/// </list>
///
/// Building a <see cref="ParsedResume"/> in-memory uses the real
/// <see cref="ParsedResume.Create"/> factory (parity F4-9), so the fixtures exercise the same
/// construction path the import handler does. NO QuestPDF — Phase A is the BCL-only engine +
/// contracts; the IDocument renderer is Phase B.
///
/// RED until the F4-10 contract surface ships in Application
/// (<c>…Resumes.Improvement.Abstractions</c>) and <c>CvImprovementEngine</c> ships
/// internal sealed in <c>Jobbliggaren.Infrastructure.Resumes.Improvement</c>.
/// </summary>
internal static class CvImprovementFixtures
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
    internal static ClicheList RealClicheList() => RealClicheLexicon().GetClicheList();
    internal static VerbMapping RealVerbMapping() => RealVerbMapper().GetVerbMapping();

    // ── A deterministic ITextAnalyzer stub (lowercase + whitespace split) ─
    // Parity CvReviewFixtures.WhitespaceTextAnalyzer — the engine's NLP-tier transform
    // (WeakVerbUpgrade) runs against a predictable lexeme stream, so the RULE behaviour
    // (not the stemmer) is under test. Supports both languages (English dispatch test).
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

    // ── ParsedResume builders (parity CvReviewFixtures) ──────────────────

    internal static ParsedExperience Experience(
        string? title = "Backend-utvecklare",
        string? organization = "Acme AB",
        string? period = "2021–2024",
        string? rawText = null) =>
        new(title, organization, period, rawText ?? $"{title}, {organization}, {period}");

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
    /// Builds a parsed CV with full control over the fields each transform reads. Defaults
    /// describe a CLEAN CV (no clichés, strong verbs, normalized dates/headings, no
    /// personnummer/GPA) so a test that overrides nothing should yield ZERO proposed changes
    /// (the honest "no fabricated edit" path). Override per-test to TRIGGER one transform.
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

    // ── Proposed-change lookup helpers ───────────────────────────────────

    internal static IReadOnlyList<ProposedChange> Of(
        CvImprovementResult result, ProposedChangeKind kind) =>
        result.Changes.Where(c => c.Kind == kind).ToList();

    internal static ProposedChange Single(
        CvImprovementResult result, ProposedChangeKind kind) =>
        result.Changes.Single(c => c.Kind == kind);

    internal static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}

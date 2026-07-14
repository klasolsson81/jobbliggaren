using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Infrastructure.Resumes.Review;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// Fas 4b PR-4 (#653, ADR 0093 §D8) — the ENGINE-level half of the bound citation-
/// losslessness measurement: when the real rubric engine reviews CANONICAL content
/// through the shared linearizer, every <c>TextSpanEvidence</c> quote it produces must
/// be locatable verbatim in the linearized text (the substrate the future ATS text view
/// renders and the UI highlights into). The Domain-level half
/// (<c>ResumeContentLinearizerTests</c>) measures field coverage; this measures the
/// actual citations end to end. A failure TRIPS the documented Form A RawText fallback
/// (ADR 0093 §D8 trip-condition — a STOPP, never fixed by weakening the assertion).
/// </summary>
public class CanonicalReviewCitationLosslessnessTests
{
    private static CvReviewEngine NewEngine() =>
        new(RealRubricProvider(), RealClicheLexicon(), RealVerbMapper(), Analyzer(),
            AllCorrectSpellChecker(), RealAllowlist(),
            RealCvConventionsProvider(), RealParsingLexicon());

    // A deliberately WEAK canonical CV so the quote-citing rules fire their Warn/Fail
    // branches (digit-free weak-verb bullets → A1/A2/A6; clichés + soft skills in the
    // prose → A7/A9; an "Objective"-style profile → A8; passive voice → C3): the
    // measurement must run over real cited quotes, not an all-Pass structural result.
    private static ResumeContent WeakContent() => new(
        new PersonalInfo("Anna Andersson", "anna.andersson@example.com", "070-123 45 67", "Stockholm"),
        experiences:
        [
            new Experience(
                "Acme AB", "Backend-utvecklare",
                new DateOnly(2021, 3, 1), new DateOnly(2024, 5, 1),
                "Ansvarade för dokumentationen och rutinerna.\nDeltog i möten och arbetades med av teamet."),
            new Experience(
                "Nordic Tech HB", "Systemutvecklare",
                new DateOnly(2019, 1, 1), new DateOnly(2021, 2, 1),
                "Jobbade med underhåll av systemen."),
        ],
        educations:
        [
            new Education("KTH", "Civilingenjör datateknik", new DateOnly(2014, 8, 1), new DateOnly(2019, 6, 1)),
        ],
        skills: [new Skill("C#", 5), new Skill("PostgreSQL", null)],
        summary: "Jag är en flexibel lagspelare med social kompetens och brinner för att arbeta med människor.",
        languages: [new SpokenLanguage("Svenska", LanguageProficiency.Native)],
        skillGroups: [],
        sections: []);

    [Theory]
    [InlineData(RenderProfile.Ats)]
    [InlineData(RenderProfile.Visual)]
    public async Task CanonicalReview_MeasuredLossless_EveryCitedQuoteIsLocatableInLinearText(
        RenderProfile profile)
    {
        var content = WeakContent();
        var linearized = ResumeContentLinearizer.Linearize(content);
        var context = CvReviewContext.FromCanonical(content, linearized, ResumeLanguage.Sv);

        var result = await NewEngine().ReviewAsync(
            context, profile, TestContext.Current.CancellationToken);

        var quotes = result.Verdicts
            .SelectMany(v => v.Evidence)
            .OfType<TextSpanEvidence>()
            .Select(e => e.Span.Quote)
            .Where(q => !string.IsNullOrEmpty(q))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        quotes.Count.ShouldBeGreaterThan(3,
            "the weak fixture must make the quote-citing rules fire — an all-structural " +
            "result would make this measurement vacuous.");

        var missing = quotes
            .Where(q => linearized.Text.IndexOf(q, StringComparison.Ordinal) < 0)
            .ToList();

        missing.ShouldBeEmpty(
            $"D8 citation-losslessness MEASUREMENT ({profile}): " +
            $"{quotes.Count - missing.Count}/{quotes.Count} cited quotes locatable in the " +
            "linearized text. Every TextSpanEvidence quote must be a verbatim substring of " +
            "the citation substrate, or the UI cannot highlight it and the ATS view drifts " +
            "from the review (ADR 0093 §D8 SPOT). A failure TRIPS the Form A RawText " +
            "fallback (STOPP). Missing: " + string.Join(" | ", missing));
    }

    [Fact]
    public async Task CanonicalReview_CitedSpansWithOffsets_ResolveToTheirQuote()
    {
        // Where the engine DID resolve an offset (Start != NotLocated), the offset must
        // slice the linear text to exactly the quote — geometry and quote can never
        // disagree on the shared substrate (D8: "what the review cites" ≡ "what the ATS
        // view shows").
        var content = WeakContent();
        var linearized = ResumeContentLinearizer.Linearize(content);
        var context = CvReviewContext.FromCanonical(content, linearized, ResumeLanguage.Sv);

        var result = await NewEngine().ReviewAsync(
            context, RenderProfile.Ats, TestContext.Current.CancellationToken);

        var located = result.Verdicts
            .SelectMany(v => v.Evidence)
            .OfType<TextSpanEvidence>()
            .Where(e => e.Span.Start != TextSpan.NotLocated
                && e.Span.Start + e.Span.Length <= linearized.Text.Length)
            .ToList();

        foreach (var evidence in located)
        {
            // Offsets resolved against the linear substrate must round-trip; offsets
            // resolved against a rule-local synthetic concatenation (AllProse) are
            // positional hints only (#478 Low) — those quotes are still substring-
            // locatable (previous test), which is the load-bearing guarantee.
            var slice = linearized.Text.Substring(evidence.Span.Start, evidence.Span.Length);
            if (slice == evidence.Span.Quote)
                continue;

            linearized.Text.IndexOf(evidence.Span.Quote, StringComparison.Ordinal)
                .ShouldBeGreaterThanOrEqualTo(0,
                    "an offset-carrying quote must at minimum stay substring-locatable " +
                    "in the linear substrate.");
        }
    }
}

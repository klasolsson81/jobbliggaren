using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Queries.ReviewParsedResume;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Review;
using Jobbliggaren.Infrastructure.Resumes.Review.Rules;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// #1062 B2 — a cited quote longer than <see cref="ReviewText.ExcerptMaxChars"/> is shortened ON A
/// WORD BOUNDARY and FLAGGED as an excerpt, instead of being cut mid-word and presented as if it
/// were the whole thing. Pre-fix the four A8 call sites each did <c>text[..80]</c>, so a CV whose
/// profile ran past 80 characters was cited as "… Jag ä" — the engine attributing a fragment it
/// created to the user, on the PASS path as much as the FAIL paths.
///
/// <para>The trailing "…" is deliberately NOT here. It is the client's, because two invariants
/// pinned elsewhere forbid it in the quote: <c>source.Substring(Start, Length) == Quote</c>
/// (<see cref="ReviewTextSpanTests"/>) and <c>Span.Length == Quote.Length</c>
/// (<see cref="CvReviewEvidenceRedactionTests"/>). An ellipsis is not in the CV, so a quote
/// carrying one would fail to locate and every truncated citation would silently degrade to
/// <see cref="TextSpan.NotLocated"/>. Both invariants are re-asserted here against a SHORTENED
/// span, which is the case they were never exercised on.</para>
/// </summary>
public class ReviewTextExcerptTests
{
    // Every fixture below was MEASURED against the cap before it was written down, and each one
    // carries its own guard asserting the property it was chosen for. A fixture that quietly
    // stopped exercising the defect (e.g. a re-worded profile whose cap happened to land on a
    // space) would otherwise make these tests pass without measuring anything.

    /// <summary>Cap falls MID-WORD: char 80 is a letter, so a hard cut ends inside "Jag".</summary>
    private const string MidWordAtCap =
        "Erfaren systemutvecklare med bred bakgrund inom betalsystem och integrationer. Jag är van vid att leda.";

    /// <summary>Cap falls exactly ON whitespace — the prefix already ends a word, nothing to back off.</summary>
    private const string SpaceAtCap =
        "Erfaren systemutvecklare med bred bakgrund inom betalsystem och integrationernaX och mer text efter kapet";

    /// <summary>A profile whose first 80 characters span TWO lines — the newline must survive the
    /// excerpt, because the second half of B2 is that the client renders it (white-space: pre-line)
    /// instead of collapsing two profile lines into one sentence the user never wrote.</summary>
    private const string MultiLine =
        "Erfaren systemutvecklare inom betalsystem.\nJag söker en roll där jag kan leda ett team framåt.";

    /// <summary>No whitespace at all within the cap — there is no word boundary to find.</summary>
    private const string Unbroken =
        "Aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    // ── ReviewText.Excerpt — the one home ────────────────────────────────

    [Fact]
    public void Excerpt_ShouldReturnTheTextUnchanged_WhenItFitsTheCap()
    {
        const string shortProfile = "Erfaren backend-utvecklare inom betalsystem.";
        shortProfile.Length.ShouldBeLessThanOrEqualTo(ReviewText.ExcerptMaxChars);

        var (quote, isExcerpt) = ReviewText.Excerpt(shortProfile);

        quote.ShouldBe(shortProfile);
        isExcerpt.ShouldBeFalse("a text nobody shortened must not claim to be an excerpt.");
    }

    [Fact]
    public void Excerpt_ShouldCutOnAWordBoundary_WhenTheCapFallsMidWord()
    {
        // Fixture guard — this is the control the test must cross. If the cap ever fell on
        // whitespace here, a hard cut would already end a word and the assertions below would
        // hold with no word-boundary logic in the code at all.
        char.IsWhiteSpace(MidWordAtCap[ReviewText.ExcerptMaxChars]).ShouldBeFalse(
            "fixture guard: the cap must land mid-word, or this test proves nothing.");

        var (quote, isExcerpt) = ReviewText.Excerpt(MidWordAtCap);

        isExcerpt.ShouldBeTrue();
        quote.ShouldBe(MidWordAtCap[..quote.Length],
            "the excerpt is a PREFIX of the cited text — that is what keeps it locatable.");
        quote.Length.ShouldBeLessThanOrEqualTo(ReviewText.ExcerptMaxChars);
        char.IsWhiteSpace(MidWordAtCap[quote.Length]).ShouldBeTrue(
            "the character the excerpt stops before is whitespace — i.e. it ended a whole word.");
        quote.ShouldBe(quote.TrimEnd(), "no dangling separator before the client's ellipsis.");
    }

    [Fact]
    public void Excerpt_ShouldKeepTheFullCapWidth_WhenTheCapFallsOnWhitespace()
    {
        char.IsWhiteSpace(SpaceAtCap[ReviewText.ExcerptMaxChars]).ShouldBeTrue(
            "fixture guard: this fixture exists for the cap-lands-on-a-space branch.");

        var (quote, isExcerpt) = ReviewText.Excerpt(SpaceAtCap);

        isExcerpt.ShouldBeTrue();
        quote.Length.ShouldBe(ReviewText.ExcerptMaxChars,
            "the prefix already ends a word, so backing off to an earlier space would throw away "
            + "a whole word for nothing.");
        SpaceAtCap.ShouldStartWith(quote);
    }

    [Fact]
    public void Excerpt_ShouldKeepTheLineBreak_WhenItFallsInsideTheExcerpt()
    {
        MultiLine.IndexOf('\n').ShouldBeLessThan(ReviewText.ExcerptMaxChars,
            "fixture guard: the newline must be INSIDE the cap for this test to measure anything.");

        var (quote, isExcerpt) = ReviewText.Excerpt(MultiLine);

        isExcerpt.ShouldBeTrue();
        quote.Contains('\n').ShouldBeTrue(
            "the excerpt keeps the CV's own line structure — flattening it would present two "
            + "profile lines as one sentence the user never wrote (B2b, rendered by pre-line).");
    }

    [Fact]
    public void Excerpt_ShouldFallBackToTheHardCut_WhenTheCapHoldsNoWordBoundary()
    {
        var (quote, isExcerpt) = ReviewText.Excerpt(Unbroken);

        isExcerpt.ShouldBeTrue();
        quote.Length.ShouldBe(ReviewText.ExcerptMaxChars,
            "with no whitespace to cut at there is no boundary to find; the alternative is sending "
            + "the whole unbounded run, which is exactly what the cap exists to prevent.");
        Unbroken.ShouldStartWith(quote);
    }

    [Theory]
    [InlineData(MidWordAtCap)]
    [InlineData(SpaceAtCap)]
    [InlineData(MultiLine)]
    [InlineData(Unbroken)]
    public void Excerpt_ShouldNeverPutAnEllipsisInTheQuote_ForAnyShortenedText(string text)
    {
        // THE trap this whole design routes around: an ellipsis in the quote is not in the source,
        // so the span would stop resolving and every truncated citation would degrade to
        // NotLocated. The marker is the client's job; the flag is how it learns to draw it.
        var (quote, isExcerpt) = ReviewText.Excerpt(text);

        isExcerpt.ShouldBeTrue("fixture guard: all four fixtures are longer than the cap.");
        quote.ShouldNotContain("…");
        quote.ShouldNotEndWith("...");
    }

    // ── SpanExcerpt — the two pinned invariants, on a SHORTENED span ─────

    [Fact]
    public void SpanExcerpt_ShouldKeepBothPinnedInvariants_WhenTheQuoteIsShortened()
    {
        var source = $"Anna Andersson\n{MidWordAtCap}\nStockholm";

        var span = ReviewText.SpanExcerpt(source, MidWordAtCap, "profiltext").Span;

        span.IsExcerpt.ShouldBeTrue();
        span.Start.ShouldNotBe(TextSpan.NotLocated,
            "a word-boundary excerpt is still a prefix of the cited text, so it stays locatable.");
        source.Substring(span.Start, span.Length).ShouldBe(span.Quote,
            "ReviewTextSpanTests' offset invariant must survive shortening.");
        span.Length.ShouldBe(span.Quote.Length,
            "CvReviewEvidenceRedactionTests' length invariant must survive shortening.");
    }

    [Fact]
    public void SpanExcerpt_ShouldNotFlagAnExcerpt_WhenTheCitedTextFitsTheCap()
    {
        const string profile = "Erfaren backend-utvecklare inom betalsystem.";
        var source = $"Anna Andersson\n{profile}";

        var span = ReviewText.SpanExcerpt(source, profile, null).Span;

        span.IsExcerpt.ShouldBeFalse();
        span.Quote.ShouldBe(profile, "an unshortened citation is still the verbatim whole.");
    }

    // ── End-to-end: the PASS path on an otherwise clean CV ──────────────

    [Fact]
    public async Task ReviewAsync_ShouldCiteAWordBoundedExcerpt_OnA8sPassPath()
    {
        // Call site 399 is A8's PASS branch, so pre-fix the mid-word cut sat on a "Godkänt"
        // verdict on a CV with nothing wrong with it — not only on weak CVs. This runs the real
        // engine over a real ParsedResume; nothing about the assertion is hand-built.
        var resume = Resume(profile: MidWordAtCap, rawText: $"Anna Andersson\n{MidWordAtCap}");

        var result = await ReviewAsync(resume);

        var a8 = Verdict(result, "A8");
        a8.Verdict.ShouldBe(CriterionVerdict.Pass,
            "the fixture profile is within the rubric's word bound and trips no Fail branch.");

        var span = a8.Evidence.ShouldHaveSingleItem().ShouldBeOfType<TextSpanEvidence>().Span;
        span.IsExcerpt.ShouldBeTrue("the profile is longer than the cap, so the citation is an excerpt.");
        span.Quote.ShouldNotBe(MidWordAtCap[..ReviewText.ExcerptMaxChars],
            "the pre-fix hard cut ended inside 'Jag' — that exact string is the regression.");
        char.IsWhiteSpace(MidWordAtCap[span.Quote.Length]).ShouldBeTrue(
            "the cited excerpt ends a whole word of the user's own text.");
        resume.RawText.Substring(span.Start, span.Length).ShouldBe(span.Quote,
            "the excerpt still resolves against the CV's raw text end-to-end.");
    }

    // ── The excerpt fact must survive the seams downstream ──────────────

    [Fact]
    public void EvidenceRedactor_ShouldKeepTheExcerptFlag_WhenTheQuoteCarriedAPersonnummer()
    {
        // Fork 3B rebuilds the span POSITIONALLY (new TextSpan(0, 0, redactedQuote)) — the one
        // shape that can drop an additive field silently. Redaction masks a personnummer; it does
        // not lengthen the citation, so a shortened quote is still shortened afterwards. Losing
        // the flag here would restore the implied "this is your whole sentence" claim on exactly
        // the CVs that carry a personnummer.
        const string pnr = "811218-9876";
        const string mask = "******-****";
        var profile = $"Erfaren systemutvecklare inom betalsystem, pnr {pnr}, med lång erfarenhet av integrationer.";
        profile.Length.ShouldBeGreaterThan(ReviewText.ExcerptMaxChars, "fixture guard.");
        profile.IndexOf(pnr, StringComparison.Ordinal).ShouldBeLessThan(ReviewText.ExcerptMaxChars,
            "fixture guard: the personnummer must fall INSIDE the excerpt, or fork 3B never fires.");

        var evidence = ReviewText.SpanExcerpt($"Anna Andersson\n{profile}", profile, "profiltext");
        evidence.Span.IsExcerpt.ShouldBeTrue("precondition: the input span is an excerpt.");
        var verdict = CvCriterionVerdict.Assessed(
            "A8", RubricCategory.Content, CriterionVerdict.Pass, [evidence]);

        var redacted = EvidenceRedactor.Redact([verdict]).ShouldHaveSingleItem();

        var span = redacted.Evidence.ShouldHaveSingleItem().ShouldBeOfType<TextSpanEvidence>().Span;
        span.Quote.Contains(mask, StringComparison.Ordinal).ShouldBeTrue(
            "precondition: fork 3B fired (the quote was masked).");
        span.Start.ShouldBe(0, "precondition: fork 3B zeroes the offset.");
        span.IsExcerpt.ShouldBeTrue("the excerpt fact survives redaction.");
    }

    [Fact]
    public void CvReviewDtoMapper_ShouldTransportTheExcerptFlag_ToTheClient()
    {
        // The flag is what lets the client draw the "…" the engine refuses to write into the
        // quote. If it stopped at the Application boundary the whole design would be inert.
        var evidence = ReviewText.SpanExcerpt($"Anna Andersson\n{MidWordAtCap}", MidWordAtCap, null);
        var verdict = CvCriterionVerdict.Assessed(
            "A8", RubricCategory.Content, CriterionVerdict.Pass, [evidence]);
        var result = new CvReviewResult(
            RealRubric().Version, RenderProfile.Ats,
            Categories: [], Verdicts: [verdict], CriticalFails: [], AssessedCount: 1, TotalCount: 1);

        var dto = result.ToDto(new Dictionary<string, string>());

        var cited = dto.Verdicts.ShouldHaveSingleItem().Evidence.ShouldHaveSingleItem();
        cited.IsExcerpt.ShouldBeTrue();
        cited.Quote!.Contains('…').ShouldBeFalse(
            "the wire carries the verbatim substring, never the marker.");
    }

    [Fact]
    public void CvReviewDtoMapper_ShouldNeverFlagStructuralEvidenceAsAnExcerpt()
    {
        // A structural observation is a fact the engine states, not a quote it shortened —
        // it has no "rest of the sentence" for a marker to point at.
        var verdict = CvCriterionVerdict.Assessed(
            "B3", RubricCategory.Structure, CriterionVerdict.Fail,
            [ReviewText.Structural("e-post saknas")]);
        var result = new CvReviewResult(
            RealRubric().Version, RenderProfile.Ats,
            Categories: [], Verdicts: [verdict], CriticalFails: [], AssessedCount: 1, TotalCount: 1);

        var dto = result.ToDto(new Dictionary<string, string>());

        var cited = dto.Verdicts.ShouldHaveSingleItem().Evidence.ShouldHaveSingleItem();
        cited.Kind.ShouldBe("Structural");
        cited.IsExcerpt.ShouldBeFalse();
    }

    private static async Task<CvReviewResult> ReviewAsync(ParsedResume resume) =>
        await new CvReviewEngine(
                RealRubricProvider(), RealClicheLexicon(), RealVerbMapper(), Analyzer(),
                AllCorrectSpellChecker(), RealAllowlist(),
                RealCvConventionsProvider(), RealParsingLexicon())
            .ReviewAsync(
                CvReviewContext.FromParsed(resume), RenderProfile.Ats,
                TestContext.Current.CancellationToken);
}

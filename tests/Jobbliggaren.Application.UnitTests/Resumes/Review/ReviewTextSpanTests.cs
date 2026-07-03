using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Queries.ReviewParsedResume;
using Jobbliggaren.Infrastructure.Resumes.Review;
using Jobbliggaren.Infrastructure.Resumes.Review.Rules;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// F4-9 (#478 Low) — the cited-evidence span builders must never FABRICATE an offset. When a
/// quote/phrase cannot be located in its source, the span carries the verbatim
/// <see cref="TextSpan.Quote"/> but <see cref="TextSpan.Start"/> == <see cref="TextSpan.NotLocated"/>
/// (an honest "position unknown"), never the plausible-looking 0. The located case still resolves
/// the exact offset (<c>source.Substring(Start, Length) == Quote</c>), so the fix does not weaken a
/// genuine citation.
/// </summary>
public class ReviewTextSpanTests
{
    private const string Source = "Ledde teamet om 8 personer";

    [Fact]
    public void Span_ShouldResolveExactOffset_WhenQuoteIsPresent()
    {
        var span = ReviewText.Span(Source, "8 personer").Span;

        span.Start.ShouldBe(16);
        Source.Substring(span.Start, span.Length).ShouldBe("8 personer");
    }

    [Fact]
    public void Span_ShouldMarkNotLocated_WhenQuoteIsAbsent()
    {
        // Pre-fix this silently returned Start == 0, claiming the quote sat at the START of the
        // source — a fabricated position. Now it is TextSpan.NotLocated, an honest "not located".
        var span = ReviewText.Span(Source, "chef för avdelningen").Span;

        span.Start.ShouldBe(TextSpan.NotLocated);
        span.Quote.ShouldBe("chef för avdelningen"); // verbatim ground truth preserved
    }

    [Fact]
    public void Span_ShouldMarkNotLocated_WhenQuoteIsEmpty()
    {
        ReviewText.Span(Source, string.Empty).Span.Start.ShouldBe(TextSpan.NotLocated);
    }

    [Fact]
    public void SpanWord_ShouldResolveWordBoundedOffset_WhenPhraseIsPresent()
    {
        var span = ReviewText.SpanWord("En flexibel och driven person", "flexibel").Span;

        span.Start.ShouldBe(3);
        span.Quote.ShouldBe("flexibel");
    }

    [Fact]
    public void SpanWord_ShouldMarkNotLocated_WhenPhraseIsAbsent()
    {
        // A word-bounded phrase absent from the source must not be cited at offset 0 either.
        var span = ReviewText.SpanWord(Source, "flexibel").Span;

        span.Start.ShouldBe(TextSpan.NotLocated);
        span.Quote.ShouldBe("flexibel");
    }

    [Fact]
    public void SpanWord_ShouldMarkNotLocated_WhenPhraseIsEmpty()
    {
        // An empty phrase has no word-bounded occurrence — WordSpans yields nothing, so the builder
        // falls to the NotLocated sentinel rather than fabricating a zero-length span at offset 0.
        ReviewText.SpanWord(Source, string.Empty).Span.Start.ShouldBe(TextSpan.NotLocated);
    }

    // ── #478 Low 1 augmentation: the NotLocated sentinel must propagate intact ──
    // Downstream consumers (the PII EvidenceRedactor and the Application-boundary DTO mapper) must
    // carry a NotLocated span through unchanged — never crash on the -1 offset, and never clobber it
    // back to a plausible-looking 0. These are propagation guards, not red-vs-pre-fix cases: they
    // exercise the redactor/mapper (not ReviewText.Span), so they hold before and after the fix and
    // pin that no downstream stage silently launders "not located" into the valid position 0.

    [Fact]
    public void EvidenceRedactor_ShouldPreserveNotLocatedStart_WhenTheQuoteCarriesNoPersonnummer()
    {
        // The redactor ZEROES Span.Start only for a quote that covered a personnummer (Fork 3B). A
        // NotLocated, pnr-free quote must survive with Start == NotLocated — the pnr-zeroing path
        // (which would rewrite Start to 0) must not fire and mask the honest "not located".
        var notLocated = new TextSpanEvidence(
            new TextSpan(TextSpan.NotLocated, "chef för avdelningen".Length, "chef för avdelningen"), "note");
        var verdict = CvCriterionVerdict.Assessed(
            "A1", RubricCategory.Content, CriterionVerdict.Fail, [notLocated]);

        var redacted = EvidenceRedactor.Redact([verdict]).ShouldHaveSingleItem();

        var span = redacted.Evidence.ShouldHaveSingleItem().ShouldBeOfType<TextSpanEvidence>().Span;
        span.Start.ShouldBe(TextSpan.NotLocated, "a pnr-free NotLocated span keeps its sentinel (no zeroing).");
        span.Quote.ShouldBe("chef för avdelningen", "the verbatim quote is preserved through redaction.");
    }

    [Fact]
    public void CvReviewDtoMapper_ShouldExposeNotLocatedStartAsMinusOne_WhenTheSpanIsNotLocated()
    {
        // The Application-boundary DTO must transport the sentinel verbatim (-1), not throw and not
        // normalize it away — the client decides how to render a "position unknown" span.
        var notLocated = new TextSpanEvidence(
            new TextSpan(TextSpan.NotLocated, "flexibel".Length, "flexibel"), null);
        var verdict = CvCriterionVerdict.Assessed(
            "C3", RubricCategory.Language, CriterionVerdict.Warn, [notLocated]);
        var result = new CvReviewResult(
            RealRubric().Version, RenderProfile.Ats,
            Categories: [], Verdicts: [verdict], CriticalFails: [], AssessedCount: 1, TotalCount: 1);

        var dto = result.ToDto(new Dictionary<string, string>());

        var evidence = dto.Verdicts.ShouldHaveSingleItem().Evidence.ShouldHaveSingleItem();
        evidence.Kind.ShouldBe("TextSpan");
        evidence.Start.ShouldBe(TextSpan.NotLocated, "the DTO carries the -1 sentinel verbatim, never a fabricated 0.");
        evidence.Quote.ShouldBe("flexibel");
    }

    [Fact]
    public async Task Span_ShouldFlowNotLocatedThroughAWholeReview_WhenACitedBulletIsNotASubstringOfRawText()
    {
        // End-to-end proof the sentinel reaches a real verdict. A1 cites its offending bullet against
        // context.RawText (the whole-CV raw text), while the bullet comes from the experience entry —
        // a shape the segmenter can produce when a per-section RawText is not a verbatim substring of
        // the top-level RawText (normalized whitespace/newlines). Here the top-level RawText is set so
        // the digit-free bullet is genuinely absent from it. Pre-fix A1 would have reported Start == 0
        // (fabricated, claiming the bullet sits at the CV's very start); post-fix it honestly reports
        // NotLocated. The review must complete without throwing.
        const string bullet = "Samordnade interna möten utan mätbart resultat.";
        var resume = Resume(
            experience: [Experience(rawText: $"Backend-utvecklare\n{bullet}")],
            rawText: "Anna Andersson"); // top-level raw text does NOT contain the experience bullet

        var engine = new CvReviewEngine(
            RealRubricProvider(), RealClicheLexicon(), RealVerbMapper(), Analyzer());

        var result = await engine.ReviewAsync(resume, RenderProfile.Ats, TestContext.Current.CancellationToken);

        var a1 = Verdict(result, "A1");
        a1.Verdict.ShouldBe(CriterionVerdict.Fail, "a digit-free experience fails the measurable-results criterion.");
        var span = a1.Evidence.ShouldHaveSingleItem().ShouldBeOfType<TextSpanEvidence>().Span;
        span.Start.ShouldBe(TextSpan.NotLocated,
            "the unlocatable bullet is cited as NotLocated end-to-end, never a fabricated offset 0.");
        span.Quote.ShouldBe(bullet, "the verbatim bullet stays the ground truth even when its offset is unknown.");
    }
}

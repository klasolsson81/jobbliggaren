using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Review.Rules;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// #1060 β-3 follow-up — <c>ReviewText.DescriptionLines</c>' period test is a UNION
/// (<c>PeriodParser.TryParse(line, …) || DatePatterns.IsDateOnlyLine(line)</c>), and this class
/// exists to make that union's two halves INDIVIDUALLY load-bearing. Neither predicate subsumes
/// the other, so a substitution in either direction is a suppression regression.
///
/// <para><b>What a released row actually reaches, corrected after measurement.</b> An earlier
/// revision of this docblock said <c>DescriptionLines</c> feeds <c>WeakVerbTransform</c>, "which
/// proposes a rewrite of every bullet", and cited CLAUDE.md §5 "synthesising prose the user did not
/// write". Both halves are false. <c>WeakVerbTransform.Propose</c> emits a change only for a bullet
/// OPENING with a drop-in-safe weak verb from the KnowledgeBank mapping, and a date row opens with a
/// digit or a month abbreviation — it is offered the row and DECLINES it; the replacement is a
/// verbatim KnowledgeBank value, so it could not synthesise even if it fired.
///
/// <para>The consumer that does act on a released row is the REVIEW side, via
/// <c>ReviewText.ExperienceBullets</c> → A1/A2/A6, and the class is the INVERSE of the one first
/// cited: §5's "a CV verdict without cited textual evidence" — a verdict citing a span that is not
/// prose. Sharpest on <c>YYYY/MM</c>, which <c>DateRange</c> models on neither endpoint, so
/// <c>StripDates</c> leaves digits behind and A1 can read the employment dates as a quantified
/// result. <b>That A1/A2/A6 consequence is DERIVED from reading the rules, not run</b>
/// (senior-cto-advisor re-bind 2026-08-02); the date-model widening owns measuring it. What IS run
/// and pinned here is the escape itself.</para>
///
/// <para><b>The pin's whole purpose is to redden under EITHER substitution</b>, which is why the
/// two directions are separate test methods with disjoint inputs:
/// <list type="bullet">
/// <item>Drop the <c>DatePatterns</c> disjunct → <see cref="DescriptionLines_ShouldSuppressTheDateRow_WhereOnlyDatePatternsReachesIt"/> reddens.</item>
/// <item>Drop the <c>PeriodParser</c> disjunct → <see cref="DescriptionLines_ShouldStillSuppressTheDateRow_WhereOnlyPeriodParserReachesIt"/> reddens.</item>
/// </list>
/// A pin that passes under both forms measures nothing, so neither method may be merged into the
/// other or widened to inputs both predicates reach.</para>
///
/// <para><b>Why THIS layout, and it is load-bearing.</b> Every case runs the REAL
/// <c>HeadingDrivenResumeSegmenter</c> over a "Title / Company / Dates / bullet" CV — the layout
/// <c>DescriptionLines</c>' own docblock names. The date row is then <c>Lines[2]</c>, the
/// organisation is genuinely "Acme AB", and the organisation-equality test at the call site
/// CANNOT fire on the date row. That is what isolates the period test: in the two-line
/// "Title / Dates" layout the segmenter fabricates the date row into <c>Organization</c> and the
/// equality test suppresses it as a side effect, so the period test would be measured through an
/// accident rather than on its own. No hand-built <c>ReviewableExperience</c> appears here — the
/// premise is produced end to end by <c>HeadingDrivenResumeSegmenter</c> →
/// <c>CvReviewContext.FromParsed</c> (CLAUDE.md §5, Tests).</para>
/// </summary>
public class ReviewTextPeriodLineUnionTests
{
    private const string Bullet = "Ökade konverteringen med 23 procent.";

    // The segmenter output both readers score, produced by the real parser — never hand-built.
    private static (IReadOnlyList<string> Review, IReadOnlyList<string> Improve, string? Organization)
        BulletsFor(string dateLine)
    {
        var cv = $"""
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Systemutvecklare
            Acme AB
            {dateLine}
            {Bullet}
            """;

        var parsed = ResumeFromCvText(cv);
        var staging = parsed.Content.Experience.ShouldHaveSingleItem();
        var reviewable = CvReviewContext.FromParsed(parsed).Content.Experience.ShouldHaveSingleItem();

        // BOTH overloads, because they have different readers and the same job: the
        // ReviewableExperience one is the review engine's (A1/A2/A6), the ParsedExperience one is
        // WeakVerbTransform's (#534). A union that suppressed on one arm only would be a
        // half-closed hole, and (b6) names the improve arm specifically.
        return (
            ReviewText.DescriptionLines(reviewable).ToList(),
            ReviewText.DescriptionLines(staging).ToList(),
            staging.Organization);
    }

    private static void ShouldSuppress(string dateLine, string because)
    {
        var (review, improve, organization) = BulletsFor(dateLine);

        organization.ShouldBe("Acme AB",
            "the organisation must be the real employer, so organisation-equality cannot be what " +
            "suppresses the date row — that is what isolates the period test.");
        review.ShouldBe([Bullet], because);
        improve.ShouldBe([Bullet],
            $"WeakVerbTransform scores the same unit and must not be offered the date row — {because}");
    }

    [Fact]
    public void DescriptionLines_ShouldSuppressTheDateRow_WhereOnlyDatePatternsReachesIt()
    {
        // THE β-3 RESIDUAL THE UNION CLOSED — this line was yielded as a description bullet
        // before the promotion. PeriodParser anchors ^…$ and splits on the first separator, so a
        // LEADING "– " leaves it an empty left point and it refuses the line; DatePatterns
        // matches unanchored and trims the separator behind the match.
        //
        // THIS METHOD IS THE KILL FOR "union → PeriodParser only". If the DatePatterns disjunct
        // is ever removed, this reddens and nothing else in the suite does.
        const string dateLine = "– 2020 – 2024";

        PeriodParser.TryParse(dateLine, out _, out _, out _).ShouldBeFalse(
            "premise: PeriodParser declines this form, so only the DatePatterns half can suppress it.");
        DatePatterns.IsDateOnlyLine(dateLine).ShouldBeTrue("premise: DatePatterns reaches it.");

        ShouldSuppress(dateLine,
            "a date row with a leading separator is not a description bullet (the β-3 residual).");
    }

    [Theory]
    [InlineData("2019 till 2021", "the word separator 'till'")]
    [InlineData("3/2020 – 6/2024", "a single-digit month")]
    [InlineData("2020-06 – 2024-03", "an ISO YYYY-MM end point")]
    public void DescriptionLines_ShouldStillSuppressTheDateRow_WhereOnlyPeriodParserReachesIt(
        string dateLine, string axis)
    {
        // THE TRAP, PINNED. These are the forms a SUBSTITUTION would have lost: PeriodParser is
        // wider here and DatePatterns declines all three, so swapping the call site to a
        // DatePatterns-only predicate hands them to the bullet scorer and to WeakVerbTransform.
        //
        // THIS METHOD IS THE KILL FOR "union → DatePatterns only". The third case is an axis the
        // written three-axis table does not name — found by measuring, not by reading it:
        // DateRange's end-alternation takes the bare \d{4} of "2024" first and leaves "-03" as a
        // non-empty tail, so the whole line is never consumed.
        PeriodParser.TryParse(dateLine, out _, out _, out _).ShouldBeTrue(
            $"premise: PeriodParser reaches this form via {axis}.");
        DatePatterns.IsDateOnlyLine(dateLine).ShouldBeFalse(
            $"premise: DatePatterns declines it, so only the PeriodParser half can suppress it — {axis}.");

        ShouldSuppress(dateLine,
            $"suppression must be a strict SUPERSET, never a substitution — {axis}.");
    }

    [Fact]
    public void DescriptionLines_ShouldStillYieldARealBullet_WhenTheEntryCarriesOne()
    {
        // The pin above cannot be satisfied by suppressing everything. A prose bullet carrying a
        // digit — the shape A1 scores as a measurable result — must survive both halves of the
        // union, or "fewer bullets scored" becomes "no bullets scored" and every A-criterion
        // silently degrades to NotAssessed.
        var (review, improve, _) = BulletsFor("2013 - 2021");

        review.ShouldBe([Bullet]);
        improve.ShouldBe([Bullet]);
        DatePatterns.IsDateOnlyLine(Bullet).ShouldBeFalse();
        PeriodParser.TryParse(Bullet, out _, out _, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("jan 2020 – dec 2024")]
    [InlineData("2020 – 2024 (heltid)")]
    [InlineData("2020/01 – 2024/12")]
    [InlineData("2020 –")]
    public void DescriptionLines_ShouldStillYieldTheDateRowAsABullet_WhenNeitherPredicateModelsTheForm(
        string dateLine)
    {
        // ACCEPTED-AND-KNOWN, AND A LIVE ESCAPE — pinned because a comment claiming the union
        // closed the residual would be FALSE about exactly these four, on exactly this layout.
        //
        // These are the segmenter pin's frozen four. On the two-line "Title / Dates" layout they
        // are suppressed here, but only as a side effect: the segmenter fabricates them into
        // Organization and the organisation-equality test fires on them. On THIS layout the
        // employer is real, nothing fabricates them, and neither half of the union reaches them —
        // so the date row reaches the bullet scorer and WeakVerbTransform TODAY. That is
        // unchanged by the promotion, which factored today's model into one home and inherited its
        // blind spot; it is not a regression this change introduced, and it is not closed.
        //
        // THE TRIGGER THAT REDDENS THIS IS THE DatePatterns WIDENING — month names, trailing
        // qualifiers, keyword-less open ends, YYYY/MM — which is the deferred follow-up PR. When
        // it lands, these four move to the suppressed side and this test is REPLACED by that move,
        // not edited to keep it green. Until then the escape is measured rather than assumed away.
        var (review, improve, organization) = BulletsFor(dateLine);

        organization.ShouldBe("Acme AB");
        review.ShouldBe([dateLine, Bullet],
            "neither predicate models this form and the employer is real, so nothing suppresses the date row.");
        improve.ShouldBe([dateLine, Bullet],
            "WeakVerbTransform is offered the date row today — the cost the widening removes.");
    }
}

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
/// the other, so a substitution in either direction is a suppression regression. The axes on which
/// each is wider — and the <c>InlineData</c> adjudicating them — live in ONE home,
/// <c>DatePatternsDateOnlyLineTests</c>; no total is published there or here.
///
/// <para><b>What a released row actually reaches, corrected after measurement.</b> An earlier
/// revision of this docblock said <c>DescriptionLines</c> feeds <c>WeakVerbTransform</c>, "which
/// proposes a rewrite of every bullet", and cited CLAUDE.md §5 "synthesising prose the user did not
/// write". Both halves are false. <c>WeakVerbTransform.Propose</c> emits a change only for a bullet
/// OPENING with a drop-in-safe weak verb from the KnowledgeBank mapping, and NO date row opens with
/// one — it is offered the row and DECLINES it; the replacement is a verbatim KnowledgeBank value,
/// so it could not synthesise even if it fired. (The transform IS offered the row: its bullet unit
/// is <c>DescriptionLines</c> itself — read at <c>WeakVerbTransform.cs:34</c>, which iterates this
/// method; no test HERE calls <c>Propose</c> (<c>CvImprovementEngineTests</c> does, via
/// <c>SuggestAsync</c> — an unqualified "no test calls it" would be false). What is MEASURED below is that the
/// <c>ParsedExperience</c> overload suppresses the row. Saying the transform is not offered it
/// would over-correct.)</para>
///
/// <para>ONE consumer acts on a row this union releases: A1/A2/A6 via
/// <c>ReviewText.ExperienceBullets</c>. <c>StructureRules</c>' B5 also reads
/// <c>DescriptionLines</c> and its bullet-marker set does contain the en dash — but it does NOT act
/// on any form in play here, and <c>LeadMarker</c> has TWO guards that must be kept apart.
/// <b>Guard one</b> (<c>StructureRules.cs:362</c>) needs a marker glyph followed by whitespace — the
/// forms this union RELEASES all open with a digit or a letter, so B5 never reaches its second
/// guard on them. <b>Guard two</b> (<c>:372-376</c>, written for exactly "- 2020 – nuvarande") nulls
/// a marker whose remainder <c>PeriodParser</c> parses — that is what disarms the
/// leading-separator form, which this union SUPPRESSES anyway.</para>
///
/// <para>So B5 can act only on a row that clears BOTH: a marker glyph, and a remainder
/// <c>PeriodParser</c> refuses — "– jan 2020 – dec 2024". That was DERIVED from reading
/// <c>LeadMarker</c> and pinned by no test; the date-model widening (#1060 road 3) both MEASURED it
/// and closed it. Measured: beside a bullet using another glyph, B5 returned
/// <i>Warn — "Blandade punktsymboler"</i>, counting the user's date row as a second bullet style she
/// had not chosen. That row lives in <c>DateModelWideningReviewSideTests</c>, with a counterfactual
/// proving the criterion still Warns on two genuine glyphs.</para>
///
/// <para><b>GUARD TWO — the one that nulls a marker whose remainder <c>PeriodParser</c> parses — is
/// pinned in <c>DateModelWideningLeadMarkerTests</c>, and it was NOT pinned when this paragraph
/// first claimed it was.</b> An earlier revision said "both guards are now measured in both
/// polarities" and cited a control row "– 2020 – nuvarande". That row existed only in a throwaway
/// probe, which was deleted; the claim outlived its measurement, which is exactly the failure the
/// widening's own acceptance obligation warns about. Road 3 also gives guard two a NEW live input:
/// <c>PeriodParser</c> now parses a lone month point, so "– maj 2020" flips from marker-bearing to
/// nulled. Both are pinned now. An earlier round of this review asserted B5 flipped on the
/// leading-separator form, having read the marker set and not the guard nine lines below it.</para>
///
/// <para>The class is the INVERSE of the one first
/// cited: §5's "a CV verdict without cited textual evidence" — a verdict citing a span that is not
/// prose. Sharpest on <c>YYYY/MM</c>, which <c>DateRange</c> modelled on neither endpoint before
/// this PR, so <c>StripDates</c> left digits behind and A1 could read the employment dates as a
/// quantified result. <b>That A1/A2/A6 consequence WAS derived from reading the rules and is now
/// MEASURED</b> (#1060 road 3, (S1)): all three cited the user's date row, and on the
/// <c>YYYY/MM</c> form A1 returned an affirmative Pass noting "kvantifierad uppgift". <b>The
/// widening closed it for three of the four forms it added; the fourth, <c>YYYY/MM</c>, reopened in
/// round 5 (decision D′) and closed again in ADR 0136</b> — see
/// <see cref="DescriptionLines_SuppressesTheSlashDateRow_ThroughTheDatePatternsDisjunctAlone"/> below.
/// The verdicts live in <c>DateModelWideningReviewSideTests</c>; what is run and pinned HERE is the
/// bullet unit, which is where the cause is.</para>
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
    // "2020-06 – 2024-03" was a third row here until #1060 road 3 commit 1 ordered DateRange's
    // alternations longest-alternative-first. DatePatterns reaches that form now, so it no longer
    // ISOLATES the PeriodParser half — this method's whole job — and keeping it would have turned a
    // kill into a row that passes under either substitution. It moved to
    // DatePatternsAlternationOrderingTests. The two rows left are still individually load-bearing.
    public void DescriptionLines_ShouldStillSuppressTheDateRow_WhereOnlyPeriodParserReachesIt(
        string dateLine, string axis)
    {
        // THE TRAP, PINNED. These are the forms a SUBSTITUTION would have lost: PeriodParser is
        // wider here and DatePatterns declines both, so swapping the call site to a
        // DatePatterns-only predicate hands them to the bullet scorer and to WeakVerbTransform.
        //
        // THIS METHOD IS THE KILL FOR "union → DatePatterns only". The axes live as InlineData in
        // DatePatternsDateOnlyLineTests and no total is published anywhere — a count of a property
        // emergent from two independently written grammars decays the moment either changes, and
        // road 3 changed one of them: the ISO end-point axis that used to be this theory's third
        // row is retired, which is a row leaving the list without the list ever having a size.
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
    [InlineData("2020 –")]
    // "2020/01 – 2024/12" was IN this theory (#1060 road 3, commit 2), moved back OUT in round 5
    // (decision D′, which took the slash point out of DateRange to close a Blocker), and is back in
    // under ADR 0136 — this time through a row grammar that recognises the LINE without the VALUE
    // grammar moving, so the escape closes and D′'s Blocker stays closed. The five mixed and
    // all-slash siblings come with it: the same residue kept every one of them unsuppressed.
    [InlineData("2020/01 – 2024/12")]
    [InlineData("2008/09 – 2011/12")]
    [InlineData("2019/20 – 2021")]
    [InlineData("2018 – 2019/20")]
    [InlineData("2020 – 2024/12")]
    [InlineData("2020-06 – 2024/12")]
    public void DescriptionLines_ShouldNowSuppressTheDateRow_OnTheLayoutWhereItUsedToEscape(
        string dateLine)
    {
        // THE ESCAPE THIS TEST WAS WRITTEN TO MEASURE IS CLOSED (#1060 road 3, commit 2), and the
        // move is the one its previous revision asked for: the InlineData are frozen and
        // unchanged, and the ASSERTION moved from "still yields the date row as a bullet" to
        // "suppresses it". Editing the data to keep the old assertion green was the named failure
        // mode; this is its opposite.
        //
        // WHY THIS LAYOUT IS THE ONE THAT MATTERED. On the two-line "Title / Dates" layout these
        // forms were suppressed even before the widening — but only as a side effect of a defect:
        // the segmenter fabricated them into Organization and the organisation-equality test fired
        // on them. Here the employer is real ("Acme AB"), nothing fabricates the date row, and
        // neither half of the union reached it, so it was scored as prose. The organisation
        // assertion below is what keeps that distinction honest: it proves the suppression is the
        // period test doing its job, not equality masking the row.
        //
        // WHAT THE ESCAPE COST, MEASURED (not derived) on this exact layout before the widening:
        // A1/A2/A6 all scored and CITED the user's employment dates as though they were prose. That
        // is CLAUDE.md §5's "a CV verdict without cited textual evidence" in its inverted form. The
        // verdict-level pin lives in DateModelWideningReviewSideTests; this one stays at the
        // bullet-unit altitude where the cause is.
        var (review, improve, organization) = BulletsFor(dateLine);

        organization.ShouldBe("Acme AB",
            "the employer is real here, so organisation-equality cannot be what suppresses the row.");
        review.ShouldBe([Bullet],
            "the date row is not a description bullet — the review criteria must never score it.");
        improve.ShouldBe([Bullet],
            "WeakVerbTransform scores the same unit and must not be offered the date row either.");
    }

    [Fact]
    public void DescriptionLines_SuppressesTheSlashDateRow_ThroughTheDatePatternsDisjunctAlone()
    {
        // WHICH HALF OF THE UNION DOES IT, asserted rather than assumed — the property this class
        // exists for. PeriodParser still refuses "2020/01 – 2024/12" (ADR 0136 changed no reading),
        // so the suppression can only be the DatePatterns disjunct. A union that started passing
        // because BOTH halves reached the form would be a different change wearing this one's
        // result, and D′'s Blocker rides on the value half not moving.
        PeriodParser.TryParse("2020/01 – 2024/12", out _, out _, out _).ShouldBeFalse(
            "the VALUE half must still decline it — that is decision D′, unchanged by ADR 0136.");
        DatePatterns.IsDateOnlyLine("2020/01 – 2024/12").ShouldBeTrue(
            "so the LINE half is what suppresses it, alone.");
    }
}

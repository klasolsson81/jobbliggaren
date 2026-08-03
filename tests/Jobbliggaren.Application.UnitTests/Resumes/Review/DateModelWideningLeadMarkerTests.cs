using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Review;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// #1060 road 3 — <c>StructureRules.LeadMarker</c>'s SECOND guard, which nulls a bullet marker whose
/// remainder <c>PeriodParser</c> parses. B5 counts distinct leading glyphs and Warns at two, so a
/// marker that is not nulled becomes a "bullet style" the user never chose.
///
/// <para><b>This class exists because a docblock claimed the measurement before it existed.</b>
/// <c>ReviewTextPeriodLineUnionTests</c> stated that both <c>LeadMarker</c> guards were "measured in
/// both polarities" and cited a control row "– 2020 – nuvarande". That row lived only in a throwaway
/// probe, which was deleted — the claim outlived its evidence. Guard one (a marker glyph followed by
/// whitespace) is measured by <c>DateModelWideningReviewSideTests</c>' B5 pair; guard two was
/// measured by nothing. It is measured here.</para>
///
/// <para><b>And road 3 gave guard two a NEW live input.</b> Widening <c>PeriodParser</c> to the lone
/// month point flips "– maj 2020" from marker-bearing to nulled: before, <c>TryParse("maj 2020")</c>
/// was false and the marker survived; now it is true and the marker is dropped. That is a real
/// behaviour change this PR introduced in a criterion it never mentions, so it gets a pin at the
/// altitude the change happens.</para>
/// </summary>
public class DateModelWideningLeadMarkerTests
{
    private const string Bullet = "Ökade konverteringen med 23 procent.";

    private static async Task<CriterionVerdict> B5ForAsync(string markerRow)
    {
        // The real segmenter and the real engine — the date row sits BELOW a genuine period line so
        // the entry keeps a normal shape, and the prose bullet carries its own distinct glyph. If
        // LeadMarker does not null the marker on `markerRow`, B5 sees two glyphs and Warns.
        var cv = $"""
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Systemutvecklare
            Acme AB
            2013 - 2021
            {markerRow}
            • {Bullet}
            """;

        var engine = new CvReviewEngine(
            RealRubricProvider(), RealClicheLexicon(), RealVerbMapper(), Analyzer(),
            AllCorrectSpellChecker(), RealAllowlist(),
            RealCvConventionsProvider(), RealParsingLexicon());

        var result = await engine.ReviewAsync(
            CvReviewContext.FromParsed(ResumeFromCvText(cv)),
            RenderProfile.Ats,
            TestContext.Current.CancellationToken);

        return result.Verdicts.Single(v => v.CriterionId == "B5").Verdict;
    }

    [Fact]
    public async Task LeadMarker_IsNulled_WhenTheRemainderParsesAsAPeriod()
    {
        // ONE ROW, AND THE OTHER TWO WERE REMOVED RATHER THAN KEPT AS PADDING. An earlier revision
        // ran three: "– 2020 – nuvarande" and "- 01/2022 – 06/2024" alongside this one. Both are
        // suppressed by DescriptionLines BEFORE LeadMarker is ever offered them — DatePatterns
        // reduces each to empty, so IsDateOnlyLine is true and the union drops the row upstream. B5
        // then saw one glyph and returned NotAssessed for a reason that had nothing to do with guard
        // two. Delete guard two and both rows stayed green; the theory's own comments named a
        // mechanism two thirds of its data could not reach.
        //
        // "– maj 2020" is the row that crosses the threshold, and it is the one road 3 created:
        // IsDateOnlyLine is FALSE for it (StripTrailingDate leaves "– maj"), so the row survives the
        // union and reaches LeadMarker — where guard two nulls the marker because PeriodParser now
        // parses a lone month point. Before road 3 it did not, and the marker survived.
        const string markerRow = "– maj 2020";

        // Both premises, because either alone leaves the test measuring something else: the row must
        // SURVIVE the union to be offered the guard, and its remainder must PARSE for the guard to
        // fire.
        DatePatterns.IsDateOnlyLine(markerRow).ShouldBeFalse(
            "premise: the row must survive DescriptionLines, or LeadMarker is never offered it.");
        PeriodParser.TryParse(markerRow.TrimStart('–', ' '), out _, out _, out _).ShouldBeTrue(
            "premise: guard two only fires where the remainder parses — and it does only since road 3.");

        (await B5ForAsync(markerRow)).ShouldBe(CriterionVerdict.NotAssessed,
            "a marker leading a parseable period is a date row, not a bullet STYLE.");
    }

    [Fact]
    public async Task LeadMarker_SurvivesGuardTwo_WhenTheRemainderIsRealProse()
    {
        // THE COUNTERFACTUAL, without which the theory above proves only that B5 can return
        // NotAssessed. A marker leading genuine prose must still count as a second bullet style —
        // guard two must not swallow everything it is offered.
        const string proseRow = "– Införde en ny releaseprocess.";

        PeriodParser.TryParse(proseRow.TrimStart('–', ' '), out _, out _, out _)
            .ShouldBeFalse("premise: this remainder is prose, so guard two must NOT fire.");

        (await B5ForAsync(proseRow)).ShouldBe(CriterionVerdict.Warn,
            "two genuine bullet glyphs on two genuine bullets are still mixed punctuation.");
    }
}

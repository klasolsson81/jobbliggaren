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

    [Theory]
    // GUARD TWO FIRING — the remainder parses, so the marker is nulled and B5 sees one glyph.
    [InlineData("– 2020 – nuvarande", "the row guard two was originally written for")]
    [InlineData("– maj 2020", "a lone month point, which PeriodParser reaches only since road 3")]
    [InlineData("- 01/2022 – 06/2024", "an ASCII marker with a slash range")]
    public async Task LeadMarker_IsNulled_WhenTheRemainderParsesAsAPeriod(string markerRow, string why)
    {
        // Premise stated as an assertion rather than assumed: guard two keys off PeriodParser, so
        // the test would measure nothing if the remainder did not actually parse.
        PeriodParser.TryParse(markerRow.TrimStart('–', '-', ' '), out _, out _, out _)
            .ShouldBeTrue($"premise: guard two only fires where the remainder parses — {why}.");

        (await B5ForAsync(markerRow)).ShouldBe(CriterionVerdict.NotAssessed,
            $"a marker leading a parseable period is a date row, not a bullet STYLE — {why}.");
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

using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Infrastructure.Resumes.Review;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// Honest date absence (CV-pivot 2026-07-17, CTO-bind 5a-pre) — the review-arm precedence:
/// a date-less canonical entry carries its verbatim <c>RawPeriod</c> into
/// <c>ReviewableExperience.PeriodText</c> (the wiring in <see cref="CvReviewContext.FromCanonical"/>)
/// so the engine's <c>PeriodParser</c> recovers the period from the user's own string.
/// Both halves are pinned with counterfactuals: the WIRING (RawPeriod → PeriodText, null → null)
/// and the RECOVERY (B6 sees a recovered year-only period as a second date format — Warn;
/// without RawPeriod the entry is unparsed and B6 sees one format — Pass).
/// </summary>
public class CanonicalReviewPeriodRecoveryTests
{
    private static CvReviewEngine Engine() =>
        new(RealRubricProvider(), RealClicheLexicon(), RealVerbMapper(), Analyzer(),
            AllCorrectSpellChecker(), RealAllowlist(),
            RealCvConventionsProvider(), RealParsingLexicon());

    private static ResumeContent CanonicalContent(string? rawPeriod) =>
        new(new PersonalInfo("Anna Andersson", "anna@example.com", null, "Stockholm"),
            experiences:
            [
                new Experience(
                    "Mastercard", "Backend Developer",
                    new DateOnly(2023, 1, 1), new DateOnly(2024, 6, 1),
                    "Byggde betaltjänster och API:er."),
                new Experience(
                    "Gammalt AB", "Utvecklare", null, null,
                    "Underhöll interna system.", rawPeriod),
            ],
            skills: [new Skill("C#", 8)],
            summary: "Erfaren backend-utvecklare med fokus på betalflöden.");

    private static CvReviewContext Context(string? rawPeriod)
    {
        var content = CanonicalContent(rawPeriod);
        return CvReviewContext.FromCanonical(
            content, ResumeContentLinearizer.Linearize(content), ResumeLanguage.Sv);
    }

    // ---------------------------------------------------------------
    // The WIRING: RawPeriod → PeriodText on the canonical arm
    // ---------------------------------------------------------------

    [Fact]
    public void FromCanonical_FeedsRawPeriodIntoPeriodText_ForDatelessExperience()
    {
        var context = Context("2019–2022");

        context.Content.Experience[1].PeriodText.ShouldBe("2019–2022");
    }

    [Fact]
    public void FromCanonical_LeavesPeriodTextNull_WhenRawPeriodAbsent()
    {
        var context = Context(rawPeriod: null);

        context.Content.Experience[1].PeriodText.ShouldBeNull();
    }

    // ---------------------------------------------------------------
    // The RECOVERY: the engine date-parses the recovered period (B6 counterfactual)
    // ---------------------------------------------------------------

    [Fact]
    public async Task Review_DatelessEntryWithRawPeriod_IsRecovered_B6SeesMixedFormats()
    {
        var result = await Engine().ReviewAsync(
            Context("2019–2022"), RenderProfile.Ats, TestContext.Current.CancellationToken);

        var b6 = result.Verdicts.Single(v => v.CriterionId == "B6");
        // The dated entry carries the canonical month token; the RECOVERED year-only
        // period is a second, distinct format — mixed → Warn. If the RawPeriod→PeriodText
        // wiring dies, the second entry is unparsed and B6 collapses to Pass (one format).
        b6.Verdict.ShouldBe(CriterionVerdict.Warn);
    }

    [Fact]
    public async Task Review_DatelessEntryWithoutRawPeriod_StaysUnparsed_B6SeesOneFormat()
    {
        var result = await Engine().ReviewAsync(
            Context(rawPeriod: null), RenderProfile.Ats, TestContext.Current.CancellationToken);

        var b6 = result.Verdicts.Single(v => v.CriterionId == "B6");
        // The counterfactual: no RawPeriod → nothing to recover → only the dated entry's
        // format exists → consistent → Pass. Proves the Warn above comes from RECOVERY,
        // not from the entry's mere existence.
        b6.Verdict.ShouldBe(CriterionVerdict.Pass);
    }
}

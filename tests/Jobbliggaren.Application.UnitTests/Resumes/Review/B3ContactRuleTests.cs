using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Review;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// B3 "Kontaktuppgifter kompletta" on both arms (#1741, ADR 0142 D7). The canonical arm grades no
/// name: the account holds none and auto-promote takes none into the CV, so a name is neither
/// missed nor claimed there. The staging arm grades the file's own contact name as before.
/// </summary>
public class B3ContactRuleTests
{
    private static CvReviewEngine NewEngine() =>
        new(RealRubricProvider(), RealClicheLexicon(), RealVerbMapper(), Analyzer(),
            AllCorrectSpellChecker(), RealAllowlist(),
            RealCvConventionsProvider(), RealParsingLexicon());

    private static async Task<CvCriterionVerdict> B3Async(CvReviewContext context) =>
        Verdict(
            await NewEngine().ReviewAsync(context, RenderProfile.Ats, TestContext.Current.CancellationToken),
            "B3");

    // What auto-promote builds since #1741: the parse's contact fields and no name.
    private static CvReviewContext Canonical(PersonalInfo personalInfo)
    {
        var content = new ResumeContent(
            personalInfo,
            experiences:
            [
                new Experience("Acme AB", "Backend-utvecklare",
                    new DateOnly(2021, 1, 1), new DateOnly(2024, 1, 1), "Levererade 3 plattformsmigrationer."),
            ],
            educations:
            [
                new Education("KTH", "Civilingenjör", new DateOnly(2016, 8, 1), new DateOnly(2021, 6, 1)),
            ]);
        return CvReviewContext.FromCanonical(
            content, ResumeContentLinearizer.Linearize(content), ResumeLanguage.Sv);
    }

    private static string Observation(CvCriterionVerdict verdict) =>
        verdict.Evidence.ShouldHaveSingleItem().ShouldBeOfType<StructuralEvidence>().Observation;

    [Fact]
    public async Task B3_ShouldPassWithoutClaimingAName_OnACanonicalCvWithNoName()
    {
        var b3 = await B3Async(Canonical(
            new PersonalInfo(null, "anna@example.se", "070-123 45 67", "Stockholm")));

        b3.Verdict.ShouldBe(CriterionVerdict.Pass);
        Observation(b3).ShouldBe("E-post, telefon och ort finns i klartext.");
    }

    [Fact]
    public async Task B3_ShouldMissOnlyTheCity_OnACanonicalCvWithNoNameAndNoCity()
    {
        var b3 = await B3Async(Canonical(
            new PersonalInfo(null, "anna@example.se", "070-123 45 67", null)));

        b3.Verdict.ShouldBe(CriterionVerdict.Warn);
        Observation(b3).ShouldBe("Kontaktsektion hittad; saknar ort.");
    }

    [Fact]
    public async Task B3_ShouldNotGradeTheName_OnACanonicalCvThatCarriesOne()
    {
        // A CV promoted before #1741 carries the account's name (the mapper wrote it until then).
        // The canonical arm grades no name either way, so the name is not claimed here either.
        var b3 = await B3Async(Canonical(
            new PersonalInfo("Anna Andersson", "anna@example.se", "070-123 45 67", "Stockholm")));

        b3.Verdict.ShouldBe(CriterionVerdict.Pass);
        Observation(b3).ShouldBe("E-post, telefon och ort finns i klartext.");
    }

    [Fact]
    public async Task B3_ShouldStillMissTheName_OnAStagedFileWithoutOne()
    {
        // The file's own contact block lacks a name: the segmenter reports that contact as
        // Degraded (HeadingDrivenResumeSegmenter.ContactConfidence).
        var parsed = Resume(
            contact: new ParsedContact(null, "anna@example.se", "070-123 45 67", "Stockholm"),
            confidence: ParseConfidence.FromSections(
            [
                new SectionConfidence(
                    ParsedSectionKind.Contact, SectionConfidenceLevel.Degraded, ["email extracted", "phone extracted"]),
                new SectionConfidence(ParsedSectionKind.Experience, SectionConfidenceLevel.Confident, ["1 post"]),
            ]));

        var b3 = await B3Async(CvReviewContext.FromParsed(parsed));

        b3.Verdict.ShouldBe(CriterionVerdict.Warn);
        Observation(b3).ShouldBe("Kontaktsektion hittad; saknar namn.");
    }

    [Fact]
    public async Task B3_ShouldPassAndNameTheName_OnACompleteStagedFile()
    {
        var b3 = await B3Async(CvReviewContext.FromParsed(Resume()));

        b3.Verdict.ShouldBe(CriterionVerdict.Pass);
        Observation(b3).ShouldBe("Namn, e-post, telefon och ort finns i klartext.");
    }
}

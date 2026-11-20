using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobAds.Events;
using Jobbliggaren.TestSupport;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.JobAds;

/// <summary>
/// #2264 (ADR 0187) — the junior inputs on <see cref="JobAd"/>: every source write writes the experience mark (with
/// PRESERVE) and the input fingerprint over the post-scrub text, and Erase() clears the fingerprint. Ads come from
/// <see cref="JobAd.Import"/> and <see cref="JobAd.UpdateFromSource"/>.
/// </summary>
public class JobAdJuniorInputTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private const string Payload = "{\"id\":\"26500001\"}";
    private const string Group = "group-1";

    internal static JobAdFacets Facets(string? group = Group, bool? experienceRequired = false) =>
        new(occupationNameConceptId: null,
            occupationGroupConceptId: group,
            municipalityConceptId: null,
            regionConceptId: null,
            employmentTypeConceptId: null,
            worktimeExtentConceptId: null,
            organizationNumber: null,
            experienceRequired: experienceRequired);

    internal static JobAd Import(string title = "Junior utvecklare", JobAdFacets? facets = null) =>
        JobAd.Import(
            title,
            Company.Create("Exempel AB").Value,
            "Vi söker en utvecklare.",
            "https://arbetsformedlingen.se/platsbanken/annonser/26500001",
            ExternalReference.Create(JobSource.Platsbanken, "26500001").Value,
            Payload,
            facets ?? Facets(),
            [],
            Clock.UtcNow,
            null,
            Clock,
            extractTerms: TestKeywordExtraction.None,
            applicationUrl: null).Value;

    private static Result Update(JobAd jobAd, string title, JobAdFacets? facets = null) =>
        jobAd.UpdateFromSource(
            title,
            jobAd.Description,
            jobAd.Url,
            Payload,
            facets ?? Facets(),
            [],
            jobAd.PublishedAt,
            null,
            extractTerms: TestKeywordExtraction.None,
            applicationUrl: null);

    [Fact]
    public void Import_WritesTheMarkAndTheFingerprintOfThePostScrubInputs()
    {
        var jobAd = Import();

        jobAd.ExperienceRequired.ShouldBe(false);
        jobAd.JuniorInputFingerprint.ShouldBe(JuniorInputFingerprint.Of(jobAd.Title, jobAd.Description, Group, false));
    }

    [Fact]
    public void ARewriteOfTheText_ChangesTheFingerprint_AndAnUnchangedReimportDoesNot()
    {
        var jobAd = Import();
        var before = jobAd.JuniorInputFingerprint;

        Update(jobAd, jobAd.Title).IsSuccess.ShouldBeTrue();
        jobAd.JuniorInputFingerprint.ShouldBe(before);

        Update(jobAd, "Senior utvecklare").IsSuccess.ShouldBeTrue();
        jobAd.JuniorInputFingerprint.ShouldNotBe(before);
    }

    [Theory]
    [InlineData("group-2", false)]
    [InlineData(Group, true)]
    public void AChangedGroupOrMark_ChangesTheFingerprint(string group, bool experienceRequired)
    {
        var jobAd = Import();
        var before = jobAd.JuniorInputFingerprint;

        Update(jobAd, jobAd.Title, Facets(group, experienceRequired)).IsSuccess.ShouldBeTrue();

        jobAd.JuniorInputFingerprint.ShouldNotBe(before);
    }

    [Fact]
    public void APayloadWithoutTheMark_KeepsTheLastOne()
    {
        var jobAd = Import(facets: Facets(experienceRequired: false));

        Update(jobAd, jobAd.Title, Facets(experienceRequired: null)).IsSuccess.ShouldBeTrue();

        jobAd.ExperienceRequired.ShouldBe(false);
    }

    [Fact]
    public void Erase_ClearsTheFingerprint_AndKeepsTheMarkLikeTheOtherFacets()
    {
        var jobAd = Import();

        jobAd.Erase(Clock).IsSuccess.ShouldBeTrue();

        jobAd.JuniorInputFingerprint.ShouldBeNull();
        jobAd.ExperienceRequired.ShouldBe(false);
    }

    [Fact]
    public void TheFingerprint_SeparatesItsFields()
    {
        JuniorInputFingerprint.Of("ab", "c", null, null).ShouldNotBe(JuniorInputFingerprint.Of("a", "bc", null, null));
        JuniorInputFingerprint.Of("a", "b", null, null).Length.ShouldBe(JuniorInputFingerprint.Length);
    }
}

/// <summary>
/// #2264 (ADR 0187) — the assessment: the sweep classifies and re-classifies, stamping the fingerprint it read; an admin
/// decides; neither writer touches the other's members.
/// </summary>
public class JobAdJuniorAssessmentTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static readonly JobAdId AdId = JobAdId.New();
    private static readonly string Fingerprint = JuniorInputFingerprint.Of("Junior utvecklare", "Text.", "group-1", false);
    private static readonly JuniorClassification JuniorByTitle = new(JuniorVerdict.Junior, "title-junior", "1.0.0+aaaaaaaa");
    private static readonly JuniorClassification UnclearByMentor = new(JuniorVerdict.Unclear, "text-mentor-only", "1.0.0+bbbbbbbb");

    [Fact]
    public void Classify_StoresTheVerdict_AndTheFingerprintItWasGiven()
    {
        var assessment = JobAdJuniorAssessment.Classify(AdId, JuniorByTitle, Fingerprint, Clock);

        assessment.JobAdId.ShouldBe(AdId);
        assessment.Verdict.ShouldBe(JuniorVerdict.Junior);
        assessment.RuleId.ShouldBe("title-junior");
        assessment.RuleSetVersion.ShouldBe("1.0.0+aaaaaaaa");
        assessment.ClassifiedFingerprint.ShouldBe(Fingerprint);
        assessment.ClassifiedAt.ShouldBe(Clock.UtcNow);
        assessment.Decision.ShouldBeNull();
    }

    [Fact]
    public void Classify_TakesANullFingerprint_ForAnAdWrittenBeforeTheFingerprintExisted()
    {
        JobAdJuniorAssessment.Classify(AdId, JuniorByTitle, null, Clock).ClassifiedFingerprint.ShouldBeNull();
    }

    [Fact]
    public void Classify_RefusesAFingerprintThatIsNotASha256()
    {
        Should.Throw<ArgumentException>(() => JobAdJuniorAssessment.Classify(AdId, JuniorByTitle, "abc", Clock));
    }

    [Fact]
    public void ADecision_SurvivesAReclassification()
    {
        var assessment = JobAdJuniorAssessment.Classify(AdId, JuniorByTitle, Fingerprint, Clock);
        assessment.Decide(JuniorDecision.NotJunior, Clock).IsSuccess.ShouldBeTrue();

        assessment.Reclassify(UnclearByMentor, Fingerprint, Clock);

        assessment.Verdict.ShouldBe(JuniorVerdict.Unclear);
        assessment.RuleId.ShouldBe("text-mentor-only");
        assessment.Decision.ShouldBe(JuniorDecision.NotJunior);
        assessment.DecidedAt.ShouldBe(Clock.UtcNow);
    }

    [Fact]
    public void Decide_RaisesTheEvent_AndNullHandsTheAdBackToTheRules()
    {
        var assessment = JobAdJuniorAssessment.Classify(AdId, UnclearByMentor, Fingerprint, Clock);
        assessment.Decide(JuniorDecision.Junior, Clock);

        assessment.Decide(null, Clock).IsSuccess.ShouldBeTrue();

        assessment.Decision.ShouldBeNull();
        assessment.DecidedAt.ShouldBeNull();
        assessment.Verdict.ShouldBe(JuniorVerdict.Unclear, "a decision never touches the verdict");
        assessment.DomainEvents.OfType<JobAdJuniorDecidedDomainEvent>().Select(e => e.Decision)
            .ShouldBe([JuniorDecision.Junior, null]);
    }

    [Fact]
    public void Decide_RefusesAnUnknownDecision()
    {
        var assessment = JobAdJuniorAssessment.Classify(AdId, UnclearByMentor, Fingerprint, Clock);

        Should.Throw<ArgumentOutOfRangeException>(() => assessment.Decide((JuniorDecision)0, Clock));
    }
}

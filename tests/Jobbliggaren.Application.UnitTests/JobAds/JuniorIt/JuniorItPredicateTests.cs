using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.JobAds.JuniorIt;
using Jobbliggaren.TestSupport;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.JuniorIt;

/// <summary>
/// The junior IT predicate's shape (ADR 0186), compiled: the field holds on both arms, and either arm admits an ad in
/// the field. Ads come from <c>JobAd.Import</c> and <c>JobAd.Create</c>. Its translation to SQL, and the regex as
/// Postgres reads it, are measured in <c>JuniorItJobAdQueryReadTests</c> and <c>JuniorItHeadlinePatternAgreementTests</c>.
/// </summary>
public class JuniorItPredicateTests
{
    private const string DataItGroup = "DJh5_yyF_hEM";
    private const string OtherDataItGroup = "hmaC_cfi_UKg";
    private const string NotDataItGroup = "2kLc_pto_DpV";
    private const string Curated = "90000001";

    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;

    private static readonly Func<JobAd, bool> Matches = JuniorItPredicate.Matches(
        new JuniorItSet([Curated], [DataItGroup, OtherDataItGroup], "(^|[^a-zA-Z0-9])junior|trainee")).Compile();

    private static JobAd Imported(string title, string? group, string externalId = "80000001") =>
        JobAd.Import(
            title: title,
            company: Company.Create("Exempelbolaget AB").Value,
            description: "Vi söker en utvecklare.",
            url: $"https://arbetsformedlingen.se/platsbanken/annonser/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: $"{{\"id\":\"{externalId}\"}}",
            facets: TestFacets.From(occupationGroup: group),
            declaredContacts: [],
            publishedAt: Clock.UtcNow.AddDays(-1),
            expiresAt: null,
            clock: Clock,
            applicationUrl: null, extractTerms: TestKeywordExtraction.None).Value;

    [Fact]
    public void Matches_ACuratedAdInTheField_EvenWhenItsTitleMissesThePattern() =>
        Matches(Imported("Systemutvecklare", DataItGroup, Curated)).ShouldBeTrue();

    [Fact]
    public void Matches_AnAdInTheField_WhoseTitleMatchesThePattern_UncuratedAndInAnyCase() =>
        Matches(Imported("JUNIOR utvecklare", OtherDataItGroup)).ShouldBeTrue();

    [Fact]
    public void Matches_NoAdInTheField_ThatNeitherArmAdmits() =>
        Matches(Imported("Senior utvecklare", DataItGroup)).ShouldBeFalse();

    [Fact]
    public void Matches_NoMatchingTitleOutsideTheField() =>
        Matches(Imported("Junior ekonomiassistent", NotDataItGroup)).ShouldBeFalse();

    [Fact]
    public void Matches_NoCuratedIdOutsideTheField() =>
        Matches(Imported("Systemutvecklare", NotDataItGroup, Curated)).ShouldBeFalse();

    [Fact]
    public void Matches_NoAdWithoutAGroup_EvenCuratedAndMatching() =>
        Matches(Imported("Junior utvecklare", null, Curated)).ShouldBeFalse();

    [Fact]
    public void Matches_NoAdThatWasNeverImported()
    {
        // What POST /api/v1/job-ads stores: no import provenance and no facets, so no group.
        var manual = JobAd.Create(
            "Junior utvecklare", Company.Create("Exempelbolaget AB").Value, "Text som någon skrev själv.",
            "https://example.com/jobb/1", JobSource.Platsbanken, Clock.UtcNow.AddDays(-1), null, Clock).Value;

        Matches(manual).ShouldBeFalse();
    }
}

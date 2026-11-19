using FluentValidation.TestHelper;
using Jobbliggaren.Application.JobAds.Queries.GetPublicJuniorItJobAds;
using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.GetPublicJuniorItJobAds;

/// <summary>
/// The three inputs the anonymous list accepts (ADR 0186): a page inside the cap, one of the two orders, and either no
/// kommun or one shaped like a JobTech concept id. Everything else is the pipeline's 400.
/// </summary>
public class GetPublicJuniorItJobAdsQueryValidatorTests
{
    private readonly GetPublicJuniorItJobAdsQueryValidator _validator = new();

    private static GetPublicJuniorItJobAdsQuery Query(
        string? municipality = null, JobAdSortBy sortBy = JobAdSortBy.PublishedAtDesc, int page = 1) =>
        new(municipality, sortBy, page);

    [Theory]
    [InlineData(null, JobAdSortBy.PublishedAtDesc, 1)]
    [InlineData(null, JobAdSortBy.ExpiresAtAsc, PublicJuniorItJobAdLimits.MaxPage)]
    [InlineData("AvNB_uwa_6n6", JobAdSortBy.PublishedAtDesc, 1)]
    [InlineData("PVZL_BQT_XtL", JobAdSortBy.ExpiresAtAsc, 2)]
    public void Validate_AQueryTheListAnswers_IsValid(string? municipality, JobAdSortBy sortBy, int page) =>
        _validator.TestValidate(Query(municipality, sortBy, page)).ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(PublicJuniorItJobAdLimits.MaxPage + 1)]
    public void Validate_APageOutsideTheCap_IsInvalid(int page) =>
        _validator.TestValidate(Query(page: page)).ShouldHaveValidationErrorFor(q => q.Page);

    [Theory]
    [InlineData(JobAdSortBy.PublishedAtAsc)]
    [InlineData(JobAdSortBy.ExpiresAtDesc)]
    [InlineData(JobAdSortBy.Relevance)]
    [InlineData((JobAdSortBy)99)]
    public void Validate_AnOrderTheListDoesNotOffer_IsInvalid(JobAdSortBy sortBy) =>
        _validator.TestValidate(Query(sortBy: sortBy)).ShouldHaveValidationErrorFor(q => q.SortBy);

    [Theory]
    [InlineData("")]
    [InlineData("AvNB-uwa-6n6")]
    [InlineData("AvNB uwa 6n6")]
    [InlineData("Göteborg")]
    [InlineData("x' OR '1'='1")]
    [InlineData("%")]
    [InlineData("../AvNB_uwa_6n6")]
    public void Validate_AKommunThatIsNoConceptId_IsInvalid(string municipality) =>
        _validator.TestValidate(Query(municipality)).ShouldHaveValidationErrorFor(q => q.Municipality);

    [Fact]
    public void Validate_AKommunIdAtTheLengthCap_IsValid() =>
        _validator.TestValidate(Query(new string('a', PublicJuniorItJobAdLimits.MaxMunicipalityLength)))
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void Validate_AKommunIdPastTheLengthCap_IsInvalid() =>
        _validator.TestValidate(Query(new string('a', PublicJuniorItJobAdLimits.MaxMunicipalityLength + 1)))
            .ShouldHaveValidationErrorFor(q => q.Municipality);
}

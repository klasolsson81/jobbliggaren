using FluentValidation.TestHelper;
using Jobbliggaren.Application.JobAds.Listing;
using Jobbliggaren.Application.JobAds.Queries.GetListedJobAdIds;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.GetListedJobAdIds;

/// <summary>
/// The part and the part count come from the web's route files (ADR 0174); anything else is a 400, never a part
/// the snapshot does not hold.
/// </summary>
public class GetListedJobAdIdsQueryValidatorTests
{
    private readonly GetListedJobAdIdsQueryValidator _validator = new();

    [Theory]
    [InlineData(1)]
    [InlineData(ListedJobAdSnapshotLimits.PartCount)]
    public void Validate_EveryPartOfTheSitemap_IsValid(int part) =>
        _validator.TestValidate(new GetListedJobAdIdsQuery(part, ListedJobAdSnapshotLimits.PartCount))
            .ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ListedJobAdSnapshotLimits.PartCount + 1)]
    public void Validate_APartOutsideTheSitemap_IsInvalid(int part) =>
        _validator.TestValidate(new GetListedJobAdIdsQuery(part, ListedJobAdSnapshotLimits.PartCount))
            .ShouldHaveValidationErrorFor(q => q.Part);

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(16)]
    public void Validate_APartCountOtherThanTheSitemaps_IsInvalid(int parts) =>
        _validator.TestValidate(new GetListedJobAdIdsQuery(1, parts))
            .ShouldHaveValidationErrorFor(q => q.Parts);
}

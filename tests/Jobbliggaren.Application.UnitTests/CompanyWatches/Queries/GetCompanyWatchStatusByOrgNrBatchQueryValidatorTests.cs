using System.Globalization;
using Jobbliggaren.Application.CompanyWatches.Queries.GetCompanyWatchStatusByOrgNrBatch;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Queries;

public class GetCompanyWatchStatusByOrgNrBatchQueryValidatorTests
{
    private readonly GetCompanyWatchStatusByOrgNrBatchQueryValidator _validator = new();

    [Fact]
    public void Validate_WithinCap_Passes()
    {
        var orgNrs = Enumerable.Range(0, GetCompanyWatchStatusByOrgNrBatchQueryValidator.MaxOrgNrsPerCall)
            .Select(i => i.ToString(CultureInfo.InvariantCulture).PadLeft(10, '0'))
            .ToList();

        _validator.Validate(new GetCompanyWatchStatusByOrgNrBatchQuery(orgNrs)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_OverCap_Fails()
    {
        var orgNrs = Enumerable.Range(0, GetCompanyWatchStatusByOrgNrBatchQueryValidator.MaxOrgNrsPerCall + 1)
            .Select(i => i.ToString(CultureInfo.InvariantCulture).PadLeft(10, '0'))
            .ToList();

        _validator.Validate(new GetCompanyWatchStatusByOrgNrBatchQuery(orgNrs)).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_Empty_Passes()
    {
        _validator.Validate(new GetCompanyWatchStatusByOrgNrBatchQuery([])).IsValid.ShouldBeTrue();
    }
}

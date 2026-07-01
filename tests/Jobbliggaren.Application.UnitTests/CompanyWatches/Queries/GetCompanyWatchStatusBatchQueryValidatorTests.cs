using Jobbliggaren.Application.CompanyWatches.Queries.GetCompanyWatchStatusBatch;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Queries;

public class GetCompanyWatchStatusBatchQueryValidatorTests
{
    private readonly GetCompanyWatchStatusBatchQueryValidator _validator = new();

    [Fact]
    public void Validate_WithinCap_Passes()
    {
        var ids = Enumerable.Range(0, GetCompanyWatchStatusBatchQueryValidator.MaxJobAdIdsPerCall)
            .Select(_ => Guid.NewGuid())
            .ToList();

        _validator.Validate(new GetCompanyWatchStatusBatchQuery(ids)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_OverCap_Fails()
    {
        var ids = Enumerable.Range(0, GetCompanyWatchStatusBatchQueryValidator.MaxJobAdIdsPerCall + 1)
            .Select(_ => Guid.NewGuid())
            .ToList();

        _validator.Validate(new GetCompanyWatchStatusBatchQuery(ids)).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_Empty_Passes()
    {
        _validator.Validate(new GetCompanyWatchStatusBatchQuery([])).IsValid.ShouldBeTrue();
    }
}

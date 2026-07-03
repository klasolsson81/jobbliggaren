using Jobbliggaren.Application.Applications.Queries.GetEmployerApplicationCountBatch;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Applications;

/// <summary>
/// #446 — the batch-size guard enforced BEFORE the handler (CLAUDE.md §7 validation-test).
/// MaxJobAdIdsPerCall = 100 keeps the /jobb overlay bounded (parity
/// <c>GetJobAdStatusBatchQueryValidatorTests</c>).
/// </summary>
public class GetEmployerApplicationCountBatchQueryValidatorTests
{
    private readonly GetEmployerApplicationCountBatchQueryValidator _validator = new();

    [Fact]
    public void Validate_HappyPath_Passes()
    {
        var query = new GetEmployerApplicationCountBatchQuery([Guid.NewGuid(), Guid.NewGuid()]);

        _validator.Validate(query).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_EmptyList_Passes()
    {
        var query = new GetEmployerApplicationCountBatchQuery([]);

        _validator.Validate(query).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_ExactlyMaxJobAdIdsPerCall_Passes()
    {
        var ids = Enumerable
            .Range(0, GetEmployerApplicationCountBatchQueryValidator.MaxJobAdIdsPerCall)
            .Select(_ => Guid.NewGuid())
            .ToList();

        _validator.Validate(new GetEmployerApplicationCountBatchQuery(ids)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_OverMaxJobAdIdsPerCall_Fails()
    {
        var ids = Enumerable
            .Range(0, GetEmployerApplicationCountBatchQueryValidator.MaxJobAdIdsPerCall + 1)
            .Select(_ => Guid.NewGuid())
            .ToList();

        var result = _validator.Validate(new GetEmployerApplicationCountBatchQuery(ids));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.Contains("Max 100"));
    }

    [Fact]
    public void Validate_NullList_Fails()
    {
        _validator.Validate(new GetEmployerApplicationCountBatchQuery(null!)).IsValid.ShouldBeFalse();
    }
}

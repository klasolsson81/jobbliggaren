using JobbPilot.Application.UserStatus.Queries.GetJobAdStatusBatch;
using Shouldly;

namespace JobbPilot.Application.UnitTests.UserStatus;

/// <summary>
/// ADR 0063 — validator enforce:as FÖRE handler (CLAUDE.md §7 validation-test).
/// MaxJobAdIdsPerCall = 100 är säkerhets-/perf-vakt.
/// </summary>
public class GetJobAdStatusBatchQueryValidatorTests
{
    private readonly GetJobAdStatusBatchQueryValidator _validator = new();

    [Fact]
    public void Validate_HappyPath_Passes()
    {
        var query = new GetJobAdStatusBatchQuery([Guid.NewGuid(), Guid.NewGuid()]);

        var result = _validator.Validate(query);

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_EmptyList_Passes()
    {
        var query = new GetJobAdStatusBatchQuery([]);

        var result = _validator.Validate(query);

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_ExactlyMaxJobAdIdsPerCall_Passes()
    {
        var ids = Enumerable.Range(0, GetJobAdStatusBatchQueryValidator.MaxJobAdIdsPerCall)
            .Select(_ => Guid.NewGuid())
            .ToList();
        var query = new GetJobAdStatusBatchQuery(ids);

        var result = _validator.Validate(query);

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_OverMaxJobAdIdsPerCall_Fails()
    {
        var ids = Enumerable.Range(0, GetJobAdStatusBatchQueryValidator.MaxJobAdIdsPerCall + 1)
            .Select(_ => Guid.NewGuid())
            .ToList();
        var query = new GetJobAdStatusBatchQuery(ids);

        var result = _validator.Validate(query);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.Contains("Max 100"));
    }

    [Fact]
    public void Validate_NullList_Fails()
    {
        var query = new GetJobAdStatusBatchQuery(null!);

        var result = _validator.Validate(query);

        result.IsValid.ShouldBeFalse();
    }
}

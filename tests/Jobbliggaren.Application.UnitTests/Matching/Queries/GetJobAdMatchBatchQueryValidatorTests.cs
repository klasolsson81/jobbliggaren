using Jobbliggaren.Application.Matching.Queries.GetJobAdMatchBatch;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Matching.Queries;

/// <summary>
/// F4-13 (ADR 0076 / ADR 0063) — the batch-size guard runs BEFORE the handler
/// (CLAUDE.md §7 validation-test). MaxJobAdIdsPerCall = 100 keeps the single
/// <c>= ANY</c> query a DoS-floor; empty is allowed (→ empty result); null/over-cap
/// → 400. Parity <see cref="Jobbliggaren.Application.UserStatus.Queries.GetJobAdStatusBatch"/>.
/// </summary>
public class GetJobAdMatchBatchQueryValidatorTests
{
    private readonly GetJobAdMatchBatchQueryValidator _validator = new();

    [Fact]
    public void Validate_HappyPath_Passes()
    {
        var query = new GetJobAdMatchBatchQuery([Guid.NewGuid(), Guid.NewGuid()]);

        var result = _validator.Validate(query);

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_EmptyList_Passes()
    {
        var query = new GetJobAdMatchBatchQuery([]);

        var result = _validator.Validate(query);

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_ExactlyMaxJobAdIdsPerCall_Passes()
    {
        var ids = Enumerable.Range(0, GetJobAdMatchBatchQueryValidator.MaxJobAdIdsPerCall)
            .Select(_ => Guid.NewGuid())
            .ToList();
        var query = new GetJobAdMatchBatchQuery(ids);

        var result = _validator.Validate(query);

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_OverMaxJobAdIdsPerCall_Fails()
    {
        var ids = Enumerable.Range(0, GetJobAdMatchBatchQueryValidator.MaxJobAdIdsPerCall + 1)
            .Select(_ => Guid.NewGuid())
            .ToList();
        var query = new GetJobAdMatchBatchQuery(ids);

        var result = _validator.Validate(query);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.ErrorMessage.Contains("Max 100"));
    }

    [Fact]
    public void Validate_NullList_Fails()
    {
        var query = new GetJobAdMatchBatchQuery(null!);

        var result = _validator.Validate(query);

        result.IsValid.ShouldBeFalse();
    }
}

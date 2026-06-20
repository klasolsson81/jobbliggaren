using Jobbliggaren.Application.Matching.Queries.GetJobAdMatchDetail;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Matching.Queries;

/// <summary>
/// F4-16 (ADR 0076 Amendment (b); CTO 2026-06-20 D5) — the single-ad modal detail query
/// guard runs BEFORE the handler (CLAUDE.md §7 validation-test). <c>JobAdId</c> must be a
/// real Guid (<c>Guid.Empty</c> → 400 via FluentValidation, parity
/// <see cref="GetJobAdMatchBatchQueryValidatorTests"/>'s <c>NotEmpty</c> discipline). RED
/// until <c>GetJobAdMatchDetailQuery</c> + its validator exist.
/// </summary>
public class GetJobAdMatchDetailQueryValidatorTests
{
    private readonly GetJobAdMatchDetailQueryValidator _validator = new();

    [Fact]
    public void Validate_HappyPath_Passes()
    {
        var query = new GetJobAdMatchDetailQuery(Guid.NewGuid());

        var result = _validator.Validate(query);

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_EmptyGuid_Fails()
    {
        var query = new GetJobAdMatchDetailQuery(Guid.Empty);

        var result = _validator.Validate(query);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(GetJobAdMatchDetailQuery.JobAdId));
    }
}

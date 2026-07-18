using Jobbliggaren.Application.JobAds.Queries.GetRemoteAdCount;
using Jobbliggaren.Domain.SavedSearches;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.GetRemoteAdCount;

/// <summary>
/// #551 PR-B D7 — the "Distans (N)" count validator mirrors <c>GetFacetCountsQueryValidator</c>
/// for the non-location surface (Domain <see cref="SearchCriteria"/> = the constants' source of
/// truth). No location axis: the query carries no Municipality/Region (D7 structural exclusion).
/// The per-element concept-id regex is the structural guard preventing free text/personnummer
/// from reaching the counter.
/// </summary>
public class GetRemoteAdCountQueryValidatorTests
{
    private readonly GetRemoteAdCountQueryValidator _validator = new();

    [Fact]
    public void Empty_query_passes()
    {
        _validator.Validate(new GetRemoteAdCountQuery()).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Valid_concept_ids_on_all_non_location_dimensions_pass()
    {
        var result = _validator.Validate(new GetRemoteAdCountQuery(
            OccupationGroup: ["grp_dev"],
            EmploymentType: ["et_fast"],
            WorktimeExtent: ["wt_full"],
            Q: "utvecklare"));
        result.IsValid.ShouldBeTrue();
    }

    // Each non-location dimension has its own RuleFor(...).Must(count <= Max) — each needs a red case.
    [Theory]
    [InlineData("occ")]
    [InlineData("emp")]
    [InlineData("wt")]
    public void List_exceeding_MaxConceptIds_fails_on_each_dimension(string dimension)
    {
        var tooMany = Enumerable.Range(0, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"id{i}").ToList();
        var query = dimension switch
        {
            "occ" => new GetRemoteAdCountQuery(OccupationGroup: tooMany),
            "emp" => new GetRemoteAdCountQuery(EmploymentType: tooMany),
            "wt" => new GetRemoteAdCountQuery(WorktimeExtent: tooMany),
            _ => throw new ArgumentOutOfRangeException(nameof(dimension)),
        };
        _validator.Validate(query).IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("bad id!")]
    [InlineData("personnummer 900101-1234")]
    [InlineData("this-token-is-far-too-long-to-be-a-concept-id-xxxx")]
    public void Invalid_concept_id_format_fails(string bad)
    {
        _validator.Validate(new GetRemoteAdCountQuery(OccupationGroup: [bad])).IsValid.ShouldBeFalse();
        _validator.Validate(new GetRemoteAdCountQuery(EmploymentType: [bad])).IsValid.ShouldBeFalse();
        _validator.Validate(new GetRemoteAdCountQuery(WorktimeExtent: [bad])).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Single_char_q_fails_min_length()
    {
        _validator.Validate(new GetRemoteAdCountQuery(Q: "a")).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Over_max_length_q_fails()
    {
        var tooLong = new string('a', SearchCriteria.QMaxLength + 1);
        _validator.Validate(new GetRemoteAdCountQuery(Q: tooLong)).IsValid.ShouldBeFalse();
    }
}

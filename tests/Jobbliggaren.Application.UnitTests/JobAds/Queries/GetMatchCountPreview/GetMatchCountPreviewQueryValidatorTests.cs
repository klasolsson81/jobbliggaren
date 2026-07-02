using Jobbliggaren.Application.JobAds.Queries.GetMatchCountPreview;
using Jobbliggaren.Domain.SavedSearches;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.GetMatchCountPreview;

/// <summary>
/// Epik #526 (ADR 0088) — the preview-count validator mirrors <c>GetFacetCountsQueryValidator</c>
/// (defense-in-depth; Domain <see cref="SearchCriteria"/> = the constants' source of truth). The
/// per-element concept-id regex is the structural guard that prevents any personnummer/free text
/// from reaching the counter — a concept-id token can never match a personnummer.
/// </summary>
public class GetMatchCountPreviewQueryValidatorTests
{
    private readonly GetMatchCountPreviewQueryValidator _validator = new();

    private static GetMatchCountPreviewQuery Query(
        IReadOnlyList<string>? occ = null,
        IReadOnlyList<string>? reg = null,
        IReadOnlyList<string>? mun = null,
        IReadOnlyList<string>? emp = null) =>
        new(occ ?? [], reg ?? [], mun ?? [], emp ?? []);

    [Fact]
    public void Empty_draft_passes()
    {
        _validator.Validate(Query()).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Valid_concept_ids_on_all_dimensions_pass()
    {
        var result = _validator.Validate(
            Query(occ: ["grp_dev"], reg: ["region_AB"], mun: ["kommun_0180"], emp: ["et_fast"]));
        result.IsValid.ShouldBeTrue();
    }

    // Over-cap must fail on ALL FOUR independently-authored cap rules (copy-paste-swap guard):
    // each dimension has its own RuleFor(...).Must(count <= Max), so each needs a red case.
    [Theory]
    [InlineData("occ")]
    [InlineData("reg")]
    [InlineData("mun")]
    [InlineData("emp")]
    public void List_exceeding_MaxConceptIds_fails_on_each_dimension(string dimension)
    {
        var tooMany = Enumerable.Range(0, SearchCriteria.MaxConceptIds + 1)
            .Select(i => $"id{i}").ToList();
        var query = dimension switch
        {
            "occ" => Query(occ: tooMany),
            "reg" => Query(reg: tooMany),
            "mun" => Query(mun: tooMany),
            "emp" => Query(emp: tooMany),
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
        _validator.Validate(Query(occ: [bad])).IsValid.ShouldBeFalse();
        _validator.Validate(Query(reg: [bad])).IsValid.ShouldBeFalse();
        _validator.Validate(Query(mun: [bad])).IsValid.ShouldBeFalse();
        _validator.Validate(Query(emp: [bad])).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Max_size_lists_on_all_dimensions_pass()
    {
        var maxList = Enumerable.Range(0, SearchCriteria.MaxConceptIds)
            .Select(i => $"id{i}").ToList();
        var result = _validator.Validate(
            Query(occ: maxList, reg: maxList, mun: maxList, emp: maxList));
        result.IsValid.ShouldBeTrue();
    }
}

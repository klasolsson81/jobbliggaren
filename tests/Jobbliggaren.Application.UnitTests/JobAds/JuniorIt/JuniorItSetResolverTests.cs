using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree;
using Jobbliggaren.Infrastructure.JobAds.JuniorIt;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.JuniorIt;

// CA2012: an NSubstitute stub on a ValueTask-returning port member is the library's own idiom.
#pragma warning disable CA2012

/// <summary>
/// Where the junior IT list meets the taxonomy (ADR 0186): the field's occupation groups, or a loud failure when the
/// taxonomy cannot give them, since an empty group set would turn both arms off without a word. The taxonomy port is a
/// substitute shaped like the real tree; the real snapshot is read in <c>JuniorItJobAdQueryReadTests</c>.
/// </summary>
public class JuniorItSetResolverTests
{
    private const string DataIt = "apaJ_2ja_LuF";

    private static readonly JuniorItList List = new(
        "test", new DateOnly(2026, 11, 19), DataIt, "junior|trainee", ["90000001", "90000002"]);

    private static ITaxonomyReadModel TaxonomyWith(params TaxonomyOccupationFieldDto[] fields)
    {
        var taxonomy = Substitute.For<ITaxonomyReadModel>();
        taxonomy.GetTreeAsync(Arg.Any<CancellationToken>())
            .Returns(_ => new ValueTask<TaxonomyTreeDto>(new TaxonomyTreeDto([], fields, [], [])));
        return taxonomy;
    }

    private static TaxonomyOccupationFieldDto Field(string conceptId, params string[] groups) =>
        new(conceptId, conceptId, [], [.. groups.Select(g => new TaxonomyOccupationGroupDto(g, g))]);

    [Fact]
    public async Task ResolveAsync_GivesTheFieldsGroups_TheCuratedIds_AndThePattern()
    {
        var resolver = new JuniorItSetResolver(
            List, TaxonomyWith(Field("X82t_awd_Qyc", "2kLc_pto_DpV"), Field(DataIt, "DJh5_yyF_hEM", "hmaC_cfi_UKg")));

        var set = await resolver.ResolveAsync(CancellationToken.None);

        set.OccupationGroups.ShouldBe(["DJh5_yyF_hEM", "hmaC_cfi_UKg"]);
        set.CuratedExternalIds.ShouldBe(["90000001", "90000002"]);
        set.HeadlinePattern.ShouldBe("junior|trainee");
    }

    [Fact]
    public async Task ResolveAsync_FailsLoud_WhenTheTaxonomyHasNoSuchField()
    {
        var resolver = new JuniorItSetResolver(List, TaxonomyWith(Field("X82t_awd_Qyc", "2kLc_pto_DpV")));

        await Should.ThrowAsync<InvalidOperationException>(() => resolver.ResolveAsync(CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task ResolveAsync_FailsLoud_WhenTheFieldHasNoGroups()
    {
        var resolver = new JuniorItSetResolver(List, TaxonomyWith(Field(DataIt)));

        await Should.ThrowAsync<InvalidOperationException>(() => resolver.ResolveAsync(CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task ResolveAsync_MatchesTheFieldIdOrdinally()
    {
        var resolver = new JuniorItSetResolver(List, TaxonomyWith(Field(DataIt.ToUpperInvariant(), "DJh5_yyF_hEM")));

        await Should.ThrowAsync<InvalidOperationException>(() => resolver.ResolveAsync(CancellationToken.None).AsTask());
    }
}
#pragma warning restore CA2012

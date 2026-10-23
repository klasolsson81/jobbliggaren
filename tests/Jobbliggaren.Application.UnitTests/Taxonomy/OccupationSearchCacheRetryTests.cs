using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree;
using Jobbliggaren.Infrastructure.Taxonomy;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Taxonomy;

/// <summary>
/// #2198 (ADR 0180) — the two lazy caches on the search path publish only a successful build, so a
/// read model that faults (the database unavailable at cold start, the state the endpoint's 500
/// canary names) and then recovers is read again on the next call, instead of answering 500 until a
/// restart. The tree carries a real v30 pair: Frontend-utvecklare GDHs_eoz_uKx → Mjukvaru- och
/// systemutvecklare m.fl. DJh5_yyF_hEM (field Data/IT apaJ_2ja_LuF), which the frozen map resolves.
/// </summary>
public class OccupationSearchCacheRetryTests
{
    private const string GroupId = "DJh5_yyF_hEM";

    private static readonly TaxonomyTreeDto Tree = new(
        Regions: [],
        OccupationFields:
        [
            new TaxonomyOccupationFieldDto(
                "apaJ_2ja_LuF",
                "Data/IT",
                [new TaxonomyOccupationDto("GDHs_eoz_uKx", "Frontend-utvecklare")],
                [new TaxonomyOccupationGroupDto(GroupId, "Mjukvaru- och systemutvecklare m.fl.")]),
        ],
        EmploymentTypes: [],
        WorktimeExtents: []);

    [Fact]
    public async Task GetAsync_AfterAFaultedBuild_ReadsTheTreeAgainAndSucceeds()
    {
        var ct = TestContext.Current.CancellationToken;
        var taxonomy = new FaultOnceTaxonomy(Tree);
        var sut = new OccupationNameIndex(taxonomy);

        await Should.ThrowAsync<InvalidOperationException>(async () => await sut.GetAsync(ct));
        var contents = await sut.GetAsync(ct);

        contents.Names.ShouldHaveSingleItem().GroupConceptId.ShouldBe(GroupId);
        taxonomy.Reads.ShouldBe(2);
    }

    [Fact]
    public async Task SearchAsync_AfterAFaultedBuild_PreparesAgainAndAnswers()
    {
        var ct = TestContext.Current.CancellationToken;
        var sut = new OccupationSearcher(new OccupationNameIndex(new FaultOnceTaxonomy(Tree)));

        await Should.ThrowAsync<InvalidOperationException>(async () => await sut.SearchAsync("frontend", ct));
        var result = await sut.SearchAsync("frontend", ct);

        result.Rows.ShouldHaveSingleItem().GroupConceptId.ShouldBe(GroupId);
    }

    // Hand-rolled: the first tree read faults like an unreachable database, every later one answers.
    private sealed class FaultOnceTaxonomy(TaxonomyTreeDto tree) : ITaxonomyReadModel
    {
        public int Reads { get; private set; }

        public ValueTask<TaxonomyTreeDto> GetTreeAsync(CancellationToken cancellationToken)
        {
            Reads++;
            return Reads == 1
                ? ValueTask.FromException<TaxonomyTreeDto>(new InvalidOperationException("The taxonomy store is unavailable."))
                : new ValueTask<TaxonomyTreeDto>(tree);
        }

        public ValueTask<IReadOnlyList<TaxonomyLabelDto>> ResolveLabelsAsync(
            IReadOnlyList<string> conceptIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException("OccupationNameIndex only reads GetTreeAsync.");

        public ValueTask<IReadOnlyList<TaxonomySuggestionDto>> SuggestByPrefixAsync(
            string prefix, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException("OccupationNameIndex only reads GetTreeAsync.");

        public ValueTask<IReadOnlyList<string>> GetRelatedOccupationGroupsAsync(
            IReadOnlyList<string> ssyk4ConceptIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException("OccupationNameIndex only reads GetTreeAsync.");

        public ValueTask<IReadOnlyList<string>> GetContainingRegionsAsync(
            IReadOnlyList<string> municipalityConceptIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException("OccupationNameIndex only reads GetTreeAsync.");

        public ValueTask<IReadOnlyList<string>> GetContainingOccupationFieldsAsync(
            IReadOnlyList<string> occupationGroupConceptIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException("OccupationNameIndex only reads GetTreeAsync.");
    }
}

using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Application.JobAds.Queries.GetTaxonomyTree;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.JobAds.Queries.GetTaxonomyTree;

// ADR 0043 — GetTaxonomyTreeQueryHandler + ResolveTaxonomyLabelsQueryHandler
// är tunna adaptrar mot ITaxonomyReadModel-porten (speglar
// SuggestJobAdTermsQueryHandler). Ingen Npgsql/EF i Application — porten mockas
// med NSubstitute. Verifierar ren delegering + argument-passthrough.
//
// CA2012: NSubstitute-stubbning av ValueTask-returnerande port-medlemmar är
// ett känt analyzer-false-positive (substitute-anropet KONSUMERAS aldrig — det
// interceptas av NSubstitute för att registrera Returns). Suppression är
// scoped till mock-setup, inte produktionskod.
#pragma warning disable CA2012
public class TaxonomyQueryHandlersTests
{
    private readonly ITaxonomyReadModel _taxonomy = Substitute.For<ITaxonomyReadModel>();

    [Fact]
    public async Task Handle_ShouldReturnPortTree_WhenGetTaxonomyTreeQuery()
    {
        var ct = TestContext.Current.CancellationToken;
        var tree = new TaxonomyTreeDto(
            [new TaxonomyRegionDto("r1", "Stockholms län")],
            [new TaxonomyOccupationFieldDto("f1", "Data/IT",
                [new TaxonomyOccupationDto("o1", "Backend-utvecklare")])]);
        _taxonomy.GetTreeAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TaxonomyTreeDto>(tree));
        var sut = new GetTaxonomyTreeQueryHandler(_taxonomy);

        var result = await sut.Handle(new GetTaxonomyTreeQuery(), ct);

        result.ShouldBeSameAs(tree);
    }

    [Fact]
    public async Task Handle_ShouldDelegateOnceToPort_WhenGetTaxonomyTreeQuery()
    {
        var ct = TestContext.Current.CancellationToken;
        _taxonomy.GetTreeAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TaxonomyTreeDto>(
                new TaxonomyTreeDto([], [])));
        var sut = new GetTaxonomyTreeQueryHandler(_taxonomy);

        await sut.Handle(new GetTaxonomyTreeQuery(), ct);

        await _taxonomy.Received(1).GetTreeAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldReturnPortLabels_WhenResolveTaxonomyLabelsQuery()
    {
        var ct = TestContext.Current.CancellationToken;
        IReadOnlyList<string> ids = ["r1", "unknown-xyz"];
        IReadOnlyList<TaxonomyLabelDto> labels =
        [
            new TaxonomyLabelDto("r1", "Stockholms län"),
            new TaxonomyLabelDto("unknown-xyz", "Okänd kod (unknown-xyz)"),
        ];
        _taxonomy.ResolveLabelsAsync(ids, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<TaxonomyLabelDto>>(labels));
        var sut = new ResolveTaxonomyLabelsQueryHandler(_taxonomy);

        var result = await sut.Handle(new ResolveTaxonomyLabelsQuery(ids), ct);

        result.ShouldBeSameAs(labels);
    }

    [Fact]
    public async Task Handle_ShouldPassConceptIdsThrough_WhenResolveTaxonomyLabelsQuery()
    {
        var ct = TestContext.Current.CancellationToken;
        IReadOnlyList<string> ids = ["a", "b", "c"];
        _taxonomy.ResolveLabelsAsync(Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<TaxonomyLabelDto>>(
                (IReadOnlyList<TaxonomyLabelDto>)[]));
        var sut = new ResolveTaxonomyLabelsQueryHandler(_taxonomy);

        await sut.Handle(new ResolveTaxonomyLabelsQuery(ids), ct);

        await _taxonomy.Received(1).ResolveLabelsAsync(ids,
            Arg.Any<CancellationToken>());
    }
}
#pragma warning restore CA2012

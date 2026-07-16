using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Application.CompanyWatches.Queries.GetCriterionReference;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches.Queries;

/// <summary>#560 PR-3 (Fork G2) — the picker tree projection: hierarchy grouped correctly and
/// version stamps surfaced (a stale FE cache must be diagnosable, never guessed at).</summary>
public class GetCriterionReferenceQueryHandlerTests
{
    [Fact]
    public async Task Handle_GroupsTheHierarchy_AndSurfacesTheVersions()
    {
        var ct = TestContext.Current.CancellationToken;

        var provider = Substitute.For<ICriterionReferenceProvider>();
        provider.Sni.Returns(new SniReferenceCatalog(
            "2025.v1",
            [new SniSection("A", "Jordbruk"), new SniSection("K", "IT och telekommunikation")],
            [
                new SniDivision("01", "A", "Jordbruk och jakt"),
                new SniDivision("62", "K", "IT-tjänster"),
                new SniDivision("63", "K", "Informationstjänster"),
            ],
            [
                new SniLeaf("01110", "01", "Odling av spannmål"),
                new SniLeaf("62100", "62", "Datorprogrammering"),
                new SniLeaf("62201", "62", "IT-konsult"),
                new SniLeaf("63100", "63", "Databehandling"),
            ]));
        provider.Kommuner.Returns(new KommunReferenceCatalog(
            "2026-01-01.v1",
            [new LanEntry("01", "Stockholms län"), new LanEntry("14", "Västra Götalands län")],
            [
                new KommunEntry("0180", "Stockholm", "01"),
                new KommunEntry("0184", "Solna", "01"),
                new KommunEntry("1480", "Göteborg", "14"),
            ]));

        var tree = await new GetCriterionReferenceQueryHandler(provider)
            .Handle(new GetCriterionReferenceQuery(), ct);

        tree.SniVersion.ShouldBe("2025.v1");
        tree.KommunVersion.ShouldBe("2026-01-01.v1");

        var k = tree.Sni.Single(static s => s.Code == "K");
        k.Divisions.Count.ShouldBe(2);
        k.Divisions.Single(static d => d.Code == "62").Leaves
            .Select(static l => l.Code).ShouldBe(["62100", "62201"]);
        tree.Sni.Single(static s => s.Code == "A")
            .Divisions.ShouldHaveSingleItem()
            .Leaves.ShouldHaveSingleItem().Name.ShouldBe("Odling av spannmål");

        var stockholm = tree.Lan.Single(static l => l.Code == "01");
        stockholm.Kommuner.Select(static k => k.Code).ShouldBe(["0180", "0184"]);
        tree.Lan.Single(static l => l.Code == "14")
            .Kommuner.ShouldHaveSingleItem().Name.ShouldBe("Göteborg");
    }
}

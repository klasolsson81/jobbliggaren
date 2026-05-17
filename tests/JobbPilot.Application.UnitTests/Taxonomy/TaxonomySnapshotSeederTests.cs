using JobbPilot.Infrastructure.Taxonomy;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Taxonomy;

// ADR 0043 (Variant A) — internal static MapRows/LoadSnapshot + grace-period
// gate testas direkt (InternalsVisibleTo: JobbPilot.Application.UnitTests).
// Speglar IdempotentAdminRoleSeederTests grace-period-mönstret.
public class TaxonomySnapshotSeederTests
{
    [Theory]
    [InlineData("Development", true)]
    [InlineData("Test", true)]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    [InlineData("DEV", false)] // exakt "Development" — case-sensitivt
    public void IsSchemaInitGracePeriod_ShouldGateOnEnvironmentName_WhenChecked(
        string envName, bool expected)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(envName);

        var actual = TaxonomySnapshotSeeder.IsSchemaInitGracePeriod(env);

        actual.ShouldBe(expected);
    }

    [Fact]
    public void LoadSnapshot_ShouldDeserializeEmbeddedResource_WhenCalled()
    {
        var snapshot = TaxonomySnapshotSeeder.LoadSnapshot();

        snapshot.ShouldNotBeNull();
        // Version får inte vara defaultsentinel "unknown" — den committade
        // snapshotten ska bära en riktig JobTech-taxonomi-version.
        snapshot.TaxonomyVersion.ShouldNotBeNullOrWhiteSpace();
        snapshot.TaxonomyVersion.ShouldNotBe("unknown");
    }

    [Fact]
    public void LoadSnapshot_ShouldContainBoundedNonEmptyHierarchy_WhenCalled()
    {
        var snapshot = TaxonomySnapshotSeeder.LoadSnapshot();

        // Drift-robust: assert:ar struktur + >0, inte exakta tal
        // (snapshot regenereras kvartalsvis → exakta tal är bräckliga).
        snapshot.Regions.ShouldNotBeEmpty();
        snapshot.OccupationFields.ShouldNotBeEmpty();
        snapshot.OccupationFields.ShouldAllBe(f => f.Occupations.Count > 0);

        snapshot.Regions.ShouldAllBe(r =>
            !string.IsNullOrWhiteSpace(r.ConceptId)
            && !string.IsNullOrWhiteSpace(r.Label));
        snapshot.OccupationFields.SelectMany(f => f.Occupations).ShouldAllBe(o =>
            !string.IsNullOrWhiteSpace(o.ConceptId)
            && !string.IsNullOrWhiteSpace(o.Label));
    }

    [Fact]
    public void LoadSnapshot_ShouldHaveUniqueConceptIdsAcrossHierarchy_WhenCalled()
    {
        var snapshot = TaxonomySnapshotSeeder.LoadSnapshot();

        var allIds = snapshot.Regions.Select(r => r.ConceptId)
            .Concat(snapshot.OccupationFields.Select(f => f.ConceptId))
            .Concat(snapshot.OccupationFields
                .SelectMany(f => f.Occupations).Select(o => o.ConceptId))
            .ToList();

        // Concept-id är PK i taxonomy_concepts → duplikat skulle spränga
        // seederns AddRange (PK-konflikt). Fångas redan i fil-form.
        allIds.Distinct().Count().ShouldBe(allIds.Count);
    }

    [Fact]
    public void MapRows_ShouldEmitRegionWithoutParentAndRegionKind_WhenRegionRow()
    {
        var file = new TaxonomySnapshotFile
        {
            TaxonomyVersion = "test",
            Regions = [new TaxonomySnapshotFile.SnapshotRegion("r-1", "Skåne län")],
            OccupationFields = [],
        };

        var rows = TaxonomySnapshotSeeder.MapRows(file);

        var region = rows.ShouldHaveSingleItem();
        region.ConceptId.ShouldBe("r-1");
        region.Kind.ShouldBe(TaxonomyConceptKind.Region);
        region.Label.ShouldBe("Skåne län");
        region.ParentConceptId.ShouldBeNull();
    }

    [Fact]
    public void MapRows_ShouldEmitFieldWithoutParentAndOccupationWithFieldParent_WhenFieldRow()
    {
        var file = new TaxonomySnapshotFile
        {
            TaxonomyVersion = "test",
            Regions = [],
            OccupationFields =
            [
                new TaxonomySnapshotFile.SnapshotOccupationField(
                    "f-1", "Data/IT",
                    [
                        new TaxonomySnapshotFile.SnapshotOccupation("o-1", "Backend-utvecklare"),
                        new TaxonomySnapshotFile.SnapshotOccupation("o-2", "Frontend-utvecklare"),
                    ]),
            ],
        };

        var rows = TaxonomySnapshotSeeder.MapRows(file);

        var field = rows
            .Where(r => r.Kind == TaxonomyConceptKind.OccupationField)
            .ShouldHaveSingleItem();
        field.ConceptId.ShouldBe("f-1");
        field.ParentConceptId.ShouldBeNull();

        var occupations = rows.Where(r => r.Kind == TaxonomyConceptKind.Occupation).ToList();
        occupations.Count.ShouldBe(2);
        occupations.ShouldAllBe(o => o.ParentConceptId == "f-1");
        occupations.ShouldContain(o => o.ConceptId == "o-1" && o.Label == "Backend-utvecklare");
    }

    [Fact]
    public void MapRows_ShouldEmitRowCountEqualRegionsPlusFieldsPlusOccupations_WhenMixed()
    {
        var file = new TaxonomySnapshotFile
        {
            TaxonomyVersion = "test",
            Regions =
            [
                new TaxonomySnapshotFile.SnapshotRegion("r-1", "Skåne län"),
                new TaxonomySnapshotFile.SnapshotRegion("r-2", "Stockholms län"),
            ],
            OccupationFields =
            [
                new TaxonomySnapshotFile.SnapshotOccupationField("f-1", "Data/IT",
                    [
                        new TaxonomySnapshotFile.SnapshotOccupation("o-1", "A"),
                        new TaxonomySnapshotFile.SnapshotOccupation("o-2", "B"),
                    ]),
                new TaxonomySnapshotFile.SnapshotOccupationField("f-2", "Vård",
                    [new TaxonomySnapshotFile.SnapshotOccupation("o-3", "C")]),
            ],
        };

        var rows = TaxonomySnapshotSeeder.MapRows(file);

        var expected = file.Regions.Count
            + file.OccupationFields.Count
            + file.OccupationFields.Sum(f => f.Occupations.Count);
        rows.Count.ShouldBe(expected); // 2 + 2 + 3 = 7
    }

    [Fact]
    public void MapRows_ShouldRoundTripCommittedSnapshot_WhenInvariantHolds()
    {
        // Bro mellan unit-MapRows och den faktiska committade snapshotten:
        // radantal = regioner + fält + sum(yrken). Drift-robust (relations-
        // invariant, ej exakta tal).
        var snapshot = TaxonomySnapshotSeeder.LoadSnapshot();

        var rows = TaxonomySnapshotSeeder.MapRows(snapshot);

        var expected = snapshot.Regions.Count
            + snapshot.OccupationFields.Count
            + snapshot.OccupationFields.Sum(f => f.Occupations.Count);
        rows.Count.ShouldBe(expected);
        rows.Count(r => r.Kind == TaxonomyConceptKind.Region)
            .ShouldBe(snapshot.Regions.Count);
        rows.Count(r => r.Kind == TaxonomyConceptKind.OccupationField)
            .ShouldBe(snapshot.OccupationFields.Count);
        rows.Where(r => r.Kind == TaxonomyConceptKind.Occupation)
            .ShouldAllBe(r => r.ParentConceptId != null);
    }
}

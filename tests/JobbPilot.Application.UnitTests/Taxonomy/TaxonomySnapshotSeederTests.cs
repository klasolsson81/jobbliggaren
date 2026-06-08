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

        // Fas B1 (sök-paritet): unikheten ska hålla över HELA hierarkin —
        // regioner + kommuner + yrkesområden + yrkesgrupper + yrken. Concept-id
        // är PK i taxonomy_concepts → en kollision (t.ex. mellan en yrkesgrupps-
        // och en yrkes-id) skulle spränga seederns AddRange (PK-konflikt).
        // `?? []` speglar att kommuner/yrkesgrupper är nullable nested fält.
        var allIds = snapshot.Regions.Select(r => r.ConceptId)
            .Concat(snapshot.Regions
                .SelectMany(r => r.Municipalities ?? []).Select(m => m.ConceptId))
            .Concat(snapshot.OccupationFields.Select(f => f.ConceptId))
            .Concat(snapshot.OccupationFields
                .SelectMany(f => f.OccupationGroups ?? []).Select(g => g.ConceptId))
            .Concat(snapshot.OccupationFields
                .SelectMany(f => f.Occupations).Select(o => o.ConceptId))
            .ToList();

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
        // radantal = regioner + sum(kommuner) + fält + sum(yrken)
        // + sum(yrkesgrupper). Drift-robust (relations-invariant, ej exakta tal).
        // `?? []` håller bakåtkompat om nested-listorna skulle vara null.
        var snapshot = TaxonomySnapshotSeeder.LoadSnapshot();

        var rows = TaxonomySnapshotSeeder.MapRows(snapshot);

        var expectedMunicipalities =
            snapshot.Regions.Sum(r => (r.Municipalities ?? []).Count);
        var expectedOccupationGroups =
            snapshot.OccupationFields.Sum(f => (f.OccupationGroups ?? []).Count);

        var expected = snapshot.Regions.Count
            + expectedMunicipalities
            + snapshot.OccupationFields.Count
            + snapshot.OccupationFields.Sum(f => f.Occupations.Count)
            + expectedOccupationGroups;
        rows.Count.ShouldBe(expected);
        rows.Count(r => r.Kind == TaxonomyConceptKind.Region)
            .ShouldBe(snapshot.Regions.Count);
        rows.Count(r => r.Kind == TaxonomyConceptKind.OccupationField)
            .ShouldBe(snapshot.OccupationFields.Count);
        rows.Count(r => r.Kind == TaxonomyConceptKind.Municipality)
            .ShouldBe(expectedMunicipalities);
        rows.Count(r => r.Kind == TaxonomyConceptKind.OccupationGroup)
            .ShouldBe(expectedOccupationGroups);
        rows.Where(r => r.Kind == TaxonomyConceptKind.Occupation)
            .ShouldAllBe(r => r.ParentConceptId != null);
    }

    // ───────────────────────────────────────────────────────────────────
    // Fas B1 (Platsbanken sök-paritet, Klass 1): Municipality (parent=Region)
    // + OccupationGroup (parent=OccupationField). MapRows utökas; befintliga
    // occupations OFÖRÄNDRADE. `?? []` ger bakåtkompat för null nested-listor.
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void MapRows_ShouldEmitMunicipalityWithRegionParentAndMunicipalityKind_WhenRegionHasMunicipalities()
    {
        var file = new TaxonomySnapshotFile
        {
            TaxonomyVersion = "test",
            Regions =
            [
                new TaxonomySnapshotFile.SnapshotRegion("r-1", "Skåne län",
                    Municipalities:
                    [
                        new TaxonomySnapshotFile.SnapshotMunicipality("m-1", "Malmö"),
                        new TaxonomySnapshotFile.SnapshotMunicipality("m-2", "Lund"),
                    ]),
            ],
            OccupationFields = [],
        };

        var rows = TaxonomySnapshotSeeder.MapRows(file);

        // Regionen själv emitteras alltjämt utan parent.
        var region = rows
            .Where(r => r.Kind == TaxonomyConceptKind.Region)
            .ShouldHaveSingleItem();
        region.ParentConceptId.ShouldBeNull();

        var municipalities = rows
            .Where(r => r.Kind == TaxonomyConceptKind.Municipality).ToList();
        municipalities.Count.ShouldBe(2);
        municipalities.ShouldAllBe(m => m.ParentConceptId == "r-1");
        municipalities.ShouldContain(m => m.ConceptId == "m-1" && m.Label == "Malmö");
        municipalities.ShouldContain(m => m.ConceptId == "m-2" && m.Label == "Lund");
    }

    [Fact]
    public void MapRows_ShouldEmitOccupationGroupWithFieldParentAndOccupationGroupKind_WhenFieldHasGroups()
    {
        var file = new TaxonomySnapshotFile
        {
            TaxonomyVersion = "test",
            Regions = [],
            OccupationFields =
            [
                new TaxonomySnapshotFile.SnapshotOccupationField(
                    "f-1", "Data/IT",
                    Occupations:
                    [
                        new TaxonomySnapshotFile.SnapshotOccupation("o-1", "Backend-utvecklare"),
                    ],
                    OccupationGroups:
                    [
                        new TaxonomySnapshotFile.SnapshotOccupationGroup("g-1", "Mjukvaru- och systemutvecklare"),
                        new TaxonomySnapshotFile.SnapshotOccupationGroup("g-2", "Systemanalytiker och IT-arkitekter"),
                    ]),
            ],
        };

        var rows = TaxonomySnapshotSeeder.MapRows(file);

        // Fältet själv är oförändrat (rot, ingen parent).
        var field = rows
            .Where(r => r.Kind == TaxonomyConceptKind.OccupationField)
            .ShouldHaveSingleItem();
        field.ParentConceptId.ShouldBeNull();

        // Befintliga occupations OFÖRÄNDRADE — fortfarande parent=fält.
        var occupations = rows
            .Where(r => r.Kind == TaxonomyConceptKind.Occupation).ToList();
        occupations.ShouldAllBe(o => o.ParentConceptId == "f-1");

        var groups = rows
            .Where(r => r.Kind == TaxonomyConceptKind.OccupationGroup).ToList();
        groups.Count.ShouldBe(2);
        groups.ShouldAllBe(g => g.ParentConceptId == "f-1");
        groups.ShouldContain(g => g.ConceptId == "g-1" && g.Label == "Mjukvaru- och systemutvecklare");
        groups.ShouldContain(g => g.ConceptId == "g-2" && g.Label == "Systemanalytiker och IT-arkitekter");
    }

    [Fact]
    public void MapRows_ShouldEmitNoMunicipalityOrGroupRows_WhenNestedListsAreNull()
    {
        // Bakåtkompat: en region/fält utan municipalities/occupationGroups
        // (null, default-arg) ska INTE producera extra rader. `?? []` i MapRows.
        var file = new TaxonomySnapshotFile
        {
            TaxonomyVersion = "test",
            Regions = [new TaxonomySnapshotFile.SnapshotRegion("r-1", "Skåne län")],
            OccupationFields =
            [
                new TaxonomySnapshotFile.SnapshotOccupationField("f-1", "Data/IT",
                    [new TaxonomySnapshotFile.SnapshotOccupation("o-1", "Backend-utvecklare")]),
            ],
        };

        var rows = TaxonomySnapshotSeeder.MapRows(file);

        rows.ShouldNotContain(r => r.Kind == TaxonomyConceptKind.Municipality);
        rows.ShouldNotContain(r => r.Kind == TaxonomyConceptKind.OccupationGroup);
        // Endast region + fält + yrke = 3 rader (oförändrat beteende).
        rows.Count.ShouldBe(3);
    }

    [Fact]
    public void MapRows_ShouldEmitMunicipalitiesAndGroupsAndOccupations_WhenFullyPopulated()
    {
        // Kombinerad rad-räkning över alla fem Kinds samtidigt.
        var file = new TaxonomySnapshotFile
        {
            TaxonomyVersion = "test",
            Regions =
            [
                new TaxonomySnapshotFile.SnapshotRegion("r-1", "Skåne län",
                    Municipalities:
                    [
                        new TaxonomySnapshotFile.SnapshotMunicipality("m-1", "Malmö"),
                        new TaxonomySnapshotFile.SnapshotMunicipality("m-2", "Lund"),
                    ]),
            ],
            OccupationFields =
            [
                new TaxonomySnapshotFile.SnapshotOccupationField("f-1", "Data/IT",
                    Occupations:
                    [
                        new TaxonomySnapshotFile.SnapshotOccupation("o-1", "A"),
                        new TaxonomySnapshotFile.SnapshotOccupation("o-2", "B"),
                    ],
                    OccupationGroups:
                    [
                        new TaxonomySnapshotFile.SnapshotOccupationGroup("g-1", "G1"),
                    ]),
            ],
        };

        var rows = TaxonomySnapshotSeeder.MapRows(file);

        rows.Count(r => r.Kind == TaxonomyConceptKind.Region).ShouldBe(1);
        rows.Count(r => r.Kind == TaxonomyConceptKind.Municipality).ShouldBe(2);
        rows.Count(r => r.Kind == TaxonomyConceptKind.OccupationField).ShouldBe(1);
        rows.Count(r => r.Kind == TaxonomyConceptKind.OccupationGroup).ShouldBe(1);
        rows.Count(r => r.Kind == TaxonomyConceptKind.Occupation).ShouldBe(2);
        rows.Count.ShouldBe(1 + 2 + 1 + 1 + 2); // 7
    }

    [Fact]
    public void MapRows_ShouldRoundTripMunicipalityAndGroupCountsFromCommittedSnapshot_WhenInvariantHolds()
    {
        // Hårdkodade paritets-tal från den committade snapshoten (version "30"):
        // 290 kommuner + 400 yrkesgrupper. Drift-robust komplement: jämför
        // även mot summan från snapshoten (om snapshoten regenereras med andra
        // tal fångar relations-varianten det medan 290/400 dokumenterar
        // Platsbanken-paritets-baseline vid B1).
        var snapshot = TaxonomySnapshotSeeder.LoadSnapshot();

        var rows = TaxonomySnapshotSeeder.MapRows(snapshot);

        var municipalityRows = rows.Count(r => r.Kind == TaxonomyConceptKind.Municipality);
        var groupRows = rows.Count(r => r.Kind == TaxonomyConceptKind.OccupationGroup);

        // Relations-baserad (drift-robust) — primär invariant.
        municipalityRows.ShouldBe(snapshot.Regions.Sum(r => (r.Municipalities ?? []).Count));
        groupRows.ShouldBe(snapshot.OccupationFields.Sum(f => (f.OccupationGroups ?? []).Count));

        // Paritets-baseline vid Fas B1 (Platsbanken: 290 kommuner / 400 yrkesgrupper).
        municipalityRows.ShouldBe(290);
        groupRows.ShouldBe(400);

        // Alla kommun-/grupp-rader har giltig parent.
        rows.Where(r => r.Kind == TaxonomyConceptKind.Municipality)
            .ShouldAllBe(r => r.ParentConceptId != null);
        rows.Where(r => r.Kind == TaxonomyConceptKind.OccupationGroup)
            .ShouldAllBe(r => r.ParentConceptId != null);
    }

    [Fact]
    public void LoadSnapshot_ShouldContainNonEmptyValidMunicipalitiesAndGroups_WhenCalled()
    {
        // Committade snapshoten (version "30") ska bära kommuner + yrkesgrupper
        // med giltiga conceptId/label, och varje barn-parent-relation ska finnas
        // i snapshoten (kommun→region 1:1, yrkesgrupp→yrkesområde 1:1).
        var snapshot = TaxonomySnapshotSeeder.LoadSnapshot();

        var municipalities = snapshot.Regions
            .SelectMany(r => (r.Municipalities ?? []).Select(m => (m, region: r)))
            .ToList();
        municipalities.ShouldNotBeEmpty();
        municipalities.ShouldAllBe(x =>
            !string.IsNullOrWhiteSpace(x.m.ConceptId)
            && !string.IsNullOrWhiteSpace(x.m.Label));

        var regionIds = snapshot.Regions.Select(r => r.ConceptId).ToHashSet();
        municipalities.ShouldAllBe(x => regionIds.Contains(x.region.ConceptId));

        var groups = snapshot.OccupationFields
            .SelectMany(f => (f.OccupationGroups ?? []).Select(g => (g, field: f)))
            .ToList();
        groups.ShouldNotBeEmpty();
        groups.ShouldAllBe(x =>
            !string.IsNullOrWhiteSpace(x.g.ConceptId)
            && !string.IsNullOrWhiteSpace(x.g.Label));

        var fieldIds = snapshot.OccupationFields.Select(f => f.ConceptId).ToHashSet();
        groups.ShouldAllBe(x => fieldIds.Contains(x.field.ConceptId));
    }
}

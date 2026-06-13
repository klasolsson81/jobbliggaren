using Jobbliggaren.Infrastructure.Taxonomy;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Taxonomy;

// ADR 0043 (Variant A) — internal static MapRows/LoadSnapshot + grace-period
// gate testas direkt (InternalsVisibleTo: Jobbliggaren.Application.UnitTests).
// Speglar IdempotentAdminRoleSeederTests grace-period-mönstret.
public class TaxonomySnapshotSeederTests
{
    // Tom Klass 2-fixtur — speglar TaxonomySnapshotFile-byggstilen ovan, men för
    // de syntetiska region-/occupation-projektions-testen som inte bryr sig om
    // anställningsform/omfattning. Default:as till tomma listor så befintliga
    // rad-räkningar/projektions-assertions förblir giltiga (MapRows kräver nu
    // ett andra Klass2-argument). EmptyKlass2() håller region/occupation-testen
    // ortogonala mot Klass 2 (lägger 0 rader).
    private static Klass2TaxonomyFile EmptyKlass2() => new()
    {
        Version = "test",
        EmploymentTypes = [],
        WorktimeExtents = [],
    };

    private static Klass2TaxonomyFile Klass2(
        IReadOnlyList<Klass2TaxonomyFile.Klass2Option> employmentTypes,
        IReadOnlyList<Klass2TaxonomyFile.Klass2Option> worktimeExtents) => new()
        {
            Version = "test",
            EmploymentTypes = employmentTypes,
            WorktimeExtents = worktimeExtents,
        };

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

        var rows = TaxonomySnapshotSeeder.MapRows(file, EmptyKlass2());

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

        var rows = TaxonomySnapshotSeeder.MapRows(file, EmptyKlass2());

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

        var rows = TaxonomySnapshotSeeder.MapRows(file, EmptyKlass2());

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

        // EmptyKlass2(): denna invariant räknar bara region/occupation-hierarkin
        // (Klass 2-rad-räkningen verifieras separat i MapRows_ShouldEmit…Klass2-
        // testen nedan), så de plattа dimensionerna hålls utanför totalen här.
        var rows = TaxonomySnapshotSeeder.MapRows(snapshot, EmptyKlass2());

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

        var rows = TaxonomySnapshotSeeder.MapRows(file, EmptyKlass2());

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

        var rows = TaxonomySnapshotSeeder.MapRows(file, EmptyKlass2());

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

        var rows = TaxonomySnapshotSeeder.MapRows(file, EmptyKlass2());

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

        var rows = TaxonomySnapshotSeeder.MapRows(file, EmptyKlass2());

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

        var rows = TaxonomySnapshotSeeder.MapRows(snapshot, EmptyKlass2());

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

    // ───────────────────────────────────────────────────────────────────
    // Fas E Klass 2 (ADR 0043-amendment 2026-06-13): anställningsform +
    // omfattning. PLATTA/föräldralösa dimensioner från en SEPARAT frusen
    // embedded resurs (klass2-taxonomy.json). MapRows får ett andra Klass2-
    // argument och appenderar EmploymentType-/WorktimeExtent-rader EFTER
    // region/occupation-raderna. Inga parent-relationer (till skillnad mot
    // kommun/yrkesgrupp). Region/occupation-projektionen är OFÖRÄNDRAD.
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void MapRows_ShouldEmitEmploymentTypeRowsWithoutParentAndEmploymentTypeKind_WhenKlass2HasEmploymentTypes()
    {
        var snapshot = new TaxonomySnapshotFile
        {
            TaxonomyVersion = "test",
            Regions = [],
            OccupationFields = [],
        };
        var klass2 = Klass2(
            employmentTypes:
            [
                new Klass2TaxonomyFile.Klass2Option("PFZr_Syz_cUq", "Vanlig anställning"),
                new Klass2TaxonomyFile.Klass2Option("gro4_cWF_6D7", "Vikariat"),
            ],
            worktimeExtents: []);

        var rows = TaxonomySnapshotSeeder.MapRows(snapshot, klass2);

        var employmentTypes = rows
            .Where(r => r.Kind == TaxonomyConceptKind.EmploymentType).ToList();
        employmentTypes.Count.ShouldBe(2);
        // Platta/föräldralösa — ingen ParentConceptId (till skillnad mot kommun/grupp).
        employmentTypes.ShouldAllBe(e => e.ParentConceptId == null);
        employmentTypes.ShouldContain(e =>
            e.ConceptId == "PFZr_Syz_cUq" && e.Label == "Vanlig anställning");
        employmentTypes.ShouldContain(e =>
            e.ConceptId == "gro4_cWF_6D7" && e.Label == "Vikariat");
        // Inga worktime-/region-/occupation-rader läckte in.
        rows.ShouldAllBe(r => r.Kind == TaxonomyConceptKind.EmploymentType);
    }

    [Fact]
    public void MapRows_ShouldEmitWorktimeExtentRowsWithoutParentAndWorktimeExtentKind_WhenKlass2HasWorktimeExtents()
    {
        var snapshot = new TaxonomySnapshotFile
        {
            TaxonomyVersion = "test",
            Regions = [],
            OccupationFields = [],
        };
        var klass2 = Klass2(
            employmentTypes: [],
            worktimeExtents:
            [
                new Klass2TaxonomyFile.Klass2Option("6YE1_gAC_R2G", "Heltid"),
                new Klass2TaxonomyFile.Klass2Option("947z_JGS_Uk2", "Deltid"),
            ]);

        var rows = TaxonomySnapshotSeeder.MapRows(snapshot, klass2);

        var worktimeExtents = rows
            .Where(r => r.Kind == TaxonomyConceptKind.WorktimeExtent).ToList();
        worktimeExtents.Count.ShouldBe(2);
        worktimeExtents.ShouldAllBe(w => w.ParentConceptId == null);
        worktimeExtents.ShouldContain(w =>
            w.ConceptId == "6YE1_gAC_R2G" && w.Label == "Heltid");
        worktimeExtents.ShouldContain(w =>
            w.ConceptId == "947z_JGS_Uk2" && w.Label == "Deltid");
        rows.ShouldAllBe(r => r.Kind == TaxonomyConceptKind.WorktimeExtent);
    }

    [Fact]
    public void MapRows_ShouldEmitRowCountAcrossAllSevenKinds_WhenFullyPopulatedWithKlass2()
    {
        // Total rad-räkning = region + kommun + yrkesområde + yrke + yrkesgrupp
        // + anställningsform + omfattning. Klass 2-raderna är ADDITIVA ovanpå den
        // befintliga hierarkin (CTO BESLUT 1 — appenderas efter occupation-raderna).
        var snapshot = new TaxonomySnapshotFile
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
        var klass2 = Klass2(
            employmentTypes:
            [
                new Klass2TaxonomyFile.Klass2Option("PFZr_Syz_cUq", "Vanlig anställning"),
                new Klass2TaxonomyFile.Klass2Option("gro4_cWF_6D7", "Vikariat"),
                new Klass2TaxonomyFile.Klass2Option("sTu5_NBQ_udq", "Tidsbegränsad"),
            ],
            worktimeExtents:
            [
                new Klass2TaxonomyFile.Klass2Option("6YE1_gAC_R2G", "Heltid"),
                new Klass2TaxonomyFile.Klass2Option("947z_JGS_Uk2", "Deltid"),
            ]);

        var rows = TaxonomySnapshotSeeder.MapRows(snapshot, klass2);

        rows.Count(r => r.Kind == TaxonomyConceptKind.Region).ShouldBe(1);
        rows.Count(r => r.Kind == TaxonomyConceptKind.Municipality).ShouldBe(2);
        rows.Count(r => r.Kind == TaxonomyConceptKind.OccupationField).ShouldBe(1);
        rows.Count(r => r.Kind == TaxonomyConceptKind.Occupation).ShouldBe(2);
        rows.Count(r => r.Kind == TaxonomyConceptKind.OccupationGroup).ShouldBe(1);
        rows.Count(r => r.Kind == TaxonomyConceptKind.EmploymentType).ShouldBe(3);
        rows.Count(r => r.Kind == TaxonomyConceptKind.WorktimeExtent).ShouldBe(2);

        var expected = snapshot.Regions.Count
            + snapshot.Regions.Sum(r => (r.Municipalities ?? []).Count)
            + snapshot.OccupationFields.Count
            + snapshot.OccupationFields.Sum(f => f.Occupations.Count)
            + snapshot.OccupationFields.Sum(f => (f.OccupationGroups ?? []).Count)
            + klass2.EmploymentTypes.Count
            + klass2.WorktimeExtents.Count;
        rows.Count.ShouldBe(expected); // 1 + 2 + 1 + 2 + 1 + 3 + 2 = 12
    }

    [Fact]
    public void MapRows_ShouldEmitNoKlass2Rows_WhenKlass2IsEmpty()
    {
        // Bakåtkompat/ortogonalitet: en tom Klass2-fil ska INTE addera rader —
        // region/occupation-projektionen står oförändrad (skyddar EmptyKlass2()-
        // antagandet som de äldre projektions-testen bygger på).
        var snapshot = new TaxonomySnapshotFile
        {
            TaxonomyVersion = "test",
            Regions = [new TaxonomySnapshotFile.SnapshotRegion("r-1", "Skåne län")],
            OccupationFields = [],
        };

        var rows = TaxonomySnapshotSeeder.MapRows(snapshot, EmptyKlass2());

        rows.ShouldNotContain(r => r.Kind == TaxonomyConceptKind.EmploymentType);
        rows.ShouldNotContain(r => r.Kind == TaxonomyConceptKind.WorktimeExtent);
        rows.Count.ShouldBe(1); // endast regionen
    }

    [Fact]
    public void LoadKlass2_ShouldDeserializeEmbeddedResource_WhenCalled()
    {
        // Bevisar att klass2-taxonomy.json är registrerad som <EmbeddedResource>
        // i Jobbliggaren.Infrastructure.csproj OCH parsar mot Klass2TaxonomyFile.
        // Drift-robust mot label-redigeringar (assert:ar antal + en stabil
        // spot-check-relation, ej hela corpus-listan).
        var klass2 = TaxonomySnapshotSeeder.LoadKlass2();

        klass2.ShouldNotBeNull();
        klass2.Version.ShouldBe("1");
        // Frusen, legaldefinierad mängd (CTO BESLUT 1) — exakta tal är stabila här
        // (till skillnad mot den kvartalsregenererade snapshoten): 8 + 2.
        klass2.EmploymentTypes.Count.ShouldBe(8);
        klass2.WorktimeExtents.Count.ShouldBe(2);

        // Spot-check: en känd concept-id/label-relation ur dev-corpus.
        klass2.WorktimeExtents.ShouldContain(w =>
            w.ConceptId == "6YE1_gAC_R2G" && w.Label == "Heltid");
        klass2.EmploymentTypes.ShouldContain(e =>
            e.ConceptId == "PFZr_Syz_cUq" && e.Label == "Vanlig anställning");

        // Alla optioner ska bära giltiga concept-id + label (ingen tom rad).
        klass2.EmploymentTypes.ShouldAllBe(e =>
            !string.IsNullOrWhiteSpace(e.ConceptId)
            && !string.IsNullOrWhiteSpace(e.Label));
        klass2.WorktimeExtents.ShouldAllBe(w =>
            !string.IsNullOrWhiteSpace(w.ConceptId)
            && !string.IsNullOrWhiteSpace(w.Label));
    }

    [Fact]
    public void CompositeVersion_ShouldCombineSnapshotAndKlass2Versions_WhenCalled()
    {
        // Idempotens-nyckel: "{taxonomyVersion}+klass2-{klass2Version}". Bump av
        // endera versionen ändrar nyckeln → re-seed triggas (meta-jämförelsen
        // i StartAsync). Verifierar exakt format mot sample-inputs.
        var snapshot = new TaxonomySnapshotFile
        {
            TaxonomyVersion = "30",
            Regions = [],
            OccupationFields = [],
        };
        var klass2 = Klass2(employmentTypes: [], worktimeExtents: []);

        var version = TaxonomySnapshotSeeder.CompositeVersion(snapshot, klass2);

        version.ShouldBe("30+klass2-test");
    }

    [Fact]
    public void CompositeVersion_ShouldReflectKlass2VersionBump_WhenKlass2VersionDiffers()
    {
        var snapshot = new TaxonomySnapshotFile
        {
            TaxonomyVersion = "30",
            Regions = [],
            OccupationFields = [],
        };

        var v1 = TaxonomySnapshotSeeder.CompositeVersion(
            snapshot, new Klass2TaxonomyFile { Version = "1" });
        var v2 = TaxonomySnapshotSeeder.CompositeVersion(
            snapshot, new Klass2TaxonomyFile { Version = "2" });

        v1.ShouldBe("30+klass2-1");
        v2.ShouldBe("30+klass2-2");
        v1.ShouldNotBe(v2); // bump → ny idempotens-nyckel → re-seed
    }
}

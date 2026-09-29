using System.Collections;
using System.Reflection;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobSeekers.Commands;
using Jobbliggaren.Application.JobSeekers.Commands.UpdateMatchPreferences;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobSeekers.Commands.UpdateMatchPreferences;

// #1918 — the per-part write. A part the command carries replaces that part; a part it does not
// carry stays as stored. UnitOfWorkBehavior saves after the handler even when it returns a failure,
// so "sets nothing" is asserted on the tracked aggregate itself.
public class UpdateMatchPreferencesCommandHandlerTests
{
    public enum Part { Occupations, Skills, Locations, EmploymentTypes, Experience }

    private static readonly FakeDateTimeProvider SeedClock = FakeDateTimeProvider.Default;

    // Later than SeedClock, so UpdatedAt tells whether this handler wrote the aggregate.
    private static readonly FakeDateTimeProvider HandlerClock =
        new(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public UpdateMatchPreferencesCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static readonly Dictionary<Part, string[]> OwnedBy = new()
    {
        [Part.Occupations] =
        [
            nameof(MatchPreferences.PreferredOccupationGroups),
            nameof(MatchPreferences.PreferredOccupationExperience),
        ],
        [Part.Skills] = [nameof(MatchPreferences.PreferredSkills)],
        [Part.Locations] =
        [
            nameof(MatchPreferences.PreferredRegions),
            nameof(MatchPreferences.PreferredMunicipalities),
            nameof(MatchPreferences.PreferredRemote),
        ],
        [Part.EmploymentTypes] = [nameof(MatchPreferences.PreferredEmploymentTypes)],
        [Part.Experience] = [nameof(MatchPreferences.ExperienceYears)],
    };

    // What the New* parts below write, as the stored VO holds it. Every value differs from Seeded's.
    private static readonly Dictionary<string, object?[]> Written = new()
    {
        [nameof(MatchPreferences.PreferredOccupationGroups)] = ["grp_c"],
        [nameof(MatchPreferences.PreferredOccupationExperience)] = [new OccupationExperience("grp_c", 2)],
        [nameof(MatchPreferences.PreferredSkills)] = ["sk_z"],
        [nameof(MatchPreferences.PreferredRegions)] = ["reg_z"],
        [nameof(MatchPreferences.PreferredMunicipalities)] = ["kn_z"],
        [nameof(MatchPreferences.PreferredRemote)] = [false],
        [nameof(MatchPreferences.PreferredEmploymentTypes)] = ["et_z"],
        [nameof(MatchPreferences.ExperienceYears)] = [12],
    };

    // All eight fields away from their defaults, built by the domain factory.
    private static MatchPreferences Seeded() =>
        MatchPreferences.Create(
            preferredOccupationGroups: ["grp_a", "grp_b"],
            preferredRegions: ["reg_a"],
            preferredEmploymentTypes: ["et_a"],
            preferredMunicipalities: ["kn_a"],
            preferredSkills: ["sk_a", "sk_b"],
            experienceYears: 7,
            preferredOccupationExperience:
            [
                new OccupationExperience("grp_a", 4),
                new OccupationExperience("grp_b", 9),
            ],
            preferredRemote: true).Value;

    private static OccupationsPartInput NewOccupations() =>
        new(PreferredOccupationGroups: ["grp_c"],
            PreferredOccupationExperience: [new OccupationExperienceInput("grp_c", 2)]);

    private static SkillsPartInput NewSkills() => new(PreferredSkills: ["sk_z"]);

    private static LocationsPartInput NewLocations() =>
        new(PreferredRegions: ["reg_z"], PreferredMunicipalities: ["kn_z"], PreferredRemote: false);

    private static EmploymentTypesPartInput NewEmploymentTypes() => new(PreferredEmploymentTypes: ["et_z"]);

    private static ExperiencePartInput NewExperience() => new(ExperienceYears: 12);

    private static UpdateMatchPreferencesCommand OnlyPart(Part part) => part switch
    {
        Part.Occupations => new UpdateMatchPreferencesCommand(Occupations: NewOccupations()),
        Part.Skills => new UpdateMatchPreferencesCommand(Skills: NewSkills()),
        Part.Locations => new UpdateMatchPreferencesCommand(Locations: NewLocations()),
        Part.EmploymentTypes => new UpdateMatchPreferencesCommand(EmploymentTypes: NewEmploymentTypes()),
        Part.Experience => new UpdateMatchPreferencesCommand(Experience: NewExperience()),
        _ => throw new ArgumentOutOfRangeException(nameof(part), part, null),
    };

    // A seeker whose match preferences were written the way production writes them.
    private async Task<AppDbContext> SeedAsync(Guid? userId = null, MatchPreferences? preferences = null)
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(
            userId ?? _userId, TermsAcceptance.AcceptCurrent(SeedClock), SeedClock).Value;
        seeker.UpdateMatchPreferences(preferences ?? Seeded(), SeedClock);
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);
        return db;
    }

    private UpdateMatchPreferencesCommandHandler Handler(AppDbContext db) =>
        new(db, _currentUser, HandlerClock);

    private JobSeeker Stored(AppDbContext db) => db.JobSeekers.Single(js => js.UserId == _userId);

    private static object?[] ValuesOf(MatchPreferences prefs, PropertyInfo dimension) =>
        dimension.GetValue(prefs) is IEnumerable sequence and not string
            ? [.. sequence.Cast<object?>()]
            : [dimension.GetValue(prefs)];

    [Theory]
    [InlineData(Part.Occupations)]
    [InlineData(Part.Skills)]
    [InlineData(Part.Locations)]
    [InlineData(Part.EmploymentTypes)]
    [InlineData(Part.Experience)]
    public async Task Handle_WithOnePart_ReplacesThatPart_AndLeavesTheOtherFieldsAsSeeded(Part part)
    {
        var db = await SeedAsync();

        var result = await Handler(db).Handle(OnlyPart(part), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var stored = Stored(db);
        var seeded = Seeded();
        var dimensions = typeof(MatchPreferences).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        dimensions.ShouldNotBeEmpty("the check measures nothing if MatchPreferences exposes no dimensions");
        foreach (var dimension in dimensions)
        {
            var expected = OwnedBy[part].Contains(dimension.Name)
                ? Written[dimension.Name]
                : ValuesOf(seeded, dimension);
            ValuesOf(stored.MatchPreferences, dimension).ShouldBe(
                expected,
                ignoreOrder: false,
                customMessage: $"a {part}-only command and {dimension.Name}");
        }

        stored.UpdatedAt.ShouldBe(HandlerClock.UtcNow);
    }

    // The setup rail's four parts, with no experience part, must keep the stated years.
    [Fact]
    public async Task Handle_WithTheRailsFourParts_KeepsTheStoredExperienceYears()
    {
        var db = await SeedAsync();
        var command = new UpdateMatchPreferencesCommand(
            Occupations: NewOccupations(),
            Skills: NewSkills(),
            Locations: NewLocations(),
            EmploymentTypes: NewEmploymentTypes());

        var result = await Handler(db).Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var stored = Stored(db).MatchPreferences;
        stored.ExperienceYears.ShouldBe(7);
        stored.PreferredOccupationGroups.ShouldBe(["grp_c"]);
        stored.PreferredSkills.ShouldBe(["sk_z"]);
        stored.PreferredRegions.ShouldBe(["reg_z"]);
        stored.PreferredEmploymentTypes.ShouldBe(["et_z"]);
    }

    [Fact]
    public async Task Handle_WithEveryPart_ReplacesAllEightFields()
    {
        var db = await SeedAsync();
        var command = new UpdateMatchPreferencesCommand(
            Occupations: NewOccupations(),
            Skills: NewSkills(),
            Locations: NewLocations(),
            EmploymentTypes: NewEmploymentTypes(),
            Experience: NewExperience());

        var result = await Handler(db).Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        Stored(db).MatchPreferences.ShouldBe(MatchPreferences.Create(
            preferredOccupationGroups: ["grp_c"],
            preferredRegions: ["reg_z"],
            preferredEmploymentTypes: ["et_z"],
            preferredMunicipalities: ["kn_z"],
            preferredSkills: ["sk_z"],
            experienceYears: 12,
            preferredOccupationExperience: [new OccupationExperience("grp_c", 2)],
            preferredRemote: false).Value);
    }

    // The failures that reach the handler are the overlay's cross-entry rules: the validator checks
    // each entry's format and range and leaves the subset and distinct rules to MatchPreferences.Create.
    // Should the handler ever apply another part before Occupations, one of these rows has that part
    // applied before the failure.
    [Theory]
    [InlineData(Part.Skills)]
    [InlineData(Part.Locations)]
    [InlineData(Part.EmploymentTypes)]
    [InlineData(Part.Experience)]
    public async Task Handle_WithOrphanYearsBesideAValidPart_ReturnsTheFailure_AndSetsNothing(Part valid)
    {
        var db = await SeedAsync();
        var orphanYears = new OccupationsPartInput(
            PreferredOccupationGroups: ["grp_c"],
            PreferredOccupationExperience: [new OccupationExperienceInput("grp_not_chosen", 3)]);
        var command = OnlyPart(valid) with { Occupations = orphanYears };

        var result = await Handler(db).Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MatchPreferences.OrphanOccupationExperience");
        var stored = Stored(db);
        stored.MatchPreferences.ShouldBe(Seeded());
        stored.UpdatedAt.ShouldBe(SeedClock.UtcNow);
    }

    [Fact]
    public async Task Handle_WithDuplicateYearsBesideEveryOtherPart_ReturnsTheFailure_AndSetsNothing()
    {
        var db = await SeedAsync();
        var command = new UpdateMatchPreferencesCommand(
            Occupations: new OccupationsPartInput(
                PreferredOccupationGroups: ["grp_c"],
                PreferredOccupationExperience:
                [
                    new OccupationExperienceInput("grp_c", 2),
                    new OccupationExperienceInput("grp_c", 5),
                ]),
            Skills: NewSkills(),
            Locations: NewLocations(),
            EmploymentTypes: NewEmploymentTypes(),
            Experience: NewExperience());

        var result = await Handler(db).Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MatchPreferences.DuplicateOccupationExperience");
        var stored = Stored(db);
        stored.MatchPreferences.ShouldBe(Seeded());
        stored.UpdatedAt.ShouldBe(SeedClock.UtcNow);
    }

    // Without a years list, the stored years stay for the groups still chosen and go for the ones removed.
    [Fact]
    public async Task Handle_WithOccupationsWithoutYears_KeepsTheYearsOfTheGroupsStillChosen()
    {
        var db = await SeedAsync();
        var command = new UpdateMatchPreferencesCommand(
            Occupations: new OccupationsPartInput(PreferredOccupationGroups: ["grp_a", "grp_c"]));

        var result = await Handler(db).Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var stored = Stored(db).MatchPreferences;
        stored.PreferredOccupationGroups.ShouldBe(["grp_a", "grp_c"]);
        stored.PreferredOccupationExperience.ShouldBe([new OccupationExperience("grp_a", 4)]);
    }

    // The tri-state lives in the handler: an absent years list keeps, an EMPTY one clears.
    [Fact]
    public async Task Handle_WithOccupationsAndAnEmptyYearsList_ClearsTheYears()
    {
        var db = await SeedAsync();
        var command = new UpdateMatchPreferencesCommand(
            Occupations: new OccupationsPartInput(
                PreferredOccupationGroups: ["grp_a", "grp_b"], PreferredOccupationExperience: []));

        var result = await Handler(db).Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var stored = Stored(db).MatchPreferences;
        stored.PreferredOccupationGroups.ShouldBe(["grp_a", "grp_b"]);
        stored.PreferredOccupationExperience.ShouldBeEmpty();
    }

    // A present years list replaces: grp_a stays chosen, and its stored 4 is not merged back.
    [Fact]
    public async Task Handle_WithOccupationsAndYears_ReplacesTheYears_EvenForAGroupThatStays()
    {
        var db = await SeedAsync();
        var command = new UpdateMatchPreferencesCommand(
            Occupations: new OccupationsPartInput(
                PreferredOccupationGroups: ["grp_a", "grp_c"],
                PreferredOccupationExperience: [new OccupationExperienceInput("grp_c", 2)]));

        var result = await Handler(db).Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        Stored(db).MatchPreferences.PreferredOccupationExperience
            .ShouldBe([new OccupationExperience("grp_c", 2)]);
    }

    // The theory turns remote off over a stored on; this is the other direction, so a PreferredRemote
    // the handler does not pass through fails one of the two.
    [Fact]
    public async Task Handle_WithLocationsTurningRemoteOn_StoresIt()
    {
        var db = await SeedAsync(preferences: MatchPreferences.Empty);
        var command = new UpdateMatchPreferencesCommand(
            Locations: new LocationsPartInput(
                PreferredRegions: ["reg_a"], PreferredMunicipalities: [], PreferredRemote: true));

        var result = await Handler(db).Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        Stored(db).MatchPreferences.PreferredRemote.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_WithExperienceYearsNull_ClearsTheYears()
    {
        var db = await SeedAsync();
        var command = new UpdateMatchPreferencesCommand(Experience: new ExperiencePartInput(ExperienceYears: null));

        var result = await Handler(db).Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        Stored(db).MatchPreferences.ExperienceYears.ShouldBeNull();
    }

    // The seeker vanishes from under an authenticated request when the account's soft delete
    // (JobSeeker.SoftDelete, committed by DeleteAccountCommand) lands first: the query filter hides the row.
    [Fact]
    public async Task Handle_WhenTheSeekerIsSoftDeleted_ReturnsNotFound_AndLeavesTheRowAlone()
    {
        var db = await SeedAsync();
        Stored(db).SoftDelete(SeedClock);
        await db.SaveChangesAsync(CancellationToken.None);

        var result = await Handler(db).Handle(OnlyPart(Part.Skills), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.NotFound");
        var deleted = db.JobSeekers.IgnoreQueryFilters().Single(js => js.UserId == _userId);
        deleted.MatchPreferences.ShouldBe(Seeded());
    }

    [Fact]
    public async Task Handle_IsOwnerScoped_DoesNotTouchAnotherUsersJobSeeker()
    {
        var otherUserId = Guid.NewGuid();
        var db = await SeedAsync(otherUserId);
        var own = JobSeeker.Register(_userId, TermsAcceptance.AcceptCurrent(SeedClock), SeedClock).Value;
        db.JobSeekers.Add(own);
        await db.SaveChangesAsync(CancellationToken.None);

        var result = await Handler(db).Handle(OnlyPart(Part.Skills), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        Stored(db).MatchPreferences.PreferredSkills.ShouldBe(["sk_z"]);
        var other = db.JobSeekers.Single(js => js.UserId == otherUserId);
        other.MatchPreferences.ShouldBe(Seeded());
        other.UpdatedAt.ShouldBe(SeedClock.UtcNow);
    }
}

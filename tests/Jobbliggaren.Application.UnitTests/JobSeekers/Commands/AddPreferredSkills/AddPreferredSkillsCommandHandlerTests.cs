using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobSeekers.Commands.AddPreferredSkills;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.SavedSearches;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobSeekers.Commands.AddPreferredSkills;

// #2247 — the job card's one-click add. A union over the stored confirmed set, never a replace, and
// every other preference stays as stored. UnitOfWorkBehavior saves after the handler even on a failure,
// so "writes nothing" is asserted on the tracked aggregate itself.
public class AddPreferredSkillsCommandHandlerTests
{
    private static readonly FakeDateTimeProvider SeedClock = FakeDateTimeProvider.Default;

    // Later than SeedClock, so UpdatedAt tells whether this handler wrote the aggregate.
    private static readonly FakeDateTimeProvider HandlerClock =
        new(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public AddPreferredSkillsCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static MatchPreferences Seeded(IEnumerable<string>? skills = null) =>
        MatchPreferences.Create(
            preferredOccupationGroups: ["grp_a"],
            preferredRegions: ["reg_a"],
            preferredEmploymentTypes: ["et_a"],
            preferredMunicipalities: ["kn_a"],
            preferredSkills: skills ?? ["sk_a", "sk_b"],
            experienceYears: 7,
            preferredRemote: true).Value;

    // A seeker whose match preferences were written the way production writes them.
    private async Task<AppDbContext> SeedAsync(MatchPreferences? preferences = null)
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, TermsAcceptance.AcceptCurrent(SeedClock), SeedClock).Value;
        seeker.UpdateMatchPreferences(preferences ?? Seeded(), SeedClock);
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);
        return db;
    }

    private AddPreferredSkillsCommandHandler Handler(AppDbContext db) => new(db, _currentUser, HandlerClock);

    private JobSeeker Stored(AppDbContext db) => db.JobSeekers.Single(js => js.UserId == _userId);

    [Fact]
    public async Task Handle_NewConceptGroup_AppendsEveryMemberAndKeepsTheRestOfThePreferences()
    {
        var db = await SeedAsync();

        var result = await Handler(db).Handle(
            new AddPreferredSkillsCommand(["sk_c", "sk_c_twin"]), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var stored = Stored(db);
        stored.MatchPreferences.ShouldBe(Seeded(["sk_a", "sk_b", "sk_c", "sk_c_twin"]));
        stored.UpdatedAt.ShouldBe(HandlerClock.UtcNow);
    }

    [Fact]
    public async Task Handle_GroupPartlyStored_AppendsOnlyTheMissingMembersOnce()
    {
        var db = await SeedAsync();

        var result = await Handler(db).Handle(
            new AddPreferredSkillsCommand(["sk_b", "sk_d", "sk_d"]), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        Stored(db).MatchPreferences.PreferredSkills.ShouldBe(["sk_a", "sk_b", "sk_d"]);
    }

    [Fact]
    public async Task Handle_EveryIdAlreadyStored_SucceedsAndWritesNothing()
    {
        var db = await SeedAsync();

        var result = await Handler(db).Handle(new AddPreferredSkillsCommand(["sk_a"]), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var stored = Stored(db);
        stored.MatchPreferences.ShouldBe(Seeded());
        stored.UpdatedAt.ShouldBe(SeedClock.UtcNow);
    }

    [Fact]
    public async Task Handle_TwoAddsInARow_BothLand()
    {
        var db = await SeedAsync();

        (await Handler(db).Handle(new AddPreferredSkillsCommand(["sk_c"]), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await Handler(db).Handle(new AddPreferredSkillsCommand(["sk_d"]), CancellationToken.None)).IsSuccess.ShouldBeTrue();

        // A read-then-replace from the card would have kept only the second.
        Stored(db).MatchPreferences.PreferredSkills.ShouldBe(["sk_a", "sk_b", "sk_c", "sk_d"]);
    }

    [Fact]
    public async Task Handle_SetAlreadyAtTheDomainBound_FailsWithTheDomainCodeAndWritesNothing()
    {
        var full = Enumerable.Range(0, SearchCriteria.MaxConceptIds).Select(i => $"sk_{i}").ToList();
        var db = await SeedAsync(Seeded(full));

        var result = await Handler(db).Handle(new AddPreferredSkillsCommand(["sk_new"]), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MatchPreferences.TooManySkills");
        var stored = Stored(db);
        stored.MatchPreferences.PreferredSkills.Count.ShouldBe(SearchCriteria.MaxConceptIds);
        stored.UpdatedAt.ShouldBe(SeedClock.UtcNow);
    }

    [Fact]
    public async Task Handle_NoJobSeeker_ReturnsNotFound()
    {
        var db = TestAppDbContextFactory.Create();

        var result = await Handler(db).Handle(new AddPreferredSkillsCommand(["sk_c"]), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(Domain.Common.ErrorKind.NotFound);
    }

    [Fact]
    public async Task Handle_Unauthenticated_FailsWithoutReadingAnySeeker()
    {
        var db = await SeedAsync();
        _currentUser.UserId.Returns((Guid?)null);

        var result = await Handler(db).Handle(new AddPreferredSkillsCommand(["sk_c"]), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.Unauthorized");
        Stored(db).MatchPreferences.ShouldBe(Seeded());
    }
}

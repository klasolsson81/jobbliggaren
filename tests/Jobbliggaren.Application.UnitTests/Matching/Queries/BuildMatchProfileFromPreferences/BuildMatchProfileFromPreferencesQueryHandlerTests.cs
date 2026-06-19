using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Matching.Queries.BuildMatchProfileFromPreferences;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Matching.Queries.BuildMatchProfileFromPreferences;

// F4-12 (CTO-frozen) — preference→CandidateMatchProfile pure mapper-handler.
// INGEN CV-läsning, INGEN DEK (ej IRequiresFieldEncryptionKey), ingen PII.
// Mappning: SsykGroupConceptIds ← PreferredOccupationGroups;
// PreferredRegionConceptIds ← PreferredRegions; PreferredEmploymentTypeConceptIds
// ← PreferredEmploymentTypes; Title ← "" (tom sträng — F4-5-profilen kräver
// fältet men preferens-vägen bär ingen titel).
//
// RÖD tills MatchPreferences + BuildMatchProfileFromPreferencesQuery/Handler finns.
//
// ANTAGANDE (att verifiera): query:t returnerar CandidateMatchProfile (icke-null
// värdetyp), och "ingen JobSeeker / inga prefs / unauthenticated" → HONEST TOM
// profil (Title "", tomma listor), INTE fel/null — speglar att F4-5-profilen
// med tom SSYK-lista rapporterar NotAssessed, inte NoMatch. Read-side-grind
// (Title alltid "") gör mappern deterministisk. Om impl väljer null/Result vid
// unauthenticated faller dessa och konventionen behöver bekräftas.
public class BuildMatchProfileFromPreferencesQueryHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public BuildMatchProfileFromPreferencesQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static async Task<JobSeeker> SeedSeekerWithPrefsAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db,
        Guid userId,
        MatchPreferences prefs)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        seeker.UpdateMatchPreferences(prefs, FakeDateTimeProvider.Default);
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);
        return seeker;
    }

    [Fact]
    public async Task Handle_WithStoredPreferences_MapsAllFieldsToProfile()
    {
        var db = TestAppDbContextFactory.Create();
        var prefs = MatchPreferences.Create(
            preferredOccupationGroups: ["grp_12345", "grp_67890"],
            preferredRegions: ["stockholm_AB"],
            preferredEmploymentTypes: ["et_fast"]).Value;
        await SeedSeekerWithPrefsAsync(db, _userId, prefs);
        var handler = new BuildMatchProfileFromPreferencesQueryHandler(db, _currentUser);

        var profile = await handler.Handle(
            new BuildMatchProfileFromPreferencesQuery(), CancellationToken.None);

        profile.ShouldNotBeNull();
        profile.SsykGroupConceptIds.ShouldBe(["grp_12345", "grp_67890"]);
        profile.PreferredRegionConceptIds.ShouldBe(["stockholm_AB"]);
        profile.PreferredEmploymentTypeConceptIds.ShouldBe(["et_fast"]);
    }

    [Fact]
    public async Task Handle_TitleIsAlwaysEmptyString_FromPreferenceSide()
    {
        // Preferens-vägen bär ingen titel → Title måste vara "" (inte null) så
        // F4-5-title-dimensionen rapporterar honest "ingen titel-signal".
        var db = TestAppDbContextFactory.Create();
        var prefs = MatchPreferences.Create(
            preferredOccupationGroups: ["grp_12345"],
            preferredRegions: null,
            preferredEmploymentTypes: null).Value;
        await SeedSeekerWithPrefsAsync(db, _userId, prefs);
        var handler = new BuildMatchProfileFromPreferencesQueryHandler(db, _currentUser);

        var profile = await handler.Handle(
            new BuildMatchProfileFromPreferencesQuery(), CancellationToken.None);

        profile.ShouldNotBeNull();
        profile.Title.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task Handle_WhenJobSeekerHasEmptyPreferences_ReturnsHonestEmptyProfile()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerWithPrefsAsync(db, _userId, MatchPreferences.Empty);
        var handler = new BuildMatchProfileFromPreferencesQueryHandler(db, _currentUser);

        var profile = await handler.Handle(
            new BuildMatchProfileFromPreferencesQuery(), CancellationToken.None);

        profile.ShouldNotBeNull();
        profile.Title.ShouldBe(string.Empty);
        profile.SsykGroupConceptIds.ShouldBeEmpty();
        profile.PreferredRegionConceptIds.ShouldBeEmpty();
        profile.PreferredEmploymentTypeConceptIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WhenNoJobSeekerForUser_ReturnsHonestEmptyProfile()
    {
        // Ingen JobSeeker → honest TOM profil (inte fel/null) — F4-5-paritet:
        // tom SSYK-lista → NotAssessed, aldrig NoMatch.
        var db = TestAppDbContextFactory.Create();
        var handler = new BuildMatchProfileFromPreferencesQueryHandler(db, _currentUser);

        var profile = await handler.Handle(
            new BuildMatchProfileFromPreferencesQuery(), CancellationToken.None);

        profile.ShouldNotBeNull();
        profile.Title.ShouldBe(string.Empty);
        profile.SsykGroupConceptIds.ShouldBeEmpty();
        profile.PreferredRegionConceptIds.ShouldBeEmpty();
        profile.PreferredEmploymentTypeConceptIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WhenNotAuthenticated_ReturnsHonestEmptyProfile()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var handler = new BuildMatchProfileFromPreferencesQueryHandler(db, currentUser);

        var profile = await handler.Handle(
            new BuildMatchProfileFromPreferencesQuery(), CancellationToken.None);

        profile.ShouldNotBeNull();
        profile.SsykGroupConceptIds.ShouldBeEmpty();
        profile.PreferredRegionConceptIds.ShouldBeEmpty();
        profile.PreferredEmploymentTypeConceptIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_IsOwnerScoped_OnlyMapsCurrentUsersPreferences()
    {
        var db = TestAppDbContextFactory.Create();
        // Annan användare med preferenser som INTE får läcka in i profilen.
        await SeedSeekerWithPrefsAsync(
            db, Guid.NewGuid(),
            MatchPreferences.Create(
                preferredOccupationGroups: ["grp_OTHER"],
                preferredRegions: null,
                preferredEmploymentTypes: null).Value);
        // Aktuell användare med sina egna preferenser.
        await SeedSeekerWithPrefsAsync(
            db, _userId,
            MatchPreferences.Create(
                preferredOccupationGroups: ["grp_MINE"],
                preferredRegions: null,
                preferredEmploymentTypes: null).Value);
        var handler = new BuildMatchProfileFromPreferencesQueryHandler(db, _currentUser);

        var profile = await handler.Handle(
            new BuildMatchProfileFromPreferencesQuery(), CancellationToken.None);

        profile.ShouldNotBeNull();
        profile.SsykGroupConceptIds.ShouldBe(["grp_MINE"]);
        profile.SsykGroupConceptIds.ShouldNotContain("grp_OTHER");
    }
}

using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Application.Matching.Profiles;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Matching.Profiles;

// F4-12/F4-13 (senior-cto-advisor 2026-06-19 Decision B = B2) — the SSOT
// preference→CandidateMatchProfile mapper. These cases were relocated verbatim from
// BuildMatchProfileFromPreferencesQueryHandlerTests when the handler was thinned to a
// delegation: the mapping logic (DB-load + map + honest-empty fallback) now lives on
// MatchProfileBuilder, so the behavioural contract is pinned here (the handler keeps a
// thin delegation test of its own).
//
// INGEN CV-läsning, INGEN DEK (ej IRequiresFieldEncryptionKey), ingen PII.
// Mappning: SsykGroupConceptIds ← PreferredOccupationGroups;
// PreferredRegionConceptIds ← PreferredRegions; PreferredEmploymentTypeConceptIds
// ← PreferredEmploymentTypes; Title ← "" (tom sträng). "Ingen JobSeeker / inga prefs /
// unauthenticated" → HONEST TOM profil (Title "", tomma listor), INTE fel/null.
public class MatchProfileBuilderTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    // F4-15 (ADR 0076 Decision 6) widened the ctor with three collaborators (skill
    // resolver + DEK warm-up). They are irrelevant to the UNCHANGED preference path
    // (BuildFromPreferencesAsync reads no CV / no DEK) — stubbed here so these F4-12 cases
    // keep pinning the preference mapping. The Full-path behaviour lives in
    // MatchProfileBuilderFullTests.
    private readonly ISkillResolver _skillResolver = Substitute.For<ISkillResolver>();
    private readonly ICurrentDataOwner _dataOwner = Substitute.For<ICurrentDataOwner>();
    private readonly IUserDataKeyStore _dataKeyStore = Substitute.For<IUserDataKeyStore>();

    public MatchProfileBuilderTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private MatchProfileBuilder NewBuilder(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, ICurrentUser? user = null) =>
        new(db, user ?? _currentUser, _skillResolver, _dataOwner, _dataKeyStore);

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
    public async Task BuildFromPreferences_WithStoredPreferences_MapsAllFieldsToProfile()
    {
        var db = TestAppDbContextFactory.Create();
        var prefs = MatchPreferences.Create(
            preferredOccupationGroups: ["grp_12345", "grp_67890"],
            preferredRegions: ["stockholm_AB"],
            preferredEmploymentTypes: ["et_fast"],
            preferredMunicipalities: ["sthlm_kn"]).Value;
        await SeedSeekerWithPrefsAsync(db, _userId, prefs);
        var builder = NewBuilder(db);

        var profile = await builder.BuildFromPreferencesAsync(CancellationToken.None);

        profile.ShouldNotBeNull();
        profile.SsykGroupConceptIds.ShouldBe(["grp_12345", "grp_67890"]);
        profile.PreferredRegionConceptIds.ShouldBe(["stockholm_AB"]);
        profile.PreferredEmploymentTypeConceptIds.ShouldBe(["et_fast"]);
        // Spår 3 PR-A — municipality-dimensionen bärs igenom till profilen.
        profile.PreferredMunicipalityConceptIds.ShouldBe(["sthlm_kn"]);
    }

    // Spår 3 PR-A — fokuserat mappnings-fall: lagrade municipalities → profilens
    // PreferredMunicipalityConceptIds (parity med region/employment-type-mappningen).
    [Fact]
    public async Task BuildFromPreferences_WithStoredMunicipalities_MapsToProfileMunicipalityConceptIds()
    {
        var db = TestAppDbContextFactory.Create();
        var prefs = MatchPreferences.Create(
            preferredOccupationGroups: null,
            preferredRegions: null,
            preferredEmploymentTypes: null,
            preferredMunicipalities: ["sthlm_kn", "gbg_kn"]).Value;
        await SeedSeekerWithPrefsAsync(db, _userId, prefs);
        var builder = NewBuilder(db);

        var profile = await builder.BuildFromPreferencesAsync(CancellationToken.None);

        profile.ShouldNotBeNull();
        profile.PreferredMunicipalityConceptIds.ShouldBe(["gbg_kn", "sthlm_kn"]); // sorterad ordinal
        profile.SsykGroupConceptIds.ShouldBeEmpty();
        profile.PreferredRegionConceptIds.ShouldBeEmpty();
        profile.PreferredEmploymentTypeConceptIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task BuildFromPreferences_TitleIsAlwaysEmptyString_FromPreferenceSide()
    {
        // Preferens-vägen bär ingen titel → Title måste vara "" (inte null) så
        // F4-5-title-dimensionen rapporterar honest "ingen titel-signal".
        var db = TestAppDbContextFactory.Create();
        var prefs = MatchPreferences.Create(
            preferredOccupationGroups: ["grp_12345"],
            preferredRegions: null,
            preferredEmploymentTypes: null).Value;
        await SeedSeekerWithPrefsAsync(db, _userId, prefs);
        var builder = NewBuilder(db);

        var profile = await builder.BuildFromPreferencesAsync(CancellationToken.None);

        profile.ShouldNotBeNull();
        profile.Title.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task BuildFromPreferences_WhenJobSeekerHasEmptyPreferences_ReturnsHonestEmptyProfile()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerWithPrefsAsync(db, _userId, MatchPreferences.Empty);
        var builder = NewBuilder(db);

        var profile = await builder.BuildFromPreferencesAsync(CancellationToken.None);

        profile.ShouldNotBeNull();
        profile.Title.ShouldBe(string.Empty);
        profile.SsykGroupConceptIds.ShouldBeEmpty();
        profile.PreferredRegionConceptIds.ShouldBeEmpty();
        profile.PreferredEmploymentTypeConceptIds.ShouldBeEmpty();
        profile.PreferredMunicipalityConceptIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task BuildFromPreferences_WhenNoJobSeekerForUser_ReturnsHonestEmptyProfile()
    {
        // Ingen JobSeeker → honest TOM profil (inte fel/null) — F4-5-paritet:
        // tom SSYK-lista → NotAssessed, aldrig NoMatch.
        var db = TestAppDbContextFactory.Create();
        var builder = NewBuilder(db);

        var profile = await builder.BuildFromPreferencesAsync(CancellationToken.None);

        profile.ShouldNotBeNull();
        profile.Title.ShouldBe(string.Empty);
        profile.SsykGroupConceptIds.ShouldBeEmpty();
        profile.PreferredRegionConceptIds.ShouldBeEmpty();
        profile.PreferredEmploymentTypeConceptIds.ShouldBeEmpty();
        profile.PreferredMunicipalityConceptIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task BuildFromPreferences_WhenNotAuthenticated_ReturnsHonestEmptyProfile()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var builder = NewBuilder(db, currentUser);

        var profile = await builder.BuildFromPreferencesAsync(CancellationToken.None);

        profile.ShouldNotBeNull();
        profile.SsykGroupConceptIds.ShouldBeEmpty();
        profile.PreferredRegionConceptIds.ShouldBeEmpty();
        profile.PreferredEmploymentTypeConceptIds.ShouldBeEmpty();
        profile.PreferredMunicipalityConceptIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task BuildFromPreferences_IsOwnerScoped_OnlyMapsCurrentUsersPreferences()
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
        var builder = NewBuilder(db);

        var profile = await builder.BuildFromPreferencesAsync(CancellationToken.None);

        profile.ShouldNotBeNull();
        profile.SsykGroupConceptIds.ShouldBe(["grp_MINE"]);
        profile.SsykGroupConceptIds.ShouldNotContain("grp_OTHER");
    }
}

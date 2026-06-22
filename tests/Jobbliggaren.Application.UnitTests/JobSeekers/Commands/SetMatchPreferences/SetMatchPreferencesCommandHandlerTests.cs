using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobSeekers.Commands.SetMatchPreferences;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobSeekers.Commands.SetMatchPreferences;

// F4-12 (CTO-frozen) — write-path-handler speglar CreateSavedSearch:
// current-user-grind (UserId.HasValue), owner-scope-uppslag av JobSeeker via
// UserId, MatchPreferences.Create, jobSeeker.UpdateMatchPreferences. SKILLNAD
// mot CreateSavedSearch: TRACKED load (muterar ETT befintligt aggregat, inte
// Add av nytt). Command returnerar Result (icke-generisk — den skapar inget id).
//
// RÖD tills SetMatchPreferencesCommand + handler + MatchPreferences-typen finns.
//
// ANTAGANDE (att verifiera): no-JobSeeker → Result.Failure(DomainError.NotFound)
// med kod "JobSeeker.NotFound" (Result-väg per CreateSavedSearch-mirror, INTE
// NotFoundException-throw som UpdateMyProfile). Unauthenticated → Result.Failure
// med kod "JobSeeker.Unauthorized" (parity CreateSavedSearch "SavedSearch.
// Unauthorized"; prefix antas följa aggregatet). Om impl väljer andra koder/väg
// faller dessa och kontraktet behöver bekräftas.
public class SetMatchPreferencesCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public SetMatchPreferencesCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static async Task<JobSeeker> SeedSeekerAsync(AppDbContext db, Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);
        return seeker;
    }

    // ValidCommand bär medvetet INGA municipalities → bevisar att det additiva
    // 4:e-param-kontraktet (optional, default null) inte bryter befintliga 3-arg-
    // liknande anrop (Spår 3 PR-A).
    private static SetMatchPreferencesCommand ValidCommand() =>
        new(
            PreferredOccupationGroups: ["grp_12345"],
            PreferredRegions: ["stockholm_AB"],
            PreferredEmploymentTypes: ["et_fast"]);

    [Fact]
    public async Task Handle_WithValidCommand_SetsPreferencesOnJobSeeker()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db, _userId);
        var handler = new SetMatchPreferencesCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        // TRACKED load → muterar befintligt aggregat (SaveChanges görs av
        // UnitOfWork-behavior, inte handlern). Verifierar via spårad entitet.
        var seeker = db.JobSeekers.Single(js => js.UserId == _userId);
        seeker.MatchPreferences.PreferredOccupationGroups.ShouldBe(["grp_12345"]);
        seeker.MatchPreferences.PreferredRegions.ShouldBe(["stockholm_AB"]);
        seeker.MatchPreferences.PreferredEmploymentTypes.ShouldBe(["et_fast"]);
        // Additivt: command utan municipalities → tom municipality-dimension.
        seeker.MatchPreferences.PreferredMunicipalities.ShouldBeEmpty();
    }

    // Spår 3 PR-A — municipalities trådas igenom till den lagrade VO:t.
    [Fact]
    public async Task Handle_WithMunicipalities_ThreadsThemIntoStoredPreferences()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db, _userId);
        var handler = new SetMatchPreferencesCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);

        var command = new SetMatchPreferencesCommand(
            PreferredOccupationGroups: ["grp_12345"],
            PreferredRegions: null,
            PreferredEmploymentTypes: null,
            PreferredMunicipalities: ["sthlm_kn", "gbg_kn"]);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var seeker = db.JobSeekers.Single(js => js.UserId == _userId);
        seeker.MatchPreferences.PreferredMunicipalities.ShouldBe(["gbg_kn", "sthlm_kn"]); // sorterad ordinal
        seeker.MatchPreferences.PreferredOccupationGroups.ShouldBe(["grp_12345"]);
    }

    // Spår 3 PR-A — ogiltigt municipality-concept-id bubblar upp som
    // MatchPreferences.Create-DomainError (parity med occupation-group-fallet).
    [Fact]
    public async Task Handle_WithInvalidMunicipalityConceptId_ReturnsDomainValidationError()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db, _userId);
        var handler = new SetMatchPreferencesCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);

        var command = new SetMatchPreferencesCommand(
            PreferredOccupationGroups: null,
            PreferredRegions: null,
            PreferredEmploymentTypes: null,
            PreferredMunicipalities: ["bad id with space"]);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MatchPreferences.InvalidMunicipality");
    }

    [Fact]
    public async Task Handle_WithEmptyLists_SetsEmptyPreferences_AndSucceeds()
    {
        // Tom MatchPreferences är giltig (ingen "minst ett"-invariant) → t.ex.
        // nollställning av tidigare angivna preferenser.
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db, _userId);
        var handler = new SetMatchPreferencesCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);

        var command = new SetMatchPreferencesCommand(
            PreferredOccupationGroups: null,
            PreferredRegions: null,
            PreferredEmploymentTypes: null);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var seeker = db.JobSeekers.Single(js => js.UserId == _userId);
        seeker.MatchPreferences.PreferredOccupationGroups.ShouldBeEmpty();
        seeker.MatchPreferences.PreferredMunicipalities.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ReturnsFailure()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var handler = new SetMatchPreferencesCommandHandler(db, currentUser, FakeDateTimeProvider.Default);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.Unauthorized");
    }

    [Fact]
    public async Task Handle_WhenNoJobSeekerForUser_ReturnsNotFound()
    {
        var db = TestAppDbContextFactory.Create();
        var handler = new SetMatchPreferencesCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.NotFound");
    }

    [Fact]
    public async Task Handle_WithInvalidConceptId_ReturnsDomainValidationError()
    {
        // Ogiltigt concept-id bubblar upp som MatchPreferences.Create-DomainError.
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db, _userId);
        var handler = new SetMatchPreferencesCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);

        var command = new SetMatchPreferencesCommand(
            PreferredOccupationGroups: ["bad id with space"],
            PreferredRegions: null,
            PreferredEmploymentTypes: null);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MatchPreferences.InvalidOccupationGroup");
    }

    // STEG 3 (ADR 0079) — confirmed skills + experience are threaded into the stored
    // VO (the trusted capability source persisted for the scorer to read in PR-D).
    [Fact]
    public async Task Handle_WithSkillsAndExperience_ThreadsThemIntoStoredPreferences()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db, _userId);
        var handler = new SetMatchPreferencesCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);

        var command = new SetMatchPreferencesCommand(
            PreferredOccupationGroups: ["grp_12345"],
            PreferredRegions: null,
            PreferredEmploymentTypes: null,
            PreferredMunicipalities: null,
            PreferredSkills: ["skill_spring", "skill_java"],
            ExperienceYears: 5);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var seeker = db.JobSeekers.Single(js => js.UserId == _userId);
        seeker.MatchPreferences.PreferredSkills.ShouldBe(["skill_java", "skill_spring"]); // sorterad ordinal
        seeker.MatchPreferences.ExperienceYears.ShouldBe(5);
        seeker.MatchPreferences.PreferredOccupationGroups.ShouldBe(["grp_12345"]);
    }

    // STEG 3 (ADR 0079) — out-of-range experience bubbles up as a Create DomainError.
    [Fact]
    public async Task Handle_WithOutOfRangeExperienceYears_ReturnsDomainValidationError()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db, _userId);
        var handler = new SetMatchPreferencesCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);

        var command = new SetMatchPreferencesCommand(
            PreferredOccupationGroups: null,
            PreferredRegions: null,
            PreferredEmploymentTypes: null,
            PreferredMunicipalities: null,
            PreferredSkills: null,
            ExperienceYears: 999);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MatchPreferences.ExperienceYearsOutOfRange");
    }

    [Fact]
    public async Task Handle_IsOwnerScoped_DoesNotTouchOtherUsersJobSeeker()
    {
        // Owner-scope: bara den inloggade användarens JobSeeker muteras. En annan
        // användares aggregat ska lämnas orört.
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db, _userId);
        var otherUserId = Guid.NewGuid();
        var otherSeeker = await SeedSeekerAsync(db, otherUserId);
        var handler = new SetMatchPreferencesCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var untouched = db.JobSeekers.Single(js => js.UserId == otherUserId);
        untouched.MatchPreferences.PreferredOccupationGroups.ShouldBeEmpty();
        untouched.Id.ShouldBe(otherSeeker.Id);
    }
}

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

    // ADR 0079-amendment (exp-per-occ PR-3) — the per-occupation experience overlay is mapped
    // from the wire-shape to the Domain VO and threaded into the stored MatchPreferences.
    [Fact]
    public async Task Handle_WithOccupationExperienceOverlay_ThreadsItIntoStoredPreferences()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db, _userId);
        var handler = new SetMatchPreferencesCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);

        var command = new SetMatchPreferencesCommand(
            PreferredOccupationGroups: ["grp_12345", "grp_67890"],
            PreferredRegions: null,
            PreferredEmploymentTypes: null,
            PreferredMunicipalities: null,
            PreferredSkills: null,
            ExperienceYears: null,
            PreferredOccupationExperience:
            [
                new OccupationExperienceInput("grp_12345", 5),
                new OccupationExperienceInput("grp_67890", null), // a preferred group with no stated years
            ]);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var overlay = db.JobSeekers.Single(js => js.UserId == _userId)
            .MatchPreferences.PreferredOccupationExperience;
        overlay.Count.ShouldBe(2);
        overlay.Single(e => e.ConceptId == "grp_12345").Years.ShouldBe(5);
        overlay.Single(e => e.ConceptId == "grp_67890").Years.ShouldBeNull();
    }

    // The overlay is a SPARSE subset of PreferredOccupationGroups — an entry for a group that is
    // NOT preferred is a domain-invariant failure (enforced in MatchPreferences.Create, §2.2).
    [Fact]
    public async Task Handle_OverlayForNonPreferredGroup_ReturnsOrphanDomainError()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db, _userId);
        var handler = new SetMatchPreferencesCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);

        var command = new SetMatchPreferencesCommand(
            PreferredOccupationGroups: ["grp_12345"],
            PreferredRegions: null,
            PreferredEmploymentTypes: null,
            PreferredMunicipalities: null,
            PreferredSkills: null,
            ExperienceYears: null,
            PreferredOccupationExperience: [new OccupationExperienceInput("grp_not_preferred", 3)]);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MatchPreferences.OrphanOccupationExperience");
    }

    // Full-replace page-wipe: a command that omits the overlay clears a previously-stored one.
    [Fact]
    public async Task Handle_OmittingOverlay_ClearsPreviouslyStoredOverlay()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db, _userId);
        var handler = new SetMatchPreferencesCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);

        // First save stores an overlay.
        await handler.Handle(new SetMatchPreferencesCommand(
            PreferredOccupationGroups: ["grp_12345"],
            PreferredRegions: null,
            PreferredEmploymentTypes: null,
            PreferredOccupationExperience: [new OccupationExperienceInput("grp_12345", 4)]),
            CancellationToken.None);

        // Second save omits the overlay (null) → full-replace clears it.
        var result = await handler.Handle(new SetMatchPreferencesCommand(
            PreferredOccupationGroups: ["grp_12345"],
            PreferredRegions: null,
            PreferredEmploymentTypes: null),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        db.JobSeekers.Single(js => js.UserId == _userId)
            .MatchPreferences.PreferredOccupationExperience.ShouldBeEmpty();
    }

    // M1 (architect review) — a malformed body binds a null array element; the handler must drop
    // it (parity with the string-dimension NormalizeList null guard) and degrade to honest-empty,
    // never NRE into a 500 (the eager wire→VO map ran before Create's own null guard).
    [Fact]
    public async Task Handle_OverlayWithNullElement_DropsIt_DoesNotThrow()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db, _userId);
        var handler = new SetMatchPreferencesCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);

        var command = new SetMatchPreferencesCommand(
            PreferredOccupationGroups: ["grp_12345"],
            PreferredRegions: null,
            PreferredEmploymentTypes: null,
            PreferredMunicipalities: null,
            PreferredSkills: null,
            ExperienceYears: null,
            PreferredOccupationExperience: [null!, new OccupationExperienceInput("grp_12345", 5)]);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(); // the null was dropped, the valid entry survived
        var overlay = db.JobSeekers.Single(js => js.UserId == _userId)
            .MatchPreferences.PreferredOccupationExperience;
        overlay.ShouldHaveSingleItem().ConceptId.ShouldBe("grp_12345");
    }

    // test-writer Minor 1 — duplicate concept-id reachable via the command bubbles up as a
    // Create DomainError (the validator deliberately defers the distinct rule to Create).
    [Fact]
    public async Task Handle_OverlayWithDuplicateConceptId_ReturnsDuplicateDomainError()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db, _userId);
        var handler = new SetMatchPreferencesCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);

        var command = new SetMatchPreferencesCommand(
            PreferredOccupationGroups: ["grp_12345"],
            PreferredRegions: null,
            PreferredEmploymentTypes: null,
            PreferredMunicipalities: null,
            PreferredSkills: null,
            ExperienceYears: null,
            PreferredOccupationExperience:
            [
                new OccupationExperienceInput("grp_12345", 5),
                new OccupationExperienceInput("grp_12345", 8), // same group twice
            ]);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("MatchPreferences.DuplicateOccupationExperience");
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

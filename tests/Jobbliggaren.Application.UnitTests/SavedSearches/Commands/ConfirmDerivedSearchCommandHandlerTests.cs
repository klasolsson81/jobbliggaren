using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.SavedSearches.Commands.ConfirmDerivedSearch;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.SavedSearches.Events;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.SavedSearches.Commands;

// Fas 4 STEG B — the CV→SavedSearch confirm→create handler. Mirrors CreateSavedSearch but builds
// via SavedSearch.CreateFromResume (provenance event). Deliberately consumes only the command's
// PLAIN ids — never the deriver result (the structural bearing invariant lives in the arch tests).
public class ConfirmDerivedSearchCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public ConfirmDerivedSearchCommandHandlerTests() => _currentUser.UserId.Returns(_userId);

    private ConfirmDerivedSearchCommandHandler CreateSut(Infrastructure.Persistence.AppDbContext db) =>
        new(db, _currentUser, FakeDateTimeProvider.Default);

    private async Task<JobSeeker> SeedSeekerAsync(Infrastructure.Persistence.AppDbContext db)
    {
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return seeker;
    }

    private static ConfirmDerivedSearchCommand Command(params string[] occupationGroups) =>
        new(
            Name: "CV-sök",
            OccupationGroup: occupationGroups.Length == 0 ? ["grp_12345"] : occupationGroups,
            SourceParsedResumeId: Guid.NewGuid(),
            Municipality: null, Region: null, EmploymentType: null, WorktimeExtent: null, Q: null,
            SortBy: JobAdSortBy.PublishedAtDesc, NotificationEnabled: true);

    [Fact]
    public async Task Handle_CreatesSavedSearch_WithConfirmedOccupations_AndProvenanceEvent()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db);

        var result = await CreateSut(db).Handle(Command("grp_aaa", "grp_bbb"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var saved = db.SavedSearches.Local.ShouldHaveSingleItem();
        saved.Id.Value.ShouldBe(result.Value);
        saved.JobSeekerId.ShouldBe(seeker.Id);
        saved.Criteria.OccupationGroup.ShouldBe(["grp_aaa", "grp_bbb"]);
        saved.DomainEvents.OfType<SavedSearchDerivedFromResumeDomainEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Handle_ForwardsAllCriteriaDimensions_ToSearchCriteria()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db);
        var command = new ConfirmDerivedSearchCommand(
            Name: "CV-sök", OccupationGroup: ["grp_aaa"], SourceParsedResumeId: null,
            Municipality: ["sthlm_kn"], Region: ["stockholm_ln"], EmploymentType: ["heltid"],
            WorktimeExtent: ["full"], Q: "backend",
            SortBy: JobAdSortBy.PublishedAtDesc, NotificationEnabled: false);

        var result = await CreateSut(db).Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var saved = db.SavedSearches.Local.ShouldHaveSingleItem();
        saved.Criteria.Municipality.ShouldBe(["sthlm_kn"]);
        saved.Criteria.Region.ShouldBe(["stockholm_ln"]);
        saved.Criteria.EmploymentType.ShouldBe(["heltid"]);
        saved.Criteria.WorktimeExtent.ShouldBe(["full"]);
        saved.Criteria.Q.ShouldBe("backend");
        saved.NotificationEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_Unauthenticated_ReturnsFailure_NothingAdded()
    {
        var db = TestAppDbContextFactory.Create();
        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);
        var sut = new ConfirmDerivedSearchCommandHandler(db, anon, FakeDateTimeProvider.Default);

        var result = await sut.Handle(Command(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SavedSearch.Unauthorized");
        db.SavedSearches.Local.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_JobSeekerNotFound_ReturnsNotFoundFailure_NothingAdded()
    {
        var db = TestAppDbContextFactory.Create();

        var result = await CreateSut(db).Handle(Command(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.NotFound");
        db.SavedSearches.Local.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_InvalidConceptId_ReturnsCriteriaFailure_NothingAdded()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db);

        // Spaces + '!' fail SearchCriteria's per-element concept-id regex (handler-side, after
        // the validator's NotEmpty check passes).
        var result = await CreateSut(db).Handle(Command("not a valid id!"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SearchCriteria.InvalidOccupationGroup");
        db.SavedSearches.Local.ShouldBeEmpty();
    }
}

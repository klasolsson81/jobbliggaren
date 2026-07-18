using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Dev.Commands.ResetMyData;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Application.UnitTests.Resumes.Review;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.RecentJobSearches;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Domain.SavedJobAds;
using Jobbliggaren.Domain.SavedSearches;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Dev.Commands.ResetMyData;

/// <summary>
/// DEV-ONLY throwaway handler (REMOVE BEFORE LAUNCH). Branch-covering tests:
///   1. Not authenticated                 → Failure "Dev.NotAuthenticated"
///   2. No JobSeeker for user              → tolerant Success (nothing to clear)
///   3. Happy path                         → own CVs (+ versions) / ParsedResumes /
///      SavedJobAds / RecentJobSearches cleared + MatchPreferences reset to Empty
///   4. Owner-scope                        → a DIFFERENT user's data is untouched
/// The owner-scope assertion (4) is the security-critical one: the reset must never
/// reach across users.
/// </summary>
public class ResetMyDataCommandHandlerTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;

    private static ICurrentUser AuthenticatedAs(Guid userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return currentUser;
    }

    private static SearchCriteria SampleCriteria() =>
        SearchCriteria.Create(
            occupationGroup: ["grp_12345"], municipality: null, region: null,
            employmentType: null, worktimeExtent: null, employer: null, remote: false,
            q: null, sortBy: JobAdSortBy.PublishedAtDesc).Value;

    private static MatchPreferences StatedPreferences() =>
        MatchPreferences.Create(["grp_99999"], ["reg_42"], null).Value;

    /// <summary>
    /// Seeds a JobSeeker (with stated match preferences) plus the full set of
    /// onboarding-relevant owned data: one CV (+ its Master version), one parsed-CV
    /// staging artifact, one saved job-ad and one recent search.
    /// </summary>
    private static async Task<JobSeeker> SeedFullUserAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", Clock).Value;
        seeker.UpdateMatchPreferences(StatedPreferences(), Clock);
        db.JobSeekers.Add(seeker);

        var resume = Resume.Create(seeker.Id, "Standard-CV", "Klas Olsson", Clock).Value;
        db.Resumes.Add(resume);

        var parsed = ParsedResume.Create(
            seeker.Id,
            "CV_Klas.pdf",
            "application/pdf",
            ResumeLanguage.Sv,
            new ParsedResumeContent(
                CvReviewFixtures.CompleteContact(),
                "Profil",
                [CvReviewFixtures.Experience()],
                [CvReviewFixtures.Education()],
                ["C#"],
                ["Svenska"]),
            "Klas Olsson",
            CvReviewFixtures.ConfidentConfidence(),
            PersonnummerScanOutcome.None,
            [],
            Clock).Value;
        db.ParsedResumes.Add(parsed);

        var saved = SavedJobAd.Save(seeker.Id, new JobAdId(Guid.NewGuid()), Clock.UtcNow);
        db.SavedJobAds.Add(saved);

        var recent = RecentJobSearch.Capture(seeker.Id, SampleCriteria(), currentCount: 3, Clock.UtcNow);
        db.RecentJobSearches.Add(recent);

        await db.SaveChangesAsync(CancellationToken.None);
        return seeker;
    }

    [Fact]
    public async Task ResetMyDataCommandHandler_WhenNotAuthenticated_ReturnsNotAuthenticatedFailure()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var handler = new ResetMyDataCommandHandler(db, currentUser, Clock);

        var result = await handler.Handle(new ResetMyDataCommand(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Dev.NotAuthenticated");
    }

    [Fact]
    public async Task ResetMyDataCommandHandler_WhenNoJobSeekerForUser_ReturnsTolerantSuccess()
    {
        var db = TestAppDbContextFactory.Create();
        var handler = new ResetMyDataCommandHandler(db, AuthenticatedAs(Guid.NewGuid()), Clock);

        var result = await handler.Handle(new ResetMyDataCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ResetMyDataCommandHandler_WhenAuthenticated_ClearsOwnDataAndResetsMatchPreferences()
    {
        var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var seeker = await SeedFullUserAsync(db, userId);

        var handler = new ResetMyDataCommandHandler(db, AuthenticatedAs(userId), Clock);

        var result = await handler.Handle(new ResetMyDataCommand(), CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();

        // CVs soft-deleted (+ their versions) — vanish from the UI via query filter.
        var reloadedResumes = await db.Resumes
            .IgnoreQueryFilters()
            .Include(r => r.Versions)
            .Where(r => r.JobSeekerId == seeker.Id)
            .ToListAsync(CancellationToken.None);
        reloadedResumes.ShouldAllBe(r => r.DeletedAt != null);
        reloadedResumes.SelectMany(r => r.Versions).ShouldAllBe(v => v.DeletedAt != null);
        reloadedResumes.SelectMany(r => r.Versions).Count().ShouldBe(1);
        // None remain visible through the global soft-delete query filter.
        (await db.Resumes.AnyAsync(r => r.JobSeekerId == seeker.Id, CancellationToken.None))
            .ShouldBeFalse();

        // Parsed-CV staging artifacts discarded (soft-deleted) — gone from the UI.
        var reloadedParsed = await db.ParsedResumes
            .IgnoreQueryFilters()
            .Where(p => p.JobSeekerId == seeker.Id)
            .ToListAsync(CancellationToken.None);
        reloadedParsed.ShouldAllBe(p => p.DeletedAt != null);
        (await db.ParsedResumes.AnyAsync(p => p.JobSeekerId == seeker.Id, CancellationToken.None))
            .ShouldBeFalse();

        // "Sökta annonser" hard-deleted.
        (await db.SavedJobAds.AnyAsync(s => s.JobSeekerId == seeker.Id, CancellationToken.None))
            .ShouldBeFalse();
        (await db.RecentJobSearches.AnyAsync(r => r.JobSeekerId == seeker.Id, CancellationToken.None))
            .ShouldBeFalse();

        // Match preferences reset to Empty → hasStatedDesiredOccupation becomes false.
        var reloadedSeeker = await db.JobSeekers
            .FirstAsync(js => js.UserId == userId, CancellationToken.None);
        reloadedSeeker.MatchPreferences.ShouldBe(MatchPreferences.Empty);

        // The account itself is NOT deleted — login keeps working.
        reloadedSeeker.DeletedAt.ShouldBeNull();
    }

    [Fact]
    public async Task ResetMyDataCommandHandler_WhenAuthenticated_LeavesOtherUsersDataUntouched()
    {
        var db = TestAppDbContextFactory.Create();
        var meUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        var me = await SeedFullUserAsync(db, meUserId);
        var other = await SeedFullUserAsync(db, otherUserId);

        var handler = new ResetMyDataCommandHandler(db, AuthenticatedAs(meUserId), Clock);

        var result = await handler.Handle(new ResetMyDataCommand(), CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();

        // The OTHER user's entire dataset is intact (owner-scope proof).
        (await db.Resumes.AnyAsync(r => r.JobSeekerId == other.Id, CancellationToken.None))
            .ShouldBeTrue();
        (await db.ParsedResumes.AnyAsync(p => p.JobSeekerId == other.Id, CancellationToken.None))
            .ShouldBeTrue();
        (await db.SavedJobAds.AnyAsync(s => s.JobSeekerId == other.Id, CancellationToken.None))
            .ShouldBeTrue();
        (await db.RecentJobSearches.AnyAsync(r => r.JobSeekerId == other.Id, CancellationToken.None))
            .ShouldBeTrue();

        var otherSeeker = await db.JobSeekers
            .FirstAsync(js => js.UserId == otherUserId, CancellationToken.None);
        otherSeeker.MatchPreferences.ShouldBe(StatedPreferences());

        // Sanity: the caller's own data WAS cleared (otherwise the scope test is vacuous).
        (await db.Resumes.AnyAsync(r => r.JobSeekerId == me.Id, CancellationToken.None))
            .ShouldBeFalse();
    }
}

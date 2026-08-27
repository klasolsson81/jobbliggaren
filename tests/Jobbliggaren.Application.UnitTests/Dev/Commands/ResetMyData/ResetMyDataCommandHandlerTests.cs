using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Dev.Commands.ResetMyData;
using Jobbliggaren.Application.Dev.Configuration;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Application.UnitTests.Resumes.Review;
using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Matching;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.RecentJobSearches;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Files;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Domain.SavedJobAds;
using Jobbliggaren.Domain.SavedSearches;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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

    // The tool's own kill-switch, ON for every test that exercises what the tool DOES.
    // Development sets it explicitly in appsettings.Development.json so the map gate and this
    // handler refusal read the same value; the OFF arm is pinned separately below.
    private static readonly IOptions<DevToolsOptions> EnabledDevTools =
        Options.Create(new DevToolsOptions { EnableResetMyData = true });

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
    /// onboarding-relevant owned data: one CV (+ its Master version, and set as PRIMARY),
    /// one parsed-CV staging artifact, its captured original file, one saved job-ad, one
    /// recent search and one graded match.
    /// </summary>
    private static async Task<JobSeeker> SeedFullUserAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", Clock).Value;
        seeker.UpdateMatchPreferences(StatedPreferences(), Clock);
        db.JobSeekers.Add(seeker);

        var resume = Resume.Create(seeker.Id, "Standard-CV", "Klas Olsson", Clock).Value;
        db.Resumes.Add(resume);
        seeker.SetPrimaryResume(resume.Id, Clock).IsSuccess.ShouldBeTrue();

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

        // The uploaded original. Coupled to the parse, which is the link both product
        // cascades follow. Content is opaque sealed bytes here — the handler must never read it.
        var file = ResumeFile.CaptureOriginal(
            seeker.Id, parsed.Id, [0x01, 0x02, 0x03], "application/pdf", "CV_Klas.pdf",
            byteSize: 3, pnrFlagged: false, pnrConsentAt: null,
            pnrConsentDialogVersion: null, Clock).Value;
        db.ResumeFiles.Add(file);
        // Detached after seeding so the fixture matches the tracker state production has.
        // The row is written by ImportResumeCommandHandler in a DIFFERENT request, and the
        // reset handler only ever PROJECTS its id — it never materialises the entity, because
        // that would pull the sealed bytea it has no business reading. A fixture that leaves
        // the instance tracked makes the key-only delete stub collide with itself, which is a
        // property of the fixture and not of the handler.

        // The setup's own OUTPUT: empty preferences beside a live graded match is a state no
        // lifecycle in src/ produces. Keyed on UserId, not JobSeekerId (#868).
        var match = UserJobAdMatch.Create(
            userId, new JobAdId(Guid.NewGuid()), NotifiableMatchGrade.Strong, ["skill_1"], Clock).Value;
        db.UserJobAdMatches.Add(match);

        var saved = SavedJobAd.Save(seeker.Id, new JobAdId(Guid.NewGuid()), Clock.UtcNow);
        db.SavedJobAds.Add(saved);

        var recent = RecentJobSearch.Capture(seeker.Id, SampleCriteria(), currentCount: 3, Clock.UtcNow);
        db.RecentJobSearches.Add(recent);

        await db.SaveChangesAsync(CancellationToken.None);
        db.Detach(file);
        return seeker;
    }

    // The audited id must be non-empty on EVERY branch that reaches a success, and this is
    // the measurement rather than the claim: AuditLogEntry.Create refuses Guid.Empty
    // outright, AuditBehavior calls ExtractAggregateId on any success, and nothing in the
    // API's typed catches turns an ArgumentException into anything but a 500. So a tolerant
    // no-op that carried an empty id would answer 500 instead of 204 — while a unit test
    // that only checked IsSuccess stayed green, because it never goes through the pipeline.
    // Asserting through AuditLogEntry.Create is what couples this test to the real refusal.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ResetMyDataCommandHandler_OnEverySuccessfulBranch_ReturnsAnIdTheAuditRowAccepts(
        bool hasJobSeeker)
    {
        var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        if (hasJobSeeker)
            await SeedFullUserAsync(db, userId);

        var handler = new ResetMyDataCommandHandler(db, AuthenticatedAs(userId), Clock, EnabledDevTools);

        var result = await handler.Handle(new ResetMyDataCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(userId);

        // The actor that would reject an empty id, invoked directly.
        Should.NotThrow(() => AuditLogEntry.Create(
            occurredAt: Clock.UtcNow,
            correlationId: Guid.NewGuid(),
            userId: userId,
            eventType: "User.DataReset",
            aggregateType: "User",
            aggregateId: result.Value,
            ipAddress: null,
            userAgent: null));
    }

    [Fact]
    public async Task ResetMyDataCommandHandler_WhenFlagDisabled_RefusesBeforeTouchingAnything()
    {
        // Gate two of two. The map gate in Program.cs decides whether the ROUTE exists; this one
        // decides whether the OPERATION runs, so the primitive stays refused if it is ever reached
        // by another caller. Fail-closed is the DEFAULT here, not a configured value: the options
        // object is constructed with no initialiser, exactly as an absent DevTools section binds.
        var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var seeker = JobSeeker.Register(userId, "Me", Clock).Value;
        seeker.UpdateMatchPreferences(StatedPreferences(), Clock);
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var handler = new ResetMyDataCommandHandler(
            db, AuthenticatedAs(userId), Clock, Options.Create(new DevToolsOptions()));

        var result = await handler.Handle(
            new ResetMyDataCommand(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Code.ShouldBe("Dev.ResetMyDataDisabled");

        // And it refused BEFORE mutating: the stated preferences are still there.
        var after = await db.JobSeekers.FirstAsync(
            js => js.UserId == userId, TestContext.Current.CancellationToken);
        after.MatchPreferences.PreferredOccupationGroups.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task ResetMyDataCommandHandler_WhenNotAuthenticated_ReturnsNotAuthenticatedFailure()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var handler = new ResetMyDataCommandHandler(db, currentUser, Clock, EnabledDevTools);

        var result = await handler.Handle(new ResetMyDataCommand(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Dev.NotAuthenticated");
    }

    [Fact]
    public async Task ResetMyDataCommandHandler_WhenNoJobSeekerForUser_ReturnsTolerantSuccess()
    {
        var db = TestAppDbContextFactory.Create();
        var handler = new ResetMyDataCommandHandler(db, AuthenticatedAs(Guid.NewGuid()), Clock, EnabledDevTools);

        var result = await handler.Handle(new ResetMyDataCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ResetMyDataCommandHandler_WhenAuthenticated_ClearsOwnDataAndResetsMatchPreferences()
    {
        var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var seeker = await SeedFullUserAsync(db, userId);

        var handler = new ResetMyDataCommandHandler(db, AuthenticatedAs(userId), Clock, EnabledDevTools);

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

        // The uploaded originals are HARD-deleted. The retention sweep deliberately does not
        // collect a PROMOTED original, so without this arm the raw PDF survived every reset.
        (await db.ResumeFiles.AnyAsync(f => f.JobSeekerId == seeker.Id, CancellationToken.None))
            .ShouldBeFalse();

        // Graded matches hard-deleted — resolved on UserId, which is a different key from the
        // JobSeekerId every other arm uses.
        (await db.UserJobAdMatches.AnyAsync(m => m.UserId == userId, CancellationToken.None))
            .ShouldBeFalse();

        // Match preferences reset to Empty → hasStatedDesiredOccupation becomes false.
        var reloadedSeeker = await db.JobSeekers
            .FirstAsync(js => js.UserId == userId, CancellationToken.None);
        reloadedSeeker.MatchPreferences.ShouldBe(MatchPreferences.Empty);

        // And the account no longer points at a CV that is now invisible.
        reloadedSeeker.PrimaryResumeId.ShouldBeNull();

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

        var handler = new ResetMyDataCommandHandler(db, AuthenticatedAs(meUserId), Clock, EnabledDevTools);

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

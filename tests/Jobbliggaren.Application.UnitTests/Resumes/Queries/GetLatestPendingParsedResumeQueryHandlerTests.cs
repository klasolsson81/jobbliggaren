using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Resumes.Queries.GetLatestPendingParsedResume;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Queries;

/// <summary>
/// Fas 4 onboarding decouple (ADR 0079-amendment 2026-06-23, pending-card bind) — the read handler
/// returning the CURRENT user's most-recent PendingReview parsed-CV SUMMARY (id + file name + upload
/// time), or null when there is none. Owner-scope is the only access rule (no client-supplied id, so
/// no IDOR/enumeration surface). Like <c>GetParsedResumeOccupations</c>, the summary projects
/// plaintext metadata columns (never the encrypted CV-PII shadows), so the populated path IS
/// unit-testable on InMemory; the real-Postgres projection is covered end-to-end by the endpoint
/// integration tests.
/// </summary>
public class GetLatestPendingParsedResumeQueryHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public GetLatestPendingParsedResumeQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private GetLatestPendingParsedResumeQueryHandler CreateSut(Infrastructure.Persistence.AppDbContext db) =>
        new(db, _currentUser);

    private static ParsedResume BuildParsedResume(
        JobSeekerId owner, string sourceFileName, IDateTimeProvider clock)
    {
        var content = new ParsedResumeContent(
            new ParsedContact("Anna Andersson", "anna@example.com", "070-1234567", "Stockholm"),
            profile: "Erfaren backend-utvecklare.",
            experience: [new ParsedExperience("Backend-utvecklare", "Acme AB", "2021–2024", "Acme AB, 2021–2024")],
            education: [new ParsedEducation("KTH", "Civilingenjör", "2015–2020", "KTH 2015–2020")],
            skills: ["C#", "PostgreSQL"],
            languages: ["Svenska", "Engelska"]);

        var confidence = ParseConfidence.FromSections(
        [
            new SectionConfidence(ParsedSectionKind.Contact, SectionConfidenceLevel.Confident, []),
        ]);

        return ParsedResume.Create(
            owner, sourceFileName, "application/pdf", ResumeLanguage.Sv,
            content, "Anna Andersson\nLedde teamet.", confidence,
            PersonnummerScanOutcome.None,
            occupationProposals: [],
            clock).Value;
    }

    private static async Task<JobSeeker> SeedSeekerAsync(
        Infrastructure.Persistence.AppDbContext db, Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return seeker;
    }

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenUserIdIsNull()
    {
        var db = TestAppDbContextFactory.Create();
        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);
        var sut = new GetLatestPendingParsedResumeQueryHandler(db, anon);

        var result = await sut.Handle(
            new GetLatestPendingParsedResumeQuery(), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenJobSeekerNotFound()
    {
        var db = TestAppDbContextFactory.Create();

        var result = await CreateSut(db).Handle(
            new GetLatestPendingParsedResumeQuery(), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenSeekerHasNoPendingArtifact()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db, _userId);

        var result = await CreateSut(db).Handle(
            new GetLatestPendingParsedResumeQuery(), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ShouldReturnSummary_WhenOwnPendingArtifactExists()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);
        var parsed = BuildParsedResume(seeker.Id, "CV_Anna.pdf", FakeDateTimeProvider.Default);
        db.ParsedResumes.Add(parsed);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateSut(db).Handle(
            new GetLatestPendingParsedResumeQuery(), TestContext.Current.CancellationToken);

        result.ShouldNotBeNull();
        result.Id.ShouldBe(parsed.Id.Value);
        result.SourceFileName.ShouldBe("CV_Anna.pdf");
        result.UploadedAt.ShouldBe(FakeDateTimeProvider.Default.UtcNow);
    }

    [Fact]
    public async Task Handle_ShouldReturnMostRecent_WhenMultiplePendingArtifacts()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);
        var older = new FakeDateTimeProvider(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));
        var newer = new FakeDateTimeProvider(new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero));
        db.ParsedResumes.Add(BuildParsedResume(seeker.Id, "old.pdf", older));
        var latest = BuildParsedResume(seeker.Id, "new.pdf", newer);
        db.ParsedResumes.Add(latest);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateSut(db).Handle(
            new GetLatestPendingParsedResumeQuery(), TestContext.Current.CancellationToken);

        result.ShouldNotBeNull();
        result.Id.ShouldBe(latest.Id.Value);
        result.SourceFileName.ShouldBe("new.pdf");
    }

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenOnlyArtifactIsPromoted()
    {
        // A promoted artifact is soft-deleted and excluded by the global DeletedAt filter (and is no
        // longer PendingReview) → the user has no pending CV.
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);
        var parsed = BuildParsedResume(seeker.Id, "CV_Anna.pdf", FakeDateTimeProvider.Default);
        db.ParsedResumes.Add(parsed);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        parsed.Promote(FakeDateTimeProvider.Default).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateSut(db).Handle(
            new GetLatestPendingParsedResumeQuery(), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenOnlyArtifactIsDiscarded()
    {
        // Discard is a SEPARATE transition from Promote (different Status value, its own code
        // path) — a discarded artifact is soft-deleted (DeletedAt set) and Status == Discarded,
        // so BOTH the global DeletedAt filter and the explicit Status == PendingReview predicate
        // must exclude it → the user has no pending CV.
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);
        var parsed = BuildParsedResume(seeker.Id, "CV_Anna.pdf", FakeDateTimeProvider.Default);
        db.ParsedResumes.Add(parsed);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        parsed.Discard(FakeDateTimeProvider.Default);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateSut(db).Handle(
            new GetLatestPendingParsedResumeQuery(), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ShouldReturnPending_WhenSeekerHasBothPromotedAndPendingArtifacts()
    {
        // The realistic onboarding scenario for the pending-card: the user already promoted (or
        // discarded) an earlier CV and then imported a fresh one that is still PendingReview. The
        // handler must select the PendingReview artifact via its Status predicate — NOT the most-
        // recent-by-CreatedAt regardless of status. Here the PROMOTED artifact is the NEWER one, so
        // a status-blind OrderByDescending(CreatedAt) would wrongly pick it (or, being soft-deleted,
        // return null); only the correct Status-then-recency filter returns the pending summary.
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);

        var earlier = new FakeDateTimeProvider(new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero));
        var pending = BuildParsedResume(seeker.Id, "pending.pdf", earlier);
        db.ParsedResumes.Add(pending);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var later = new FakeDateTimeProvider(new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero));
        var promoted = BuildParsedResume(seeker.Id, "promoted.pdf", later);
        db.ParsedResumes.Add(promoted);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        promoted.Promote(later).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateSut(db).Handle(
            new GetLatestPendingParsedResumeQuery(), TestContext.Current.CancellationToken);

        result.ShouldNotBeNull();
        result.Id.ShouldBe(pending.Id.Value);
        result.SourceFileName.ShouldBe("pending.pdf");
    }

    [Fact]
    public async Task Handle_ShouldNotReturnAnotherUsersPendingArtifact()
    {
        // Owner-scope: a different job seeker's pending CV is invisible to this user.
        var db = TestAppDbContextFactory.Create();
        var otherSeeker = await SeedSeekerAsync(db, Guid.NewGuid());
        db.ParsedResumes.Add(BuildParsedResume(otherSeeker.Id, "other.pdf", FakeDateTimeProvider.Default));
        await SeedSeekerAsync(db, _userId);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateSut(db).Handle(
            new GetLatestPendingParsedResumeQuery(), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
    }
}

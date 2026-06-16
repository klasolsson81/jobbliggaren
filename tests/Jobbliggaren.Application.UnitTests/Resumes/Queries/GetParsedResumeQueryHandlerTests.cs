using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Resumes.Queries.GetParsedResume;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Queries;

/// <summary>
/// Fas 4 STEG B / B1b — the read handler returning the OWNING job seeker's PendingReview
/// parsed-CV staging artifact. Covers the fail-closed IDOR orchestration ONLY (resolve owner →
/// owner-scoped FirstOrDefault → cross-user/not-found → null + audit). The happy-path content
/// mapping is NOT unit-tested here: InMemory + AsNoTracking re-materializes the artifact with a
/// null <c>Content</c> (an EF-Ignore'd encrypted Form-B shadow only the real decryption
/// interceptor populates), so the positive find→map→return branch is proven end-to-end by
/// <c>GetParsedResumeEndpointTests.Import_then_GET_parsed_returns_200</c> (real PG decryption),
/// and the mapping fidelity by <c>GetParsedResumeMapperTests</c>. Parity with
/// <c>ReviewParsedResumeQueryHandlerTests</c> (whose happy path only works because the engine is
/// mocked, never dereferencing Content).
/// </summary>
public class GetParsedResumeQueryHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IFailedAccessLogger _failedAccess = Substitute.For<IFailedAccessLogger>();
    private readonly Guid _userId = Guid.NewGuid();

    public GetParsedResumeQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private GetParsedResumeQueryHandler CreateSut(Infrastructure.Persistence.AppDbContext db) =>
        new(db, _currentUser, _failedAccess);

    private static ParsedResume BuildParsedResume(JobSeekerId owner)
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
            new SectionConfidence(ParsedSectionKind.Experience, SectionConfidenceLevel.Confident, []),
        ]);

        return ParsedResume.Create(
            owner, "CV_Anna.pdf", "application/pdf", ResumeLanguage.Sv,
            content, "Anna Andersson\nLedde teamet.", confidence,
            PersonnummerScanOutcome.None,
            [new ProposedOccupation("q8wL_kdi_WaW", "Systemutvecklare", "Backend-utvecklare")],
            FakeDateTimeProvider.Default).Value;
    }

    private static async Task<ParsedResume> SeedOwnedAsync(
        Infrastructure.Persistence.AppDbContext db, Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        var parsed = BuildParsedResume(seeker.Id);
        db.ParsedResumes.Add(parsed);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return parsed;
    }

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenUserIdIsNull()
    {
        var db = TestAppDbContextFactory.Create();
        var parsed = await SeedOwnedAsync(db, _userId);
        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);
        var sut = new GetParsedResumeQueryHandler(db, anon, _failedAccess);

        var result = await sut.Handle(
            new GetParsedResumeQuery(parsed.Id.Value), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenJobSeekerNotFound()
    {
        var db = TestAppDbContextFactory.Create();

        var result = await CreateSut(db).Handle(
            new GetParsedResumeQuery(Guid.NewGuid()), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenArtifactNotFound_AndNotLogCrossUser()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateSut(db).Handle(
            new GetParsedResumeQuery(Guid.NewGuid()), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
        _failedAccess.DidNotReceive().LogCrossUserAttempt(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_ShouldReturnNullAndLogCrossUserAttempt_WhenArtifactBelongsToOtherUser()
    {
        var db = TestAppDbContextFactory.Create();
        var otherParsed = await SeedOwnedAsync(db, Guid.NewGuid());
        var self = JobSeeker.Register(_userId, "Self", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(self);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateSut(db).Handle(
            new GetParsedResumeQuery(otherParsed.Id.Value), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
        _failedAccess.Received(1).LogCrossUserAttempt(
            "ParsedResume", otherParsed.Id.Value, _userId, "GetParsedResume");
    }

    [Fact]
    public async Task Handle_ShouldReturnNull_AndNotLogCrossUser_WhenOwnArtifactIsPromoted()
    {
        // A promoted (soft-deleted) artifact is excluded by the global DeletedAt filter from BOTH
        // the owner-scoped find AND the AnyAsync probe → plain null, no false cross-user audit
        // on a legitimate own-promote (the documented endpoint behaviour).
        var db = TestAppDbContextFactory.Create();
        var parsed = await SeedOwnedAsync(db, _userId);
        parsed.Promote(FakeDateTimeProvider.Default).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateSut(db).Handle(
            new GetParsedResumeQuery(parsed.Id.Value), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
        _failedAccess.DidNotReceive().LogCrossUserAttempt(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>());
    }
}

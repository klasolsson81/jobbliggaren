using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.Resumes.Commands.DiscardParsedResume;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Commands.DiscardParsedResume;

// Fas 4b CV-motor v2 PR-8.1 (issue #657, ADR 0074; CTO-bind Q5) — the user rejecting an imported CV
// from the "complete your CV" gap card. Mirrors PromoteParsedResumeCommandHandler's shape: auth →
// owner resolve → owner-scoped ParsedResume load (IDOR fail-closed, identical NotFound) → the
// aggregate's Discard transition (Status=Discarded + soft-delete). A promoted/discarded artifact is
// already soft-deleted → invisible to the owner-scoped load → the unknown-id NotFound branch covers
// it (no separate NotPendingReview gate needed; the aggregate's idempotent Discard is proven in
// ParsedResumeTests).
//
// EF InMemory is sufficient (parity PromoteParsedResumeCommandHandlerTests): the seeded ParsedResume
// Content shadow is read back in the SAME context, no Form-B decrypt. SPEC-DRIVEN — RED until the
// command + validator + handler ship.
public class DiscardParsedResumeCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IFailedAccessLogger _failedAccess = Substitute.For<IFailedAccessLogger>();
    private readonly Guid _userId = Guid.NewGuid();

    public DiscardParsedResumeCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private DiscardParsedResumeCommandHandler CreateSut(Infrastructure.Persistence.AppDbContext db) =>
        new(db, _currentUser, FakeDateTimeProvider.Default, _failedAccess);

    private static ParsedResume BuildPendingReview(JobSeekerId owner)
    {
        var content = new ParsedResumeContent(
            new ParsedContact("Anna Andersson", "anna@example.com", "070-1234567", "Stockholm"),
            profile: "Erfaren backend-utvecklare.",
            experience: [new ParsedExperience("Backend-utvecklare", "Beta AB", "2021–", "raw entry")]);

        return ParsedResume.Create(
            owner, "anna-cv.pdf", "application/pdf", ResumeLanguage.Sv,
            content, "Anna Andersson\nBackend-utvecklare, Beta AB",
            ParseConfidence.FromSections(
            [
                new SectionConfidence(ParsedSectionKind.Contact, SectionConfidenceLevel.Confident, []),
            ]),
            PersonnummerScanOutcome.None, [], FakeDateTimeProvider.Default).Value;
    }

    private static async Task<ParsedResume> SeedOwnedAsync(
        Infrastructure.Persistence.AppDbContext db, Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        var parsed = BuildPendingReview(seeker.Id);
        db.ParsedResumes.Add(parsed);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return parsed;
    }

    // ===============================================================
    // Happy path — Discarded + soft-deleted
    // ===============================================================

    [Fact]
    public async Task Handle_WhenOwnedPendingArtifact_ReturnsSuccess_MarksDiscarded_SoftDeletesFromClock()
    {
        var db = TestAppDbContextFactory.Create();
        var parsed = await SeedOwnedAsync(db, _userId);

        var result = await CreateSut(db).Handle(
            new DiscardParsedResumeCommand(parsed.Id.Value), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var reloaded = db.ParsedResumes.Local.ShouldHaveSingleItem();
        reloaded.Status.ShouldBe(ParsedResumeStatus.Discarded);
        reloaded.DeletedAt.ShouldBe(FakeDateTimeProvider.Default.UtcNow);
    }

    // ===============================================================
    // Auth / not-found
    // ===============================================================

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ThrowsUnauthorizedException()
    {
        var db = TestAppDbContextFactory.Create();
        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);
        var sut = new DiscardParsedResumeCommandHandler(db, anon, FakeDateTimeProvider.Default, _failedAccess);

        await Should.ThrowAsync<UnauthorizedException>(
            () => sut.Handle(new DiscardParsedResumeCommand(Guid.NewGuid()),
                TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task Handle_WhenJobSeekerNotFound_ReturnsNotFoundFailure()
    {
        var db = TestAppDbContextFactory.Create(); // no JobSeeker for _userId

        var result = await CreateSut(db).Handle(
            new DiscardParsedResumeCommand(Guid.NewGuid()), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.NotFound");
    }

    [Fact]
    public async Task Handle_WhenArtifactUnknown_ReturnsNotFoundFailure_NoCrossUserLog()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateSut(db).Handle(
            new DiscardParsedResumeCommand(Guid.NewGuid()), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ParsedResume.NotFound");
        // Unknown id (a legitimate typo or an already-finalized, soft-deleted artifact) is NOT logged.
        _failedAccess.DidNotReceive().LogCrossUserAttempt(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>());
    }

    // ===============================================================
    // IDOR cross-user (fail-closed identical NotFound, logged once, no mutation)
    // ===============================================================

    [Fact]
    public async Task Handle_WhenArtifactBelongsToOtherUser_ReturnsNotFound_LogsCrossUser_NoMutation()
    {
        var db = TestAppDbContextFactory.Create();
        var otherParsed = await SeedOwnedAsync(db, Guid.NewGuid());
        var self = JobSeeker.Register(_userId, "Self", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(self);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateSut(db).Handle(
            new DiscardParsedResumeCommand(otherParsed.Id.Value), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ParsedResume.NotFound"); // identical NotFound — no enumeration oracle
        _failedAccess.Received(1).LogCrossUserAttempt(
            "ParsedResume", otherParsed.Id.Value, _userId, Arg.Any<string>());
        // No mutation on someone else's artifact.
        otherParsed.Status.ShouldBe(ParsedResumeStatus.PendingReview);
        otherParsed.DeletedAt.ShouldBeNull();
    }

    // ===============================================================
    // Audit contract (IAuditableCommand<Result>)
    // ===============================================================

    [Fact]
    public void Command_AuditContract_IsParsedResumeDiscarded()
    {
        var id = Guid.NewGuid();
        var command = new DiscardParsedResumeCommand(id);

        command.EventType.ShouldBe("ParsedResume.Discarded");
        command.AggregateType.ShouldBe("ParsedResume");
        command.ExtractAggregateId(Result.Success()).ShouldBe(id);
    }
}

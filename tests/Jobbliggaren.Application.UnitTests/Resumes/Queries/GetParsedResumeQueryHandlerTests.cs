using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Resumes.Common;
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
/// parsed-CV staging artifact: the fail-closed IDOR orchestration (resolve owner → owner-scoped
/// FirstOrDefault → cross-user/not-found → null + audit) and, since #1060 PR C, the derived
/// auto-promote block reason.
///
/// <para><b>Correction (#1060 PR C, found by <c>test-writer</c>).</b> This docblock used to say
/// the happy path could NOT be unit-tested here, because InMemory + <c>AsNoTracking</c>
/// re-materializes the artifact with a null <c>Content</c> (the EF-Ignore'd encrypted Form-B
/// shadow only the real decryption interceptor populates). The premise is true; the conclusion
/// was false. <see cref="FakeContentHydrationInterceptor"/> is the house seam for exactly this,
/// it sets <c>Content</c> on MATERIALIZATION (so tracking is irrelevant), and
/// <c>GetResumeAtsTextQueryHandlerTests</c> / <c>GetResumeByIdQueryHandlerTests</c> both use it
/// against the same load shape — the latter noting in as many words that "the seam, not the
/// intent, was missing". The seam's own docblock even names THIS file as where it is
/// documented, while this file denied it existed. The real decrypt path stays proven end to end
/// by <c>GetParsedResumeEndpointTests</c>; what belongs here is the derivation's own branches,
/// which integration tests cover only where a production import can reach them.</para>
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
        new(db, _currentUser, _failedAccess, FakeDateTimeProvider.Default);

    /// <summary>A context whose materialization hydrates the EF-Ignore'd Form-B shadow, the way
    /// the real decryption interceptor does. Without it the read path answers only the two
    /// plaintext gates; with it the Tier-2 derivation is reachable in a unit test.</summary>
    private static Infrastructure.Persistence.AppDbContext CreateHydratedDb(
        ParsedResumeContent content) =>
        TestAppDbContextFactory.Create(
            new FakeContentHydrationInterceptor(parsedContent: content));

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
        var sut = new GetParsedResumeQueryHandler(db, anon, _failedAccess, FakeDateTimeProvider.Default);

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

    // ===============================================================
    // #1060 PR C — the derived block reason. These reach Tier 2 through
    // FakeContentHydrationInterceptor, which is what the earlier "cannot be unit-tested"
    // docblock got wrong. Integration coverage in GetParsedResumeEndpointTests is the
    // complement, not the substitute: it can only reach states a production IMPORT reaches,
    // and the promotable-yet-pending state below is not one of them.
    // ===============================================================

    private static ParsedResumeContent CleanContent(
        IReadOnlyList<ParsedExperience>? experience = null) =>
        new(
            new ParsedContact("Anna Andersson", "anna@example.com", "070-1234567", "Stockholm"),
            profile: "Erfaren backend-utvecklare.",
            experience: experience ??
                [new ParsedExperience("Backend-utvecklare", "Acme AB", "2021-2024", "raw")],
            education: [new ParsedEducation("KTH", "Civilingenjör", "2015-2020", "raw")],
            skills: ["C#"],
            languages: ["Svenska"]);

    private async Task<ParsedResume> SeedHydratedAsync(
        Infrastructure.Persistence.AppDbContext db, ParsedResumeContent content,
        string displayName = "Test User")
    {
        var seeker = JobSeeker.Register(_userId, displayName, FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        var parsed = ParsedResume.Create(
            seeker.Id, "CV_Anna.pdf", "application/pdf", ResumeLanguage.Sv,
            content, "raw text",
            ParseConfidence.FromSections(
            [
                new SectionConfidence(ParsedSectionKind.Contact, SectionConfidenceLevel.Confident, []),
                new SectionConfidence(ParsedSectionKind.Experience, SectionConfidenceLevel.Confident, []),
            ]),
            PersonnummerScanOutcome.None, [], FakeDateTimeProvider.Default).Value;
        db.ParsedResumes.Add(parsed);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return parsed;
    }

    [Fact]
    public async Task Handle_ShouldReportNoBlockReason_ForAPendingArtifactNothingBlocksAnyMore()
    {
        // §5 Tests: the premise is a PendingReview artifact that the gate now clears, and no
        // IMPORT can produce it — anything promotable gets promoted in the same request. The
        // actor that produces it is the GATE CHANGE: PR B (a72c77e7) retired
        // AutoPromoteBlockReason.UnclassifiedPreamble and narrowed the confidence gate from
        // RequiresManualReview to Failed, so every artifact left pending under the old gates
        // evaluates promotable now and is still sitting in the hub. That actor is a code change
        // rather than a callable method, so what the test asserts is the CURRENT gate's own
        // predicate over this content — which AutoPromoteGateTests pins independently.
        //
        // This is also the only assertion in the suite that a NON-null answer would fail, which
        // makes it the pin for two otherwise-invisible defects: a mapper that hardcodes a reason,
        // and a read path that passes a label the aggregate rejects (an empty label makes
        // Resume.ValidateName fail, and EVERY clean artifact would report IncompleteContent).
        var db = CreateHydratedDb(CleanContent());
        var parsed = await SeedHydratedAsync(db, CleanContent());

        var result = await CreateSut(db).Handle(
            new GetParsedResumeQuery(parsed.Id.Value), TestContext.Current.CancellationToken);

        result.ShouldNotBeNull();
        result.BlockReason.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ShouldReportIncompleteContent_WhenTheCanonicalResumeRejectsAnEntry()
    {
        var content = CleanContent(
            experience: [new ParsedExperience("Backend-utvecklare", null, "2021-2024", "raw")]);
        var db = CreateHydratedDb(content);
        var parsed = await SeedHydratedAsync(db, content);

        var result = await CreateSut(db).Handle(
            new GetParsedResumeQuery(parsed.Id.Value), TestContext.Current.CancellationToken);

        result.ShouldNotBeNull();
        result.BlockReason.ShouldBe(nameof(AutoPromoteBlockReason.IncompleteContent));
    }

    [Fact]
    public async Task Handle_ShouldPassTheOwnersDisplayName_IntoTheGatesContentGuard()
    {
        // The DisplayName column added to this handler's owner projection, pinned at the unit
        // level as well as end to end. A personnummer in the ACCOUNT NAME with a CLEAN file is
        // the only input on which the gate's answer depends on that column, so this is the
        // assertion that fails if the projection ever stops carrying it.
        var db = CreateHydratedDb(CleanContent());
        var parsed = await SeedHydratedAsync(db, CleanContent(), displayName: "Anna 811218-9876");

        var result = await CreateSut(db).Handle(
            new GetParsedResumeQuery(parsed.Id.Value), TestContext.Current.CancellationToken);

        result.ShouldNotBeNull();
        result.BlockReason.ShouldBe(nameof(AutoPromoteBlockReason.PersonnummerInAccountName));
        // The FILE is clean — so the reason cannot have come from the parse's own scan.
        result.Personnummer.Found.ShouldBeFalse();
    }
}

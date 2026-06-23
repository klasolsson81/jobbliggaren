using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Resumes.Queries.GetParsedResumeOccupations;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Queries;

/// <summary>
/// Fas 4 onboarding (CTO Variant B 2026-06-21) — the read handler returning the OWNING job
/// seeker's non-PII SSYK occupation proposals for a PendingReview parsed CV. Covers BOTH the
/// fail-closed IDOR orchestration (parity <c>GetParsedResumeQueryHandlerTests</c>) AND the
/// positive projection path: unlike the GetParsedResume Content (an encrypted Form-B shadow that
/// InMemory cannot rehydrate), <c>occupation_proposals</c> is plain jsonb projected via
/// <c>EF.Property</c> — so the populated find→map branch IS unit-testable here. The real-Postgres
/// value-converter projection (and that it never decrypts CV-PII) is proven end-to-end by
/// <c>GetParsedResumeOccupationsEndpointTests</c>.
/// </summary>
public class GetParsedResumeOccupationsQueryHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IFailedAccessLogger _failedAccess = Substitute.For<IFailedAccessLogger>();
    private readonly Guid _userId = Guid.NewGuid();

    public GetParsedResumeOccupationsQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private GetParsedResumeOccupationsQueryHandler CreateSut(Infrastructure.Persistence.AppDbContext db) =>
        new(db, _currentUser, _failedAccess);

    private static ParsedResume BuildParsedResume(
        JobSeekerId owner, IReadOnlyList<ProposedOccupation> proposals)
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
            proposals,
            FakeDateTimeProvider.Default).Value;
    }

    private static async Task<ParsedResume> SeedOwnedAsync(
        Infrastructure.Persistence.AppDbContext db, Guid userId,
        IReadOnlyList<ProposedOccupation> proposals)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        var parsed = BuildParsedResume(seeker.Id, proposals);
        db.ParsedResumes.Add(parsed);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return parsed;
    }

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenUserIdIsNull()
    {
        var db = TestAppDbContextFactory.Create();
        var parsed = await SeedOwnedAsync(db, _userId, [Proposal()]);
        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);
        var sut = new GetParsedResumeOccupationsQueryHandler(db, anon, _failedAccess);

        var result = await sut.Handle(
            new GetParsedResumeOccupationsQuery(parsed.Id.Value), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenJobSeekerNotFound()
    {
        var db = TestAppDbContextFactory.Create();

        var result = await CreateSut(db).Handle(
            new GetParsedResumeOccupationsQuery(Guid.NewGuid()), TestContext.Current.CancellationToken);

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
            new GetParsedResumeOccupationsQuery(Guid.NewGuid()), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
        _failedAccess.DidNotReceive().LogCrossUserAttempt(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_ShouldReturnNullAndLogCrossUserAttempt_WhenArtifactBelongsToOtherUser()
    {
        var db = TestAppDbContextFactory.Create();
        var otherParsed = await SeedOwnedAsync(db, Guid.NewGuid(), [Proposal()]);
        var self = JobSeeker.Register(_userId, "Self", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(self);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateSut(db).Handle(
            new GetParsedResumeOccupationsQuery(otherParsed.Id.Value), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
        _failedAccess.Received(1).LogCrossUserAttempt(
            "ParsedResume", otherParsed.Id.Value, _userId, "GetParsedResumeOccupations");
    }

    [Fact]
    public async Task Handle_ShouldReturnNull_AndNotLogCrossUser_WhenOwnArtifactIsPromoted()
    {
        // A promoted (soft-deleted) artifact is excluded by the global DeletedAt filter from BOTH
        // the owner-scoped find AND the AnyAsync probe → plain null, no false cross-user audit on
        // a legitimate own-promote (parity GetParsedResumeQueryHandler).
        var db = TestAppDbContextFactory.Create();
        var parsed = await SeedOwnedAsync(db, _userId, [Proposal()]);
        parsed.Promote(FakeDateTimeProvider.Default).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateSut(db).Handle(
            new GetParsedResumeOccupationsQuery(parsed.Id.Value), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
        _failedAccess.DidNotReceive().LogCrossUserAttempt(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_ShouldReturnProposals_WhenOwnArtifactHasProposals()
    {
        var db = TestAppDbContextFactory.Create();
        var parsed = await SeedOwnedAsync(db, _userId,
        [
            // ADR 0079-amendment: the first proposal carries a CV-derived ApproximateYears; the
            // second is "not stated" (null) — both must round-trip through the jsonb projection.
            new ProposedOccupation("q8wL_kdi_WaW", "Systemutvecklare", "Backend-utvecklare", 5),
            new ProposedOccupation("a1B2_c3D4_e5F", "Mjukvaruutvecklare", "Backend-utvecklare"),
        ]);

        var result = await CreateSut(db).Handle(
            new GetParsedResumeOccupationsQuery(parsed.Id.Value), TestContext.Current.CancellationToken);

        result.ShouldNotBeNull();
        result.Count.ShouldBe(2);
        result[0].ConceptId.ShouldBe("q8wL_kdi_WaW");
        result[0].Label.ShouldBe("Systemutvecklare");
        result[0].MatchedOn.ShouldBe("Backend-utvecklare");
        result[0].ApproximateYears.ShouldBe(5);
        result[1].ConceptId.ShouldBe("a1B2_c3D4_e5F");
        result[1].ApproximateYears.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ShouldReturnEmptyList_WhenOwnArtifactHasNoProposals()
    {
        // Found-with-no-proposals must be an EMPTY list, not null — the action maps empty → noRole
        // ("CV finns men inget yrke kunde läsas"), distinct from null → noCv ("inget CV").
        var db = TestAppDbContextFactory.Create();
        var parsed = await SeedOwnedAsync(db, _userId, []);

        var result = await CreateSut(db).Handle(
            new GetParsedResumeOccupationsQuery(parsed.Id.Value), TestContext.Current.CancellationToken);

        result.ShouldNotBeNull();
        result.ShouldBeEmpty();
        _failedAccess.DidNotReceive().LogCrossUserAttempt(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>());
    }

    private static ProposedOccupation Proposal() =>
        new("q8wL_kdi_WaW", "Systemutvecklare", "Backend-utvecklare");
}

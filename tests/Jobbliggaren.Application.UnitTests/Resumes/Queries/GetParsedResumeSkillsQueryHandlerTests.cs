using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Resumes.Queries.GetParsedResumeSkills;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Queries;

/// <summary>
/// ADR 0079 STEG 3 (CV-seeded skill chips) — the read handler returning the OWNING job
/// seeker's non-PII JobTech skill proposals for a PendingReview parsed CV. An EXACT mirror
/// of <see cref="GetParsedResumeOccupationsQueryHandlerTests"/>: the same fail-closed IDOR
/// orchestration (resolve owner → owner-scoped find → cross-user/unknown → null + audit, no
/// enumeration oracle) AND the positive projection path. Unlike the GetParsedResume Content
/// (an encrypted Form-B shadow InMemory cannot rehydrate), <c>skill_proposals</c> is plain
/// jsonb projected via <c>EF.Property</c> — so the populated find→map branch IS unit-testable
/// here. The real-Postgres value-converter projection (and that it never decrypts CV-PII) is
/// proven end-to-end by <c>GetParsedResumeSkillsEndpointTests</c>.
/// </summary>
public class GetParsedResumeSkillsQueryHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IFailedAccessLogger _failedAccess = Substitute.For<IFailedAccessLogger>();
    private readonly Guid _userId = Guid.NewGuid();

    public GetParsedResumeSkillsQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private GetParsedResumeSkillsQueryHandler CreateSut(Infrastructure.Persistence.AppDbContext db) =>
        new(db, _currentUser, _failedAccess);

    private static ParsedResume BuildParsedResume(
        JobSeekerId owner, IReadOnlyList<ProposedSkill> skillProposals)
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

        // No occupation proposals here — skill proposals are passed via the optional trailing
        // skillProposals param (ADR 0079), proving they are independent of occupation proposals.
        return ParsedResume.Create(
            owner, "CV_Anna.pdf", "application/pdf", ResumeLanguage.Sv,
            content, "Anna Andersson\nLedde teamet.", confidence,
            PersonnummerScanOutcome.None,
            [],
            FakeDateTimeProvider.Default,
            skillProposals).Value;
    }

    private static async Task<ParsedResume> SeedOwnedAsync(
        Infrastructure.Persistence.AppDbContext db, Guid userId,
        IReadOnlyList<ProposedSkill> skillProposals)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        var parsed = BuildParsedResume(seeker.Id, skillProposals);
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
        var sut = new GetParsedResumeSkillsQueryHandler(db, anon, _failedAccess);

        var result = await sut.Handle(
            new GetParsedResumeSkillsQuery(parsed.Id.Value), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenJobSeekerNotFound()
    {
        var db = TestAppDbContextFactory.Create();

        var result = await CreateSut(db).Handle(
            new GetParsedResumeSkillsQuery(Guid.NewGuid()), TestContext.Current.CancellationToken);

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
            new GetParsedResumeSkillsQuery(Guid.NewGuid()), TestContext.Current.CancellationToken);

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
            new GetParsedResumeSkillsQuery(otherParsed.Id.Value), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
        _failedAccess.Received(1).LogCrossUserAttempt(
            "ParsedResume", otherParsed.Id.Value, _userId, "GetParsedResumeSkills");
    }

    [Fact]
    public async Task Handle_ShouldReturnNull_AndNotLogCrossUser_WhenOwnArtifactIsPromoted()
    {
        // A promoted (soft-deleted) artifact is excluded by the global DeletedAt filter from BOTH
        // the owner-scoped find AND the AnyAsync probe → plain null, no false cross-user audit on
        // a legitimate own-promote (parity GetParsedResumeOccupationsQueryHandler).
        var db = TestAppDbContextFactory.Create();
        var parsed = await SeedOwnedAsync(db, _userId, [Proposal()]);
        parsed.Promote(FakeDateTimeProvider.Default).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateSut(db).Handle(
            new GetParsedResumeSkillsQuery(parsed.Id.Value), TestContext.Current.CancellationToken);

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
            new ProposedSkill("k1A2_b3C4_d5E", "C#"),
            new ProposedSkill("m6N7_o8P9_q0R", "PostgreSQL"),
        ]);

        var result = await CreateSut(db).Handle(
            new GetParsedResumeSkillsQuery(parsed.Id.Value), TestContext.Current.CancellationToken);

        result.ShouldNotBeNull();
        result.Count.ShouldBe(2);
        result[0].ConceptId.ShouldBe("k1A2_b3C4_d5E");
        result[0].Label.ShouldBe("C#");
        result[1].ConceptId.ShouldBe("m6N7_o8P9_q0R");
        result[1].Label.ShouldBe("PostgreSQL");
    }

    [Fact]
    public async Task Handle_ShouldReturnEmptyList_WhenOwnArtifactHasNoProposals()
    {
        // Found-with-no-proposals must be an EMPTY list, not null — the FE maps empty → "CV finns
        // men inga kompetenser kunde läsas", distinct from null → "inget CV" (parity occupations).
        var db = TestAppDbContextFactory.Create();
        var parsed = await SeedOwnedAsync(db, _userId, []);

        var result = await CreateSut(db).Handle(
            new GetParsedResumeSkillsQuery(parsed.Id.Value), TestContext.Current.CancellationToken);

        result.ShouldNotBeNull();
        result.ShouldBeEmpty();
        _failedAccess.DidNotReceive().LogCrossUserAttempt(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>());
    }

    private static ProposedSkill Proposal() => new("k1A2_b3C4_d5E", "C#");
}

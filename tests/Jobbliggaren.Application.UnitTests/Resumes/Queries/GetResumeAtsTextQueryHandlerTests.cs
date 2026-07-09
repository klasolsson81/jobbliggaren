using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Resumes.Queries.GetResumeAtsText;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Queries;

/// <summary>
/// Fas 4b PR-8.2 (#657, ADR 0093 §D5(e); CTO-bind PR-8 Q3) — the canonical ATS-text query. Thin
/// composition over already-tested parts: owner-resolve → owner-scoped load (Include Versions) →
/// <c>ResumeContentLinearizer</c> (the §D8 SPOT) → <c>PersonnummerRedactor</c> egress guard →
/// "Linearized"-sourced DTO. Mirrors <c>ReviewResumeQueryHandlerTests</c>: FirstOrDefault by Id +
/// JobSeekerId, cross-user attempt logged, null on not-found. The EF-Ignore'd ResumeVersion.Content
/// is hydrated on materialization by <see cref="FakeContentHydrationInterceptor"/> (InMemory +
/// AsNoTracking re-materializes it as null; the real decrypt path stays proven by the Api
/// integration tests). The happy-path text is pinned through the SAME public linearizer + redactor
/// the handler composes — a copied literal would let the view silently drift from the substrate.
/// </summary>
public class GetResumeAtsTextQueryHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IFailedAccessLogger _failedAccess = Substitute.For<IFailedAccessLogger>();
    private readonly Guid _userId = Guid.NewGuid();

    public GetResumeAtsTextQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private GetResumeAtsTextQueryHandler CreateSut(Infrastructure.Persistence.AppDbContext db) =>
        new(db, _currentUser, _failedAccess);

    // The canonical Master content is EF-Ignore'd; the handler dereferences it (linearize →
    // redact) before returning, so hydrate it on materialization exactly like the production
    // decrypt interceptor does (parity ReviewResumeQueryHandlerTests.CreateDb).
    private static Infrastructure.Persistence.AppDbContext CreateDb(ResumeContent? masterContent = null) =>
        TestAppDbContextFactory.Create(
            new FakeContentHydrationInterceptor(
                resumeContent: masterContent ?? ResumeContent.Empty("Klas Olsson")));

    private static async Task<Resume> SeedOwnedResumeAsync(
        Infrastructure.Persistence.AppDbContext db, Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        var resume = Resume.Create(seeker.Id, "Mitt CV", "Klas Olsson", FakeDateTimeProvider.Default).Value;
        db.Resumes.Add(resume);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return resume;
    }

    // ===============================================================
    // Auth / not-found / cross-user — null returns
    // ===============================================================

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenUserIdIsNull()
    {
        var db = CreateDb();
        var resume = await SeedOwnedResumeAsync(db, _userId);

        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);
        var sut = new GetResumeAtsTextQueryHandler(db, anon, _failedAccess);

        var result = await sut.Handle(
            new GetResumeAtsTextQuery(resume.Id.Value), TestContext.Current.CancellationToken);

        // An owned, real CV is still withheld from an unauthenticated principal.
        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenJobSeekerNotFound()
    {
        var db = CreateDb(); // no JobSeeker registered for _userId

        var result = await CreateSut(db).Handle(
            new GetResumeAtsTextQuery(Guid.NewGuid()), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ShouldReturnNullAndNotLogCrossUserAttempt_WhenResumeUnknown()
    {
        var db = CreateDb();
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateSut(db).Handle(
            new GetResumeAtsTextQuery(Guid.NewGuid()), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
        // A genuinely unknown id is a legit typo, never a cross-user probe → no ops signal.
        _failedAccess.DidNotReceive().LogCrossUserAttempt(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_ShouldReturnNullAndLogCrossUserAttempt_WhenResumeBelongsToOtherUser()
    {
        var db = CreateDb();
        var otherResume = await SeedOwnedResumeAsync(db, Guid.NewGuid());
        // The requesting user has a job seeker but does not own the resume.
        var self = JobSeeker.Register(_userId, "Self", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(self);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateSut(db).Handle(
            new GetResumeAtsTextQuery(otherResume.Id.Value), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
        // The operation string is part of the ops-signal contract (CloudWatch metric filter).
        _failedAccess.Received(1).LogCrossUserAttempt(
            "Resume", otherResume.Id.Value, _userId, "GetResumeAtsText");
    }

    // ===============================================================
    // Happy path — linearized + redacted text, "Linearized" source
    // ===============================================================

    [Fact]
    public async Task Handle_ShouldReturnLinearizedRedactedText_WhenOwnerRequestsOwnResume()
    {
        var content = new ResumeContent(
            new PersonalInfo("Klas Olsson", "klas@example.se", null, "Stockholm"),
            experiences:
            [
                new Experience("Acme AB", "Backend-utvecklare",
                    new DateOnly(2022, 1, 1), new DateOnly(2024, 6, 30), "Byggde .NET-tjänster."),
            ],
            summary: "Erfaren utvecklare med fokus på .NET.");
        var db = CreateDb(content);
        var resume = await SeedOwnedResumeAsync(db, _userId);

        var result = await CreateSut(db).Handle(
            new GetResumeAtsTextQuery(resume.Id.Value), TestContext.Current.CancellationToken);

        result.ShouldNotBeNull();
        // The only source claim this endpoint emits (D5e — never conflated with a parsed view).
        result.Source.ShouldBe("Linearized");
        // Pin the SPOT contract via the SAME public APIs the handler composes (linearizer →
        // redactor), computed here rather than copied — the view can never silently drift from
        // the substrate the review cites and the ATS-PDF renders.
        result.Text.ShouldBe(
            PersonnummerRedactor.Redact(ResumeContentLinearizer.Linearize(content).Text));
        result.Text.ShouldContain("Klas Olsson");
    }

    // ===============================================================
    // Belt-and-braces personnummer redaction at egress (§5, ADR 0074 Invariant 1)
    // ===============================================================

    [Fact]
    public async Task Handle_ShouldMaskPersonnummer_WhenMasterContentContainsOne()
    {
        // A personnummer-shaped span the user typed into the summary. The pnr guard is
        // Application-level; Resume's own ValidateContent does NOT pnr-scan, so the aggregate
        // accepts this content at the domain level (asserted below). The ATS-text query is a NEW
        // egress surface, so it redacts belt-and-braces before returning — this test pins that
        // the Redact step is actually in the pipeline (a missing call would leak the raw pnr).
        var pnrContent = new ResumeContent(
            new PersonalInfo("Klas Olsson", null, null, null),
            summary: "Ref 811218-9876 anges ej.");

        // Premise: the domain layer accepts pnr-shaped free text (the guard is not domain-level).
        var premise = Resume.Create(
            JobSeeker.Register(Guid.NewGuid(), "Owner", FakeDateTimeProvider.Default).Value.Id,
            "Mitt CV", "Klas Olsson", FakeDateTimeProvider.Default).Value;
        premise.UpdateMasterContent(pnrContent, FakeDateTimeProvider.Default).IsSuccess.ShouldBeTrue();

        var db = CreateDb(pnrContent);
        var resume = await SeedOwnedResumeAsync(db, _userId);

        var result = await CreateSut(db).Handle(
            new GetResumeAtsTextQuery(resume.Id.Value), TestContext.Current.CancellationToken);

        result.ShouldNotBeNull();
        // The pnr span is masked in place (every digit → *, separators kept); the surrounding
        // user text survives, so the view stays useful without leaking the identity number.
        result.Text.ShouldNotContain("811218-9876");
        result.Text.ShouldNotContain("811218");
        result.Text.ShouldContain("Ref ");
        result.Text.ShouldContain("anges ej.");
    }

    // ===============================================================
    // Soft delete — hidden by the global query filter (GDPR / ADR 0074 Art. 17)
    // ===============================================================

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenResumeIsSoftDeleted()
    {
        var db = CreateDb();
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        var resume = Resume.Create(seeker.Id, "Mitt CV", "Klas Olsson", FakeDateTimeProvider.Default).Value;
        resume.SoftDelete(FakeDateTimeProvider.Default);
        db.Resumes.Add(resume);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateSut(db).Handle(
            new GetResumeAtsTextQuery(resume.Id.Value), TestContext.Current.CancellationToken);

        result.ShouldBeNull();
        // The global DeletedAt==null filter hides the row from BOTH the owner-scoped load and the
        // existence probe, so a soft-deleted CV is a plain 404 with no cross-user ops signal
        // (the caller owns it — it is simply gone).
        _failedAccess.DidNotReceive().LogCrossUserAttempt(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>());
    }
}

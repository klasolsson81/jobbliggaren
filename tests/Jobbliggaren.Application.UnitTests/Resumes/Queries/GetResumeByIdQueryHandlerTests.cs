using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Resumes.Queries.GetResumeById;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Queries;

public class GetResumeByIdQueryHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public GetResumeByIdQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static async Task<Resume> SeedResumeAsync(
        Infrastructure.Persistence.AppDbContext db, Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);

        var resume = Resume.Create(seeker.Id, "Mitt CV", "Klas Olsson", FakeDateTimeProvider.Default).Value;
        db.Resumes.Add(resume);
        await db.SaveChangesAsync(CancellationToken.None);
        return resume;
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ReturnsNull()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var handler = new GetResumeByIdQueryHandler(db, currentUser, Substitute.For<IFailedAccessLogger>());

        var result = await handler.Handle(new GetResumeByIdQuery(resume.Id.Value), CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WhenJobSeekerNotFound_ReturnsNull()
    {
        var db = TestAppDbContextFactory.Create();

        var handler = new GetResumeByIdQueryHandler(db, _currentUser, Substitute.For<IFailedAccessLogger>());

        var result = await handler.Handle(new GetResumeByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WhenResumeNotFound_ReturnsNull()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetResumeByIdQueryHandler(db, _currentUser, Substitute.For<IFailedAccessLogger>());

        var result = await handler.Handle(new GetResumeByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WhenResumeBelongsToOtherUser_ReturnsNull()
    {
        var db = TestAppDbContextFactory.Create();
        var otherResume = await SeedResumeAsync(db, Guid.NewGuid());

        var ownSeeker = JobSeeker.Register(_userId, "Self", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(ownSeeker);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetResumeByIdQueryHandler(db, _currentUser, Substitute.For<IFailedAccessLogger>());

        var result = await handler.Handle(new GetResumeByIdQuery(otherResume.Id.Value), CancellationToken.None);

        result.ShouldBeNull();
    }

    // Handle_WhenResumeExists_ReturnsResumeDetailDtoWithMasterVersion borttagen
    // (senior-cto-advisor 2026-05-19, Approach C). Efter TD-13 #1c (ADR 0049
    // Mekanik-not 6) är ResumeVersion.Content EF-Ignore:ad och interceptor-ägd;
    // GetResumeByIdQueryHandler.Handle anropar ovillkorligt resume.ToDetailDto()
    // → v.Content.ToDto(). En bare InMemory-AppDbContext utan interceptor-paret
    // kan per konstruktion inte materialisera Content (InMemory förbjuden för
    // crypto, ADR 0049 Mekanik-not 4) → handlern NRE:ar före varje assertion.
    // Resume-found→DTO-shape-invarianten (id/name/versions/kind) är subsumerad
    // grön av Jobbliggaren.Api.IntegrationTests.Resumes.ResumesEndpointsTests
    // .GET_resume_by_id_returns_detail_with_master_version (hela HTTP→handler→
    // ToDetailDto-vägen mot riktig Postgres + produktions-interceptorerna).
    // Parity med C4.0-probe / C4.2a-gate-retirement; §7-coverage ej sänkt
    // (flyttad till korrekt lager). Handler-logiken (userId-null/jobseeker-/
    // resume-not-found/cross-user) bärs av övriga tester i denna klass.
}

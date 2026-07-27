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

    /// <summary>
    /// The happy path this class did not have. Every existing test here asserts
    /// <c>ShouldBeNull()</c> on an IDOR or not-found path, so nothing pinned that the handler
    /// projects any CONTENT at all — which is how the read side came to hold the only
    /// unmutated production lines in #1060's carrier change.
    ///
    /// <para>The preamble assertion is the load-bearing one. <c>ResumeMappingExtensions.ToDto</c>
    /// is the single line that puts the text on <c>ResumeDetailDto.Versions[].Content</c>, and
    /// that DTO is what <c>/cv/[id]/granska</c> renders <c>&lt;CvPreamble&gt;</c> from. Delete
    /// that one line and the whole user-visible affordance disappears from every saved CV while
    /// every other backend test stays green — measured, before this test existed.</para>
    /// </summary>
    [Fact]
    public async Task Handle_WhenOwned_ProjectsTheMasterContent_IncludingTheImportedPreamble()
    {
        const string preamble = "Erfaren backend-utvecklare med tio år i betalbranschen.";

        // Import-origin, because that is the only shape that carries a preamble at all.
        var content = new ResumeContent(
            new PersonalInfo("Klas Olsson", "klas@example.com", null, "Stockholm"),
            experiences: [new Experience("Acme AB", "Backend-utvecklare", null, null, null, "2021 - 2024")],
            skills: [new Skill("C#", 8)],
            summary: "Sammanfattning.",
            preamble: preamble);

        // ResumeVersion.Content is EF-Ignore'd — production rehydrates it from the content_enc
        // shadow through the decryption interceptor, and the handler reads it AsNoTracking, so
        // without the fake interceptor the projection dereferences null. Same reason and same
        // helper as ReviewResumeQueryHandlerTests. This is also why the class had no
        // content-bearing test before: the seam, not the intent, was missing.
        var db = TestAppDbContextFactory.Create(
            new FakeContentHydrationInterceptor(resumeContent: content));
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);

        var resume = Resume.CreateFromParsed(
            seeker.Id, "Importerat CV", content,
            new Domain.Resumes.Parsing.ParsedResumeId(Guid.NewGuid()), FakeDateTimeProvider.Default).Value;
        db.Resumes.Add(resume);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new GetResumeByIdQueryHandler(db, _currentUser, Substitute.For<IFailedAccessLogger>())
            .Handle(new GetResumeByIdQuery(resume.Id.Value), TestContext.Current.CancellationToken);

        result.ShouldNotBeNull();
        var master = result.Versions.ShouldHaveSingleItem();
        master.Kind.ShouldBe("Master");
        master.Content.Preamble.ShouldBe(preamble);
        // Positive controls: the projection really ran, so the assertion above is about the
        // preamble rather than about an empty DTO.
        master.Content.Summary.ShouldBe("Sammanfattning.");
        master.Content.Experiences.ShouldHaveSingleItem().Company.ShouldBe("Acme AB");
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

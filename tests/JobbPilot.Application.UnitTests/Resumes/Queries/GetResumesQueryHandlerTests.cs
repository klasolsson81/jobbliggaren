using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Resumes.Queries.GetResumes;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobSeekers;
using JobbPilot.Domain.Resumes;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Resumes.Queries;

public class GetResumesQueryHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public GetResumesQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static async Task<JobSeeker> SeedSeekerAsync(
        Infrastructure.Persistence.AppDbContext db, Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);
        return seeker;
    }

    private static async Task<Resume> AddResumeAsync(
        Infrastructure.Persistence.AppDbContext db,
        JobSeeker seeker,
        string name,
        IDateTimeProvider clock)
    {
        var resume = Resume.Create(seeker.Id, name, "Klas Olsson", clock).Value;
        db.Resumes.Add(resume);
        await db.SaveChangesAsync(CancellationToken.None);
        return resume;
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ReturnsEmptyPagedResult()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var handler = new GetResumesQueryHandler(db, currentUser);

        var result = await handler.Handle(new GetResumesQuery(), CancellationToken.None);

        result.Items.ShouldBeEmpty();
        result.TotalCount.ShouldBe(0);
        result.Page.ShouldBe(1);
        result.PageSize.ShouldBe(20);
    }

    [Fact]
    public async Task Handle_WhenJobSeekerNotFound_ReturnsEmptyPagedResult()
    {
        var db = TestAppDbContextFactory.Create();

        var handler = new GetResumesQueryHandler(db, _currentUser);

        var result = await handler.Handle(new GetResumesQuery(), CancellationToken.None);

        result.Items.ShouldBeEmpty();
        result.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Handle_ReturnsOnlyResumesBelongingToUser()
    {
        var db = TestAppDbContextFactory.Create();
        var ownSeeker = await SeedSeekerAsync(db, _userId);
        await AddResumeAsync(db, ownSeeker, "Mitt CV", FakeDateTimeProvider.Default);

        var otherSeeker = await SeedSeekerAsync(db, Guid.NewGuid());
        await AddResumeAsync(db, otherSeeker, "Annans CV", FakeDateTimeProvider.Default);

        var handler = new GetResumesQueryHandler(db, _currentUser);

        var result = await handler.Handle(new GetResumesQuery(), CancellationToken.None);

        result.Items.Count.ShouldBe(1);
        result.TotalCount.ShouldBe(1);
        result.Items[0].Name.ShouldBe("Mitt CV");
    }

    [Fact]
    public async Task Handle_ReturnsResumesSortedByUpdatedAtDescending()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);

        var older = new FakeDateTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var newer = new FakeDateTimeProvider(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero));
        await AddResumeAsync(db, seeker, "Äldre CV", older);
        await AddResumeAsync(db, seeker, "Nyare CV", newer);

        var handler = new GetResumesQueryHandler(db, _currentUser);

        var result = await handler.Handle(new GetResumesQuery(), CancellationToken.None);

        result.Items.Count.ShouldBe(2);
        result.TotalCount.ShouldBe(2);
        result.Items[0].Name.ShouldBe("Nyare CV");
        result.Items[1].Name.ShouldBe("Äldre CV");
    }

    [Fact]
    public async Task Handle_AppliesPagination()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);

        for (var i = 0; i < 5; i++)
        {
            var clock = new FakeDateTimeProvider(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(i));
            await AddResumeAsync(db, seeker, $"CV {i}", clock);
        }

        var handler = new GetResumesQueryHandler(db, _currentUser);

        var page1 = await handler.Handle(new GetResumesQuery(PageNumber: 1, PageSize: 2), CancellationToken.None);
        var page2 = await handler.Handle(new GetResumesQuery(PageNumber: 2, PageSize: 2), CancellationToken.None);
        var page3 = await handler.Handle(new GetResumesQuery(PageNumber: 3, PageSize: 2), CancellationToken.None);

        page1.Items.Count.ShouldBe(2);
        page2.Items.Count.ShouldBe(2);
        page3.Items.Count.ShouldBe(1);

        // TotalCount är independent of page-size — regression-skydd för PagedResult-kontraktet.
        page1.TotalCount.ShouldBe(5);
        page2.TotalCount.ShouldBe(5);
        page3.TotalCount.ShouldBe(5);
        page1.TotalPages.ShouldBe(3);
    }

    // Note: ett tidigare test "Handle_VersionCountIncludesOnlyNonDeletedVersions" togs bort —
    // EF InMemory applicerar inte global query filter på relaterade collections som Postgres
    // gör, vilket gjorde testet vilseledande. Verifiering av soft-delete-räkning sker
    // naturligare i integration-tester när Tailored-flödet öppnas i Fas 4.
}

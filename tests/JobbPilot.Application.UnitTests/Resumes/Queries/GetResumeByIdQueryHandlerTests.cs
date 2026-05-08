using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Resumes.Queries.GetResumeById;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.JobSeekers;
using JobbPilot.Domain.Resumes;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Resumes.Queries;

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

        var handler = new GetResumeByIdQueryHandler(db, currentUser);

        var result = await handler.Handle(new GetResumeByIdQuery(resume.Id.Value), CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WhenJobSeekerNotFound_ReturnsNull()
    {
        var db = TestAppDbContextFactory.Create();

        var handler = new GetResumeByIdQueryHandler(db, _currentUser);

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

        var handler = new GetResumeByIdQueryHandler(db, _currentUser);

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

        var handler = new GetResumeByIdQueryHandler(db, _currentUser);

        var result = await handler.Handle(new GetResumeByIdQuery(otherResume.Id.Value), CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WhenResumeExists_ReturnsResumeDetailDtoWithMasterVersion()
    {
        var db = TestAppDbContextFactory.Create();
        var resume = await SeedResumeAsync(db, _userId);

        var handler = new GetResumeByIdQueryHandler(db, _currentUser);

        var result = await handler.Handle(new GetResumeByIdQuery(resume.Id.Value), CancellationToken.None);

        result.ShouldNotBeNull();
        result!.Id.ShouldBe(resume.Id.Value);
        result.Name.ShouldBe("Mitt CV");
        result.Versions.Count.ShouldBe(1);
        result.Versions[0].Kind.ShouldBe("Master");
        result.Versions[0].Content.PersonalInfo.FullName.ShouldBe("Klas Olsson");
    }
}

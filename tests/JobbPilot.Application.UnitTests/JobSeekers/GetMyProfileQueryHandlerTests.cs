using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.JobSeekers.Queries.GetMyProfile;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.JobSeekers;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.JobSeekers;

public class GetMyProfileQueryHandlerTests
{
    [Fact]
    public async Task Handle_WhenJobSeekerExists_ReturnsProfile()
    {
        var userId = Guid.NewGuid();
        var db = TestAppDbContextFactory.Create();

        var seekerResult = JobSeeker.Register(userId, "Klas Olsson", FakeDateTimeProvider.Default);
        db.JobSeekers.Add(seekerResult.Value);
        await db.SaveChangesAsync(CancellationToken.None);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);

        var handler = new GetMyProfileQueryHandler(db, currentUser);

        var result = await handler.Handle(new GetMyProfileQuery(), CancellationToken.None);

        result.ShouldNotBeNull();
        result!.DisplayName.ShouldBe("Klas Olsson");
        result.Id.ShouldBe(seekerResult.Value.Id.Value);
    }

    [Fact]
    public async Task Handle_WhenJobSeekerNotFound_ReturnsNull()
    {
        var db = TestAppDbContextFactory.Create();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());

        var handler = new GetMyProfileQueryHandler(db, currentUser);

        var result = await handler.Handle(new GetMyProfileQuery(), CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WhenNotAuthenticated_ReturnsNull()
    {
        var db = TestAppDbContextFactory.Create();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var handler = new GetMyProfileQueryHandler(db, currentUser);

        var result = await handler.Handle(new GetMyProfileQuery(), CancellationToken.None);

        result.ShouldBeNull();
    }
}

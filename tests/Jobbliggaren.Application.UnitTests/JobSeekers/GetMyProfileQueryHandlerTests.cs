using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobSeekers.Queries.GetMyProfile;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobSeekers;

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
        // A never-set user projects an EMPTY overlay (present, not null/absent) so the FE can
        // .map() it safely (ADR 0079-amendment read-side projection).
        result.PreferredOccupationExperience.ShouldBeEmpty();
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

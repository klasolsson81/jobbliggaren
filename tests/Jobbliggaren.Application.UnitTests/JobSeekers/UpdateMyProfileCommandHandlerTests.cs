using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.JobSeekers.Commands.UpdateMyProfile;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobSeekers;

public class UpdateMyProfileCommandHandlerTests
{
    private static async Task<(UpdateMyProfileCommandHandler handler, AppDbContext db)> CreateHandler(Guid userId)
    {
        var db = TestAppDbContextFactory.Create();

        var seekerResult = JobSeeker.Register(userId, "Initial Name", FakeDateTimeProvider.Default);
        db.JobSeekers.Add(seekerResult.Value);
        await db.SaveChangesAsync(CancellationToken.None);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);

        var handler = new UpdateMyProfileCommandHandler(db, currentUser, FakeDateTimeProvider.Default);
        return (handler, db);
    }

    [Fact]
    public async Task Handle_WithNewDisplayName_UpdatesSuccessfully()
    {
        var userId = Guid.NewGuid();
        var (handler, db) = await CreateHandler(userId);

        var result = await handler.Handle(
            new UpdateMyProfileCommand("Klas Olsson", null, null, null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var seeker = db.JobSeekers.First(js => js.UserId == userId);
        seeker.DisplayName.ShouldBe("Klas Olsson");
    }

    [Fact]
    public async Task Handle_WithBlankDisplayName_ReturnsFailure()
    {
        var userId = Guid.NewGuid();
        var (handler, _) = await CreateHandler(userId);

        var result = await handler.Handle(
            new UpdateMyProfileCommand("   ", null, null, null), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.DisplayNameRequired");
    }

    [Fact]
    public async Task Handle_WithPreferences_UpdatesPreferences()
    {
        var userId = Guid.NewGuid();
        var (handler, db) = await CreateHandler(userId);

        var result = await handler.Handle(
            new UpdateMyProfileCommand(null, "en", false, false), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var seeker = db.JobSeekers.First(js => js.UserId == userId);
        seeker.Preferences.Language.ShouldBe("en");
        seeker.Preferences.EmailNotifications.ShouldBeFalse();
    }

    [Fact]
    public async Task Handle_WhenJobSeekerNotFound_ThrowsNotFoundException()
    {
        var db = TestAppDbContextFactory.Create();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());

        var handler = new UpdateMyProfileCommandHandler(db, currentUser, FakeDateTimeProvider.Default);

        await Should.ThrowAsync<NotFoundException>(
            () => handler.Handle(new UpdateMyProfileCommand("Name", null, null, null), CancellationToken.None).AsTask());
    }
}

using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.JobAds.Commands.CreateJobAd;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.JobAds;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.JobAds.Commands.CreateJobAd;

public class CreateJobAdCommandHandlerTests
{
    private static CreateJobAdCommand ValidCommand(DateTimeOffset? expiresAt = null) =>
        new(
            Title: "Senior Backend Engineer",
            CompanyName: "Klarna",
            Description: "Vi söker en senior backend-utvecklare.",
            Url: "https://jobs.klarna.com/job/123",
            Source: "Manual",
            PublishedAt: FakeDateTimeProvider.Default.UtcNow,
            ExpiresAt: expiresAt);

    [Fact]
    public async Task Handle_WithValidCommand_ReturnsSuccessWithGuid()
    {
        var db = Substitute.For<IAppDbContext>();
        db.JobAds.Returns(Substitute.For<DbSet<JobAd>>());
        var handler = new CreateJobAdCommandHandler(db, FakeDateTimeProvider.Default);

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task Handle_WhenExpiresAtBeforePublishedAt_ReturnsFailure()
    {
        var db = Substitute.For<IAppDbContext>();
        db.JobAds.Returns(Substitute.For<DbSet<JobAd>>());
        var handler = new CreateJobAdCommandHandler(db, FakeDateTimeProvider.Default);
        var expiresAt = FakeDateTimeProvider.Default.UtcNow.AddDays(-1);

        var result = await handler.Handle(ValidCommand(expiresAt), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobAd.InvalidDates");
    }

    [Fact]
    public async Task Handle_WithValidCommand_AddsJobAdToContextExactlyOnce()
    {
        var db = Substitute.For<IAppDbContext>();
        var jobAdSet = Substitute.For<DbSet<JobAd>>();
        db.JobAds.Returns(jobAdSet);
        var handler = new CreateJobAdCommandHandler(db, FakeDateTimeProvider.Default);

        await handler.Handle(ValidCommand(), CancellationToken.None);

        jobAdSet.Received(1).Add(Arg.Any<JobAd>());
    }
}

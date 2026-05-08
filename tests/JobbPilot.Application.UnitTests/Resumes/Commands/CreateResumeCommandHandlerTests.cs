using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Exceptions;
using JobbPilot.Application.Resumes.Commands.CreateResume;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.JobSeekers;
using JobbPilot.Domain.Resumes;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Resumes.Commands;

public class CreateResumeCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public CreateResumeCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static async Task<JobSeeker> SeedJobSeekerAsync(
        Infrastructure.Persistence.AppDbContext db, Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return seeker;
    }

    [Fact]
    public async Task Handle_WithValidCommand_ReturnsSuccessWithNonEmptyGuid()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedJobSeekerAsync(db, _userId);

        var handler = new CreateResumeCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);
        var command = new CreateResumeCommand("Mitt CV", "Klas Olsson");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task Handle_WithValidCommand_AddsResumeWithMasterVersionToDb()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedJobSeekerAsync(db, _userId);

        var handler = new CreateResumeCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);
        var command = new CreateResumeCommand("Mitt CV", "Klas Olsson");

        var result = await handler.Handle(command, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var resume = await db.Resumes
            .Include(r => r.Versions)
            .FirstOrDefaultAsync(
                r => r.Id == new ResumeId(result.Value),
                TestContext.Current.CancellationToken);
        resume.ShouldNotBeNull();
        resume!.Name.ShouldBe("Mitt CV");
        resume.Versions.Count.ShouldBe(1);
        resume.Versions[0].Kind.ShouldBe(ResumeVersionKind.Master);
    }

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ThrowsUnauthorizedException()
    {
        // I produktion fångar AuthorizationBehavior detta innan handler körs (ADR 0008).
        // Direkt-anrop testar att handlern inte sväljer felet om pipelinen kringgås.
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var handler = new CreateResumeCommandHandler(db, currentUser, FakeDateTimeProvider.Default);
        var command = new CreateResumeCommand("Mitt CV", "Klas Olsson");

        await Should.ThrowAsync<UnauthorizedException>(
            () => handler.Handle(command, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Handle_WhenJobSeekerNotFound_ReturnsJobSeekerNotFoundFailure()
    {
        var db = TestAppDbContextFactory.Create();

        var handler = new CreateResumeCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);
        var command = new CreateResumeCommand("Mitt CV", "Klas Olsson");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.NotFound");
    }

    [Fact]
    public async Task Handle_WithEmptyName_ReturnsResumeNameRequiredFailure()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedJobSeekerAsync(db, _userId);

        var handler = new CreateResumeCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);
        var command = new CreateResumeCommand("   ", "Klas Olsson");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.NameRequired");
    }

    [Fact]
    public async Task Handle_WithEmptyFullName_ReturnsResumeFullNameRequiredFailure()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedJobSeekerAsync(db, _userId);

        var handler = new CreateResumeCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);
        var command = new CreateResumeCommand("Mitt CV", "   ");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.FullNameRequired");
    }
}

using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.Resumes.Commands.CreateResume;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Commands;

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

    // Fas 4b PR-4 (security-auditor Major): the template path writes FullName into
    // canonical Master content, so it runs the SAME personnummer guard as promote/master
    // edits (ADR 0074 Invariant 1) — the canonical B4 verdict's "checked on every save"
    // claim rests on this. Both the compact and the separator form must be flagged
    // (guard shares the scanner+normalizer — this pins the wiring, not the scan logic).
    [Theory]
    [InlineData("Klas Olsson 19811218-9876")]
    [InlineData("Klas Olsson 198112189876")]
    public async Task Handle_WithPersonnummerInFullName_FailsWithGuardErrorAndAddsNothing(
        string fullName)
    {
        var db = TestAppDbContextFactory.Create();
        await SeedJobSeekerAsync(db, _userId);

        var handler = new CreateResumeCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);
        var command = new CreateResumeCommand("Mitt CV", fullName);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.PersonnummerMustBeRemoved");
        (await db.Resumes.AnyAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
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

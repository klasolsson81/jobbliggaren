using JobbPilot.Application.Applications.Commands.CreateApplication;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.UnitTests.Common;
using JobbPilot.Domain.Applications;
using JobbPilot.Domain.JobSeekers;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Applications.Commands;

// RÖD svit (TDD). Spec: architect-design §7 steg 7-10.
// CreateApplicationCommand utökas med `ManualPostingInput? Manual`
// (Title/Company/Url?/ExpiresAt? — INGEN Source). Handler:
//   command.Manual != null ⇒ ManualPosting.Create(...) (Result) →
//   Application.Create(jobSeekerId, jobAdId, coverLetter, manualPosting, clock).
// ManualPosting.Create-fel propageras som command-fel.
public class CreateApplicationManualPostingHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public CreateApplicationManualPostingHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private async Task SeedSeekerAsync(JobbPilot.Infrastructure.Persistence.AppDbContext db)
    {
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Handle_WithManualPostingAndNoJobAdId_CreatesApplicationWithManualPosting()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db);

        var handler = new CreateApplicationCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);
        var command = new CreateApplicationCommand(
            null, null,
            new ManualPostingInput("Backend-utvecklare", "Klarna", null, null));

        var result = await handler.Handle(command, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotBe(Guid.Empty);

        var app = await db.Applications.FindAsync(
            [new JobbPilot.Domain.Applications.ApplicationId(result.Value)], TestContext.Current.CancellationToken);
        app.ShouldNotBeNull();
        app!.JobAdId.ShouldBeNull();
        app.ManualPosting.ShouldNotBeNull();
        app.ManualPosting!.Title.ShouldBe("Backend-utvecklare");
        app.ManualPosting.Company.ShouldBe("Klarna");
    }

    [Fact]
    public async Task Handle_WithManualPostingFull_PersistsAllManualFields()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db);

        var expiresAt = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var handler = new CreateApplicationCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);
        var command = new CreateApplicationCommand(
            null, "Personligt brev",
            new ManualPostingInput(
                "Data Engineer", "Spotify", "https://example.com/jobb", expiresAt));

        var result = await handler.Handle(command, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var app = await db.Applications.FindAsync(
            [new JobbPilot.Domain.Applications.ApplicationId(result.Value)], TestContext.Current.CancellationToken);
        app.ShouldNotBeNull();
        app!.ManualPosting.ShouldNotBeNull();
        app.ManualPosting!.Url.ShouldBe("https://example.com/jobb");
        app.ManualPosting.ExpiresAt.ShouldBe(expiresAt);
        app.CoverLetter.ShouldBe("Personligt brev");
    }

    [Fact]
    public async Task Handle_WithoutManualPostingAndNoJobAdId_StillSucceeds()
    {
        // Oförändrat dagens cover-letter-only-beteende (Manual = null).
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db);

        var handler = new CreateApplicationCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);
        var command = new CreateApplicationCommand(null, null, null);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_WithJobAdIdAndNoManual_StillSucceedsUnchanged()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db);

        var jobAdId = Guid.NewGuid();
        var handler = new CreateApplicationCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);
        var command = new CreateApplicationCommand(jobAdId, null, null);

        var result = await handler.Handle(command, CancellationToken.None);
        await db.SaveChangesAsync(CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var app = await db.Applications.FindAsync(
            [new JobbPilot.Domain.Applications.ApplicationId(result.Value)], TestContext.Current.CancellationToken);
        app.ShouldNotBeNull();
        app!.JobAdId!.Value.Value.ShouldBe(jobAdId);
        app.ManualPosting.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WithBothJobAdIdAndManual_ReturnsFailureFromAggregateInvariant()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db);

        var handler = new CreateApplicationCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);
        var command = new CreateApplicationCommand(
            Guid.NewGuid(), null,
            new ManualPostingInput("Backend-utvecklare", "Klarna", null, null));

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.JobAdAndManualMutuallyExclusive");
    }

    [Fact]
    public async Task Handle_WithInvalidManualPostingUrl_PropagatesVoFailureAsCommandFailure()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db);

        var handler = new CreateApplicationCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);
        var command = new CreateApplicationCommand(
            null, null,
            new ManualPostingInput(
                "Backend-utvecklare", "Klarna", "javascript:alert(1)", null));

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ManualPosting.UrlInvalid");
    }

    [Fact]
    public async Task Handle_WithEmptyManualTitle_PropagatesVoFailureAsCommandFailure()
    {
        var db = TestAppDbContextFactory.Create();
        await SeedSeekerAsync(db);

        var handler = new CreateApplicationCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);
        var command = new CreateApplicationCommand(
            null, null, new ManualPostingInput("", "Klarna", null, null));

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ManualPosting.TitleRequired");
    }

    [Fact]
    public async Task Handle_WithManualPosting_WhenUserIdIsNull_ReturnsUnauthorized()
    {
        // Cross-user/auth-väg oförändrad (ICurrentUser-scoping bevarad).
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);

        var handler = new CreateApplicationCommandHandler(db, currentUser, FakeDateTimeProvider.Default);
        var command = new CreateApplicationCommand(
            null, null, new ManualPostingInput("Backend-utvecklare", "Klarna", null, null));

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Application.Unauthorized");
    }

    [Fact]
    public async Task Handle_WithManualPosting_WhenJobSeekerNotFound_ReturnsNotFound()
    {
        var db = TestAppDbContextFactory.Create();
        // ingen seeker seedad

        var handler = new CreateApplicationCommandHandler(db, _currentUser, FakeDateTimeProvider.Default);
        var command = new CreateApplicationCommand(
            null, null, new ManualPostingInput("Backend-utvecklare", "Klarna", null, null));

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.NotFound");
    }
}

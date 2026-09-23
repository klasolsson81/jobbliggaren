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

        var seekerResult = JobSeeker.Register(userId, "Initial Name", TermsAcceptance.AcceptCurrent(FakeDateTimeProvider.Default), FakeDateTimeProvider.Default);
        db.JobSeekers.Add(seekerResult.Value);
        await db.SaveChangesAsync(CancellationToken.None);

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);

        var handler = new UpdateMyProfileCommandHandler(db, currentUser, FakeDateTimeProvider.Default);
        return (handler, db);
    }

    [Fact]
    public async Task Handle_WithLanguage_UpdatesLanguage()
    {
        var userId = Guid.NewGuid();
        var (handler, db) = await CreateHandler(userId);

        var result = await handler.Handle(
            new UpdateMyProfileCommand("en"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var seeker = db.JobSeekers.First(js => js.UserId == userId);
        seeker.Preferences.Language.ShouldBe("en");
        result.Value.ShouldBe(seeker.Id.Value); // #192 echoed owner id
    }

    [Fact]
    public async Task Handle_WithLanguageChange_PreservesVag4ConsentFields()
    {
        // TD-115 consent-clobber regression: a profile language change must NOT reset the
        // Vag 4 background-match consent. The pre-fix handler rebuilt Preferences via the
        // positional constructor and silently zeroed BackgroundMatchNotificationsEnabled +
        // the Art. 7 consent timestamps. The `with`-expression fix preserves them.
        var userId = Guid.NewGuid();
        var (handler, db) = await CreateHandler(userId);
        var seeker = db.JobSeekers.First(js => js.UserId == userId);
        seeker.UpdateNotificationConsent(enabled: true, DigestCadence.Daily, FakeDateTimeProvider.Default);
        await db.SaveChangesAsync(CancellationToken.None);

        var result = await handler.Handle(
            new UpdateMyProfileCommand("en"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var reloaded = db.JobSeekers.First(js => js.UserId == userId);
        reloaded.Preferences.Language.ShouldBe("en");
        reloaded.Preferences.BackgroundMatchNotificationsEnabled.ShouldBeTrue();
        reloaded.Preferences.DigestCadence.ShouldBe(DigestCadence.Daily);
        reloaded.Preferences.NotificationConsentAt.ShouldNotBeNull();
        reloaded.Preferences.NotificationConsentWithdrawnAt.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_WithoutLanguage_LeavesPreferencesUntouched()
    {
        // TD-115: a command without a language must NOT touch Preferences at all (the Language
        // branch must be skipped) — else a future refactor could clobber consent through a no-op
        // mutation.
        var userId = Guid.NewGuid();
        var (handler, db) = await CreateHandler(userId);
        var seeker = db.JobSeekers.First(js => js.UserId == userId);
        seeker.UpdateNotificationConsent(enabled: true, DigestCadence.Daily, FakeDateTimeProvider.Default);
        await db.SaveChangesAsync(CancellationToken.None);
        var originalPrefs = seeker.Preferences;

        var result = await handler.Handle(
            new UpdateMyProfileCommand(null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var reloaded = db.JobSeekers.First(js => js.UserId == userId);
        reloaded.Preferences.ShouldBe(originalPrefs); // record value-equality — nothing touched
    }

    [Fact]
    public async Task Handle_WhenJobSeekerNotFound_ThrowsNotFoundException()
    {
        var db = TestAppDbContextFactory.Create();

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(Guid.NewGuid());

        var handler = new UpdateMyProfileCommandHandler(db, currentUser, FakeDateTimeProvider.Default);

        await Should.ThrowAsync<NotFoundException>(
            () => handler.Handle(new UpdateMyProfileCommand("sv"), CancellationToken.None).AsTask());
    }
}

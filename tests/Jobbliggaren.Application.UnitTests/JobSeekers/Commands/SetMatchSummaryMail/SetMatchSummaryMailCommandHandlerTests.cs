using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.JobSeekers.Commands.SetMatchSummaryMail;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobSeekers.Commands.SetMatchSummaryMail;

// ADR 0164 (#2107) — the summary mail switch. Same owner-scoped audited shape as the consent handlers;
// SaveChanges is UnitOfWorkBehavior's, so assertions read the tracked entity.
public class SetMatchSummaryMailCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    private static readonly FakeDateTimeProvider ClockT0 =
        new(new DateTimeOffset(2026, 10, 11, 8, 0, 0, TimeSpan.Zero));
    private static readonly FakeDateTimeProvider ClockT1 =
        new(new DateTimeOffset(2026, 10, 11, 9, 0, 0, TimeSpan.Zero));
    private static readonly FakeDateTimeProvider ClockT2 =
        new(new DateTimeOffset(2026, 10, 11, 10, 0, 0, TimeSpan.Zero));

    public SetMatchSummaryMailCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static async Task<JobSeeker> SeedSeekerAsync(AppDbContext db, Guid userId)
    {
        var seeker = JobSeeker.Register(userId, TermsAcceptance.AcceptCurrent(ClockT0), ClockT0).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);
        return seeker;
    }

    private SetMatchSummaryMailCommandHandler HandlerWith(AppDbContext db, IDateTimeProvider clock) =>
        new(db, _currentUser, clock);

    private Preferences StoredPreferences(AppDbContext db) =>
        db.JobSeekers.Single(js => js.UserId == _userId).Preferences;

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ReturnsUnauthorizedValidationFailure()
    {
        var db = TestAppDbContextFactory.Create();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns((Guid?)null);
        var handler = new SetMatchSummaryMailCommandHandler(db, currentUser, ClockT0);

        var result = await handler.Handle(new SetMatchSummaryMailCommand(Enabled: false), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.Unauthorized");
    }

    [Fact]
    public async Task Handle_WhenNoJobSeekerForUser_ReturnsNotFound()
    {
        var db = TestAppDbContextFactory.Create();

        var result = await HandlerWith(db, ClockT0).Handle(
            new SetMatchSummaryMailCommand(Enabled: false), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.NotFound");
    }

    // The other switches are set first, so the assertion sees that only this switch's stamp moved.
    [Fact]
    public async Task Handle_WhenSwitchingOff_StampsOnlyTheSummaryPause_AndEchoesJobSeekerId()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);
        seeker.UpdateNotificationConsent(enabled: true, ClockT0);
        seeker.SetTopMatchMail(on: false, ClockT0);
        seeker.SetDigestCadence(DigestCadence.Daily, ClockT0);
        await db.SaveChangesAsync(CancellationToken.None);
        var before = StoredPreferences(db);

        var result = await HandlerWith(db, ClockT1).Handle(
            new SetMatchSummaryMailCommand(Enabled: false), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(seeker.Id.Value);
        StoredPreferences(db).ShouldBe(before with { MatchSummaryMailOffAt = ClockT1.UtcNow });
    }

    [Fact]
    public async Task Handle_WhenSwitchingOnAfterOff_ClearsOnlyTheSummaryPause_AndEchoesJobSeekerId()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = await SeedSeekerAsync(db, _userId);
        seeker.UpdateNotificationConsent(enabled: true, ClockT0);
        seeker.SetNotificationMail(on: false, ClockT0);
        seeker.SetMatchSummaryMail(on: false, ClockT1);
        await db.SaveChangesAsync(CancellationToken.None);
        var before = StoredPreferences(db);

        var result = await HandlerWith(db, ClockT2).Handle(
            new SetMatchSummaryMailCommand(Enabled: true), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(seeker.Id.Value);
        StoredPreferences(db).ShouldBe(before with { MatchSummaryMailOffAt = null });
    }

    [Theory]
    [InlineData(true, "{\"enabled\":true}")]
    [InlineData(false, "{\"enabled\":false}")]
    public void BuildAuditPayload_IsExactlyTheRequestedEnabled(bool enabled, string expected)
    {
        var payload = new SetMatchSummaryMailCommand(Enabled: enabled).BuildAuditPayload(
            Result.Success(Guid.NewGuid()), Substitute.For<IIdentifierPseudonymizer>());

        payload.ShouldBe(expected);
    }

    [Fact]
    public void Command_IsAuditedAsMatchSummaryMailUpdatedOnTheJobSeeker()
    {
        var command = new SetMatchSummaryMailCommand(Enabled: false);

        command.EventType.ShouldBe("JobSeeker.MatchSummaryMailUpdated");
        command.AggregateType.ShouldBe("JobSeeker");
    }

    [Fact]
    public async Task Handle_IsOwnerScoped_DoesNotTouchOtherUsersSwitch()
    {
        var db = TestAppDbContextFactory.Create();
        var ownSeeker = await SeedSeekerAsync(db, _userId);
        var otherUserId = Guid.NewGuid();
        var otherSeeker = await SeedSeekerAsync(db, otherUserId);

        var result = await HandlerWith(db, ClockT1).Handle(
            new SetMatchSummaryMailCommand(Enabled: false), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(ownSeeker.Id.Value);
        result.Value.ShouldNotBe(otherSeeker.Id.Value);
        db.JobSeekers.Single(js => js.UserId == otherUserId).Preferences.MatchSummaryMailOffAt.ShouldBeNull();
    }
}

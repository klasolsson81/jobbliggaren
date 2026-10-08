using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.Commands.DeleteAccount;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth.Commands.DeleteAccount;

public sealed class AccountDeletionSchedulerTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ScheduleAsync_ShouldReturnThePersistedDeletionReceipt_WhenTheAccountIsLive(
        bool administratorInitiated, bool suspended)
    {
        await using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var profile = Register(userId);
        db.JobSeekers.Add(profile);
        await db.SaveChangesAsync(Ct);
        var reader = Reader(db, userId, suspended);
        var writer = Writer(userId, suspended);
        var eraser = Substitute.For<IExternalLoginEraser>();
        var scheduler = new AccountDeletionScheduler(db, Clock, reader, writer, eraser);

        var result = await scheduler.ScheduleAsync(userId, administratorInitiated, Ct);
        await db.SaveChangesAsync(Ct);

        result.IsSuccess.ShouldBeTrue();
        var persisted = await db.JobSeekers.IgnoreQueryFilters().AsNoTracking().SingleAsync(Ct);
        result.Value.UserId.ShouldBe(userId);
        result.Value.ProfileId.ShouldBe(profile.Id.Value);
        result.Value.DeletedAt.ShouldBe(persisted.DeletedAt.ShouldNotBeNull());
        result.Value.EligibleAt.ShouldBe(new DateTimeOffset(2026, 5, 19, 12, 0, 0, TimeSpan.Zero));
        result.Value.ScheduledRunAt.ShouldBe(new DateTimeOffset(2026, 5, 20, 4, 0, 0, TimeSpan.Zero));
        result.Value.IsSuspended.ShouldBe(suspended);
        result.Value.AccessRevision.ShouldBe(1);
        await writer.Received(1).CanRemoveAccessAsync(userId, Ct);
        await writer.Received(1).AdvanceDeletionAsync(userId, Ct);
        await writer.DidNotReceiveWithAnyArgs().AdvanceCredentialsAsync(default, Ct);
        await writer.DidNotReceiveWithAnyArgs().ChangeAsync(default, default, default, Ct);
        await eraser.Received(1).EraseAllAsync(userId, Ct);
    }

    [Fact]
    public async Task ScheduleAsync_ShouldUseTheAggregateDeletionInstant_WhenTheClockAdvancesDuringScheduling()
    {
        await using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var profile = Register(userId);
        db.JobSeekers.Add(profile);
        await db.SaveChangesAsync(Ct);
        var clock = new AdvancingFakeDateTimeProvider(
            new DateTimeOffset(2026, 10, 8, 3, 59, 59, TimeSpan.Zero), TimeSpan.FromSeconds(2));
        var scheduler = new AccountDeletionScheduler(db, clock, Reader(db, userId), Writer(userId),
            Substitute.For<IExternalLoginEraser>());

        var result = await scheduler.ScheduleAsync(userId, true, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.DeletedAt.ShouldBe(profile.DeletedAt.ShouldNotBeNull());
        result.Value.EligibleAt.ShouldBe(profile.DeletedAt.Value.AddDays(30));
    }

    [Fact]
    public async Task ScheduleAsync_ShouldUseOneMillisecondInstantForTheEntireCascade_WhenTheClockHasSubMillisecondPrecision()
    {
        await using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var profile = Register(userId);
        var application = DomainApplication.Create(profile.Id, null, "A cover letter", null, Clock).Value;
        application.AddFollowUp(FollowUpChannel.Email, Clock.UtcNow.AddDays(3), "Follow up", Clock).IsSuccess.ShouldBeTrue();
        application.AddNote("A private note", Clock).IsSuccess.ShouldBeTrue();
        var resume = Resume.Create(profile.Id, "Target CV", "Target person", Clock).Value;
        db.JobSeekers.Add(profile);
        db.Applications.Add(application);
        db.Resumes.Add(resume);
        await db.SaveChangesAsync(Ct);
        db.ChangeTracker.Clear();
        var clock = new PreciseAdvancingClock(new DateTimeOffset(2026, 10, 8, 3, 59, 59, TimeSpan.Zero).AddTicks(9_999_999));
        var expected = new DateTimeOffset(2026, 10, 8, 3, 59, 59, 999, TimeSpan.Zero);

        var result = await new AccountDeletionScheduler(db, clock, Reader(db, userId), Writer(userId),
            Substitute.For<IExternalLoginEraser>()).ScheduleAsync(userId, true, Ct);

        result.IsSuccess.ShouldBeTrue();
        clock.Reads.ShouldBe(1);
        result.Value.DeletedAt.ShouldBe(expected);
        result.Value.EligibleAt.ShouldBe(new DateTimeOffset(2026, 11, 7, 3, 59, 59, 999, TimeSpan.Zero));
        result.Value.ScheduledRunAt.ShouldBe(new DateTimeOffset(2026, 11, 7, 4, 0, 0, TimeSpan.Zero));
        var deletedProfile = await db.JobSeekers.IgnoreQueryFilters().SingleAsync(Ct);
        var deletedApplication = await db.Applications.IgnoreQueryFilters().Include(value => value.FollowUps)
            .Include(value => value.Notes).SingleAsync(Ct);
        var deletedResume = await db.Resumes.IgnoreQueryFilters().Include(value => value.Versions).SingleAsync(Ct);
        deletedProfile.DeletedAt.ShouldBe(expected);
        deletedApplication.DeletedAt.ShouldBe(expected);
        deletedApplication.FollowUps.ShouldHaveSingleItem().DeletedAt.ShouldBe(expected);
        deletedApplication.Notes.ShouldHaveSingleItem().DeletedAt.ShouldBe(expected);
        deletedResume.DeletedAt.ShouldBe(expected);
        deletedResume.Versions.ShouldHaveSingleItem().DeletedAt.ShouldBe(expected);
        deletedProfile.DomainEvents.ShouldHaveSingleItem().OccurredAt.ShouldBe(expected);
        deletedApplication.DomainEvents.ShouldHaveSingleItem().OccurredAt.ShouldBe(expected);
        deletedResume.DomainEvents.ShouldHaveSingleItem().OccurredAt.ShouldBe(expected);
        await db.SaveChangesAsync(Ct);
        db.ChangeTracker.Clear();
        (await db.JobSeekers.IgnoreQueryFilters().SingleAsync(Ct)).DeletedAt.ShouldBe(result.Value.DeletedAt);
    }

    [Fact]
    public async Task ScheduleAsync_ShouldCascadeOnlyTheTargetTree_WhenAnAdministratorSchedulesDeletion()
    {
        await using var db = TestAppDbContextFactory.Create();
        var targetId = Guid.NewGuid();
        var target = Register(targetId);
        var other = Register(Guid.NewGuid());
        var application = DomainApplication.Create(target.Id, null, "A cover letter", null, Clock).Value;
        application.AddFollowUp(FollowUpChannel.Email, Clock.UtcNow.AddDays(3), "Follow up", Clock).IsSuccess.ShouldBeTrue();
        application.AddNote("A private note", Clock).IsSuccess.ShouldBeTrue();
        var otherApplication = DomainApplication.Create(other.Id, null, null, null, Clock).Value;
        var resume = Resume.Create(target.Id, "Target CV", "Target person", Clock).Value;
        var otherResume = Resume.Create(other.Id, "Other CV", "Other person", Clock).Value;
        db.JobSeekers.AddRange(target, other);
        db.Applications.AddRange(application, otherApplication);
        db.Resumes.AddRange(resume, otherResume);
        await db.SaveChangesAsync(Ct);
        db.ChangeTracker.Clear();
        var eraser = Substitute.For<IExternalLoginEraser>();
        var scheduler = new AccountDeletionScheduler(db, Clock, Reader(db, targetId), Writer(targetId), eraser);

        var result = await scheduler.ScheduleAsync(targetId, true, Ct);
        await db.SaveChangesAsync(Ct);
        db.ChangeTracker.Clear();

        result.IsSuccess.ShouldBeTrue();
        var deletedApplication = await db.Applications.IgnoreQueryFilters().Include(value => value.FollowUps)
            .Include(value => value.Notes).SingleAsync(value => value.Id == application.Id, Ct);
        deletedApplication.DeletedAt.ShouldNotBeNull();
        deletedApplication.FollowUps.ShouldHaveSingleItem().DeletedAt.ShouldNotBeNull();
        deletedApplication.Notes.ShouldHaveSingleItem().DeletedAt.ShouldNotBeNull();
        var deletedResume = await db.Resumes.IgnoreQueryFilters().Include(value => value.Versions)
            .SingleAsync(value => value.Id == resume.Id, Ct);
        deletedResume.DeletedAt.ShouldNotBeNull();
        deletedResume.Versions.ShouldHaveSingleItem().DeletedAt.ShouldNotBeNull();
        (await db.JobSeekers.SingleAsync(value => value.Id == other.Id, Ct)).DeletedAt.ShouldBeNull();
        (await db.Applications.SingleAsync(value => value.Id == otherApplication.Id, Ct)).DeletedAt.ShouldBeNull();
        (await db.Resumes.Include(value => value.Versions).SingleAsync(value => value.Id == otherResume.Id, Ct))
            .Versions.ShouldHaveSingleItem().DeletedAt.ShouldBeNull();
        await eraser.Received(1).EraseAllAsync(targetId, Ct);
        await eraser.DidNotReceive().EraseAllAsync(other.UserId, Ct);
    }

    [Fact]
    public async Task ScheduleAsync_ShouldReturnNotFoundWithoutMutation_WhenTheAdminTargetAccountIsAbsent()
    {
        await using var db = TestAppDbContextFactory.Create();
        var targetId = Guid.NewGuid();
        var reader = AccountAccessTestKit.Reader(_ => null);
        var writer = Writer(targetId);
        var eraser = Substitute.For<IExternalLoginEraser>();

        var result = await new AccountDeletionScheduler(db, Clock, reader, writer, eraser)
            .ScheduleAsync(targetId, true, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Admin.AccountNotFound");
        result.Error.Kind.ShouldBe(ErrorKind.NotFound);
        await AssertNoMutationAsync(writer, eraser);
    }

    [Fact]
    public async Task ScheduleAsync_ShouldDegradeSafelyWithoutMutation_WhenAnAdminTargetProfileInvariantBreaks()
    {
        await using var db = TestAppDbContextFactory.Create();
        var targetId = Guid.NewGuid();
        // Unreachable invariant break: an Identity row without the profile registration creates.
        // SqlAccountAccess.ReadAsync's LEFT JOIN exposes HasProfile=false; only safe refusal is asserted.
        var reader = AccountAccessTestKit.Reader(id => id == targetId
            ? AccountAccessTestKit.Account(targetId, "target@example.test") with { HasProfile = false }
            : null);
        var writer = Writer(targetId);
        var eraser = Substitute.For<IExternalLoginEraser>();

        var result = await new AccountDeletionScheduler(db, Clock, reader, writer, eraser)
            .ScheduleAsync(targetId, true, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Admin.ProfileUnavailable");
        result.Error.Kind.ShouldBe(ErrorKind.Gone);
        await AssertNoMutationAsync(writer, eraser);
    }

    [Theory]
    [InlineData(false, ErrorKind.Gone)]
    [InlineData(true, ErrorKind.Conflict)]
    public async Task ScheduleAsync_ShouldPreserveTheOriginalDeletionWithoutMutation_WhenAlreadyPending(
        bool administratorInitiated, ErrorKind expectedKind)
    {
        await using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var profile = Register(userId);
        profile.SoftDelete(Clock);
        var deletedAt = profile.DeletedAt.ShouldNotBeNull();
        db.JobSeekers.Add(profile);
        await db.SaveChangesAsync(Ct);
        var writer = Writer(userId);
        var eraser = Substitute.For<IExternalLoginEraser>();
        var laterClock = new FakeDateTimeProvider(Clock.UtcNow.AddDays(1));

        var result = await new AccountDeletionScheduler(db, laterClock, Reader(db, userId), writer, eraser)
            .ScheduleAsync(userId, administratorInitiated, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(expectedKind);
        if (administratorInitiated)
            result.Error.Code.ShouldBe("Admin.AccountAlreadyPendingDeletion");
        profile.DeletedAt.ShouldBe(deletedAt);
        await AssertNoMutationAsync(writer, eraser);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScheduleAsync_ShouldProtectTheLastEffectiveAdminWithoutMutation_WhenRemovalIsRefused(
        bool administratorInitiated)
    {
        await using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var profile = Register(userId);
        db.JobSeekers.Add(profile);
        await db.SaveChangesAsync(Ct);
        var reader = Reader(db, userId, administrator: true);
        var writer = Writer(userId);
        writer.CanRemoveAccessAsync(userId, Ct).Returns(false);
        var eraser = Substitute.For<IExternalLoginEraser>();

        var result = await new AccountDeletionScheduler(db, Clock, reader, writer, eraser)
            .ScheduleAsync(userId, administratorInitiated, Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AccountAccessErrors.LastAdministrator);
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        profile.DeletedAt.ShouldBeNull();
        await writer.Received(1).CanRemoveAccessAsync(userId, Ct);
        await AssertNoMutationAsync(writer, eraser);
    }

    [Fact]
    public async Task ScheduleAsync_ShouldPropagateTheAccessFaultBeforeCascade_WhenCredentialAdvancementFails()
    {
        await using var db = TestAppDbContextFactory.Create();
        var userId = Guid.NewGuid();
        var profile = Register(userId);
        db.JobSeekers.Add(profile);
        await db.SaveChangesAsync(Ct);
        var writer = Writer(userId);
        var fault = new InvalidOperationException("access-transition-fault");
        writer.AdvanceDeletionAsync(userId, Ct).ThrowsAsync(fault);
        var eraser = Substitute.For<IExternalLoginEraser>();
        var scheduler = new AccountDeletionScheduler(db, Clock, Reader(db, userId), writer, eraser);

        var actual = await Should.ThrowAsync<InvalidOperationException>(() => scheduler.ScheduleAsync(userId, true, Ct));

        actual.ShouldBeSameAs(fault);
        profile.DeletedAt.ShouldBeNull();
        await eraser.DidNotReceiveWithAnyArgs().EraseAllAsync(default, Ct);
    }

    private static JobSeeker Register(Guid userId) =>
        JobSeeker.Register(userId, TermsAcceptance.AcceptCurrent(Clock), Clock).Value;

    private static IAccountAccessReader Reader(
        AppDbContext db, Guid userId, bool suspended = false, bool administrator = false)
    {
        var reader = AccountAccessTestKit.ReaderFromProfiles(db, id => id == userId ? "target@example.test" : null);
        reader.ReadAsync(userId, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var profile = db.JobSeekers.IgnoreQueryFilters().AsNoTracking().Single(value => value.UserId == userId);
            return AccountAccessTestKit.Account(userId, "target@example.test") with
            {
                IsSuspended = suspended,
                IsAdmin = administrator,
                DeletedAt = profile.DeletedAt,
            };
        });
        return reader;
    }

    private static IAccountAccessWriter Writer(Guid userId, bool suspended = false)
    {
        var writer = Substitute.For<IAccountAccessWriter>();
        writer.CanRemoveAccessAsync(userId, Arg.Any<CancellationToken>()).Returns(true);
        writer.AdvanceDeletionAsync(userId, Arg.Any<CancellationToken>())
            .Returns(AccountAccessTestKit.Account(userId, "target@example.test") with
            {
                IsSuspended = suspended,
                AccessRevision = 1,
                CredentialCutoff = 1,
            });
        return writer;
    }

    private static async Task AssertNoMutationAsync(IAccountAccessWriter writer, IExternalLoginEraser eraser)
    {
        await writer.DidNotReceiveWithAnyArgs().AdvanceDeletionAsync(default, Ct);
        await writer.DidNotReceiveWithAnyArgs().AdvanceCredentialsAsync(default, Ct);
        await writer.DidNotReceiveWithAnyArgs().ChangeAsync(default, default, default, Ct);
        await eraser.DidNotReceiveWithAnyArgs().EraseAllAsync(default, Ct);
    }

    private sealed class PreciseAdvancingClock(DateTimeOffset first) : IDateTimeProvider
    {
        public int Reads { get; private set; }

        public DateTimeOffset UtcNow => first.AddSeconds(2 * Reads++);
    }
}

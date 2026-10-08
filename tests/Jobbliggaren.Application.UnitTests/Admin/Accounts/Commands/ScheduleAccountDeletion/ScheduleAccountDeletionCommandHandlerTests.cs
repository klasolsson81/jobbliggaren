using Jobbliggaren.Application.Admin.Accounts.Commands.ScheduleAccountDeletion;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.Commands.DeleteAccount;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.UnitTests.Auth;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Accounts.Commands.ScheduleAccountDeletion;

public sealed class ScheduleAccountDeletionCommandHandlerTests
{
    private const string Grant = "an-opaque-reauth-grant"; // gitleaks:allow
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Handle_ShouldScheduleOnlyTheTarget_WhenTheAdministratorIsAnotherAccount()
    {
        await using var db = TestAppDbContextFactory.Create();
        var actor = Register(Guid.NewGuid());
        var target = Register(Guid.NewGuid());
        db.JobSeekers.AddRange(actor, target);
        await db.SaveChangesAsync(Ct);
        var user = CurrentUser(actor.UserId);
        var writer = Writer(target.UserId);
        var eraser = Substitute.For<IExternalLoginEraser>();
        var handler = Handler(db, user, writer, eraser);

        var result = await handler.Handle(new ScheduleAccountDeletionCommand(target.UserId, Grant), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.UserId.ShouldBe(target.UserId);
        result.Value.ProfileId.ShouldBe(target.Id.Value);
        target.DeletedAt.ShouldNotBeNull();
        actor.DeletedAt.ShouldBeNull();
        await writer.Received(1).AdvanceDeletionAsync(target.UserId, Ct);
        await writer.DidNotReceive().AdvanceDeletionAsync(actor.UserId, Ct);
        await eraser.Received(1).EraseAllAsync(target.UserId, Ct);
    }

    [Fact]
    public async Task Handle_ShouldRefuseBeforeTheScheduler_WhenTheActorIsNotAuthenticated()
    {
        await using var db = TestAppDbContextFactory.Create();
        var writer = Substitute.For<IAccountAccessWriter>();
        var eraser = Substitute.For<IExternalLoginEraser>();
        var reader = Substitute.For<IAccountAccessReader>();
        var handler = new ScheduleAccountDeletionCommandHandler(
            new AccountDeletionScheduler(db, Clock, reader, writer, eraser), CurrentUser(null));

        var result = await handler.Handle(new ScheduleAccountDeletionCommand(Guid.NewGuid(), Grant), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Auth.NotAuthenticated");
        await reader.DidNotReceiveWithAnyArgs().ReadAsync(default, Ct);
        await writer.DidNotReceiveWithAnyArgs().AdvanceDeletionAsync(default, Ct);
        await eraser.DidNotReceiveWithAnyArgs().EraseAllAsync(default, Ct);
    }

    [Fact]
    public async Task Handle_ShouldRefuseWithoutMutation_WhenTheAdministratorTargetsThemselves()
    {
        await using var db = TestAppDbContextFactory.Create();
        var actorId = Guid.NewGuid();
        var writer = Substitute.For<IAccountAccessWriter>();
        var eraser = Substitute.For<IExternalLoginEraser>();
        var reader = Substitute.For<IAccountAccessReader>();
        var handler = new ScheduleAccountDeletionCommandHandler(
            new AccountDeletionScheduler(db, Clock, reader, writer, eraser), CurrentUser(actorId));

        var result = await handler.Handle(new ScheduleAccountDeletionCommand(actorId, Grant), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Admin.SelfDeletion");
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        await reader.DidNotReceiveWithAnyArgs().ReadAsync(default, Ct);
        await writer.DidNotReceiveWithAnyArgs().AdvanceDeletionAsync(default, Ct);
        await eraser.DidNotReceiveWithAnyArgs().EraseAllAsync(default, Ct);
    }

    [Fact]
    public async Task Handle_ShouldPreserveTheAdminConflict_WhenTheTargetIsAlreadyPendingDeletion()
    {
        await using var db = TestAppDbContextFactory.Create();
        var target = Register(Guid.NewGuid());
        target.SoftDelete(Clock);
        var deletedAt = target.DeletedAt.ShouldNotBeNull();
        db.JobSeekers.Add(target);
        await db.SaveChangesAsync(Ct);
        var writer = Writer(target.UserId);
        var eraser = Substitute.For<IExternalLoginEraser>();
        var handler = Handler(db, CurrentUser(Guid.NewGuid()), writer, eraser);

        var result = await handler.Handle(new ScheduleAccountDeletionCommand(target.UserId, Grant), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Admin.AccountAlreadyPendingDeletion");
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        target.DeletedAt.ShouldBe(deletedAt);
        await writer.DidNotReceiveWithAnyArgs().AdvanceDeletionAsync(default, Ct);
        await eraser.DidNotReceiveWithAnyArgs().EraseAllAsync(default, Ct);
    }

    private static ScheduleAccountDeletionCommandHandler Handler(
        AppDbContext db, ICurrentUser user, IAccountAccessWriter writer, IExternalLoginEraser eraser)
    {
        var reader = AccountAccessTestKit.ReaderFromProfiles(db,
            id => db.JobSeekers.IgnoreQueryFilters().Any(profile => profile.UserId == id) ? "target@example.test" : null);
        return new ScheduleAccountDeletionCommandHandler(new AccountDeletionScheduler(db, Clock, reader, writer, eraser), user);
    }

    private static ICurrentUser CurrentUser(Guid? userId)
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(userId);
        return user;
    }

    private static IAccountAccessWriter Writer(Guid targetId)
    {
        var writer = Substitute.For<IAccountAccessWriter>();
        writer.CanRemoveAccessAsync(targetId, Arg.Any<CancellationToken>()).Returns(true);
        writer.AdvanceDeletionAsync(targetId, Arg.Any<CancellationToken>())
            .Returns(AccountAccessTestKit.Account(targetId, "target@example.test") with { AccessRevision = 1, CredentialCutoff = 1 });
        return writer;
    }

    private static JobSeeker Register(Guid userId) =>
        JobSeeker.Register(userId, TermsAcceptance.AcceptCurrent(Clock), Clock).Value;
}

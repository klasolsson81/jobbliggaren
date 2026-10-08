using Jobbliggaren.Application.Admin.Accounts.Commands.ScheduleAccountDeletion;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.Commands.DeleteAccount;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Accounts.Commands.ScheduleAccountDeletion;

public sealed class ScheduleAccountDeletionCommandTests
{
    private const string Grant = "a-sensitive-one-use-reauth-grant"; // gitleaks:allow

    [Fact]
    public void Command_ShouldRequireAdminStepUpAndProtectedMutation_WhenConstructed()
    {
        var targetId = Guid.NewGuid();
        var command = new ScheduleAccountDeletionCommand(targetId, Grant);

        command.ShouldBeAssignableTo<IAdminRequest>();
        ((IReauthenticatingRequest)command).ReauthGrant.ShouldBe(Grant);
        ((IAccountAccessMutation)command).TargetUserId.ShouldBe(targetId);
        command.EventType.ShouldBe("Admin.AccountDeletionScheduled");
        command.AggregateType.ShouldBe("User");
        ((IAuditableCommand)command).AuditFailures.ShouldBeFalse();
        command.GetType().IsAssignableTo(typeof(IReplayOnConcurrencyConflict)).ShouldBeFalse();
        command.GetType().IsAssignableTo(typeof(IRequiresFieldEncryptionKey)).ShouldBeFalse();
        command.GetType().IsAssignableTo(typeof(IAuditPayloadCommand<Result<AccountDeletionScheduled>>)).ShouldBeFalse();
    }

    [Fact]
    public void ExtractAggregateId_ShouldAuditTheIdentityTarget_WhenSchedulingSucceeds()
    {
        var targetId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var deletedAt = FakeDateTimeProvider.Default.UtcNow;
        var receipt = new AccountDeletionScheduled(targetId, profileId, deletedAt, deletedAt.AddDays(30),
            new DateTimeOffset(2026, 5, 20, 4, 0, 0, TimeSpan.Zero), false, 1);

        new ScheduleAccountDeletionCommand(targetId, Grant).ExtractAggregateId(Result.Success(receipt))
            .ShouldBe(targetId);
        new DeleteAccountCommand(Grant).ExtractAggregateId(Result.Success(receipt)).ShouldBe(profileId);
    }

    [Fact]
    public void ToString_ShouldRedactTheGrant_WhenTheCommandIsPrinted()
    {
        var targetId = Guid.NewGuid();
        var printed = new ScheduleAccountDeletionCommand(targetId, Grant).ToString().ShouldNotBeNull();

        printed.ShouldNotContain(Grant);
        printed.ShouldContain(targetId.ToString());
    }
}

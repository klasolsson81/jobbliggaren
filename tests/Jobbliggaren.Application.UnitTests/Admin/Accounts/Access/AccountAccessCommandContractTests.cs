using Jobbliggaren.Application.Admin.Accounts.Commands.ReinstateAccount;
using Jobbliggaren.Application.Admin.Accounts.Commands.SuspendAccount;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Accounts.Access;

public sealed class AccountAccessCommandContractTests
{
    private const string Grant = "a-sensitive-one-use-reauth-grant"; // gitleaks:allow

    [Theory]
    [InlineData(true, "Admin.AccountSuspended")]
    [InlineData(false, "Admin.AccountReinstated")]
    public void Command_BothTransitions_RequireAdminStepUpAndLockedMutation(bool suspend, string eventType)
    {
        var targetId = Guid.NewGuid();
        var command = Command(suspend, targetId);

        command.ShouldBeAssignableTo<IAdminRequest>();
        ((IReauthenticatingRequest)command).ReauthGrant.ShouldBe(Grant);
        ((IAccountAccessMutation)command).TargetUserId.ShouldBe(targetId);
        command.EventType.ShouldBe(eventType);
        command.AggregateType.ShouldBe("User");
        command.AuditFailures.ShouldBeFalse();
        (command is IAuditPayloadCommand<Result<AccountAccessChanged>>).ShouldBeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExtractAggregateId_Success_IdentifiesTheTargetAccount(bool suspend)
    {
        var targetId = Guid.NewGuid();
        var command = Command(suspend, targetId);
        var response = Result.Success(new AccountAccessChanged(targetId, suspend, suspend ? 1 : 2, PendingDeletion: false));

        command.ExtractAggregateId(response).ShouldBe(targetId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ToString_SensitiveGrant_RedactsTheGrant(bool suspend)
    {
        var targetId = Guid.NewGuid();
        var printed = Command(suspend, targetId).ToString().ShouldNotBeNull();

        printed.ShouldNotContain(Grant);
        printed.ShouldContain(targetId.ToString());
    }

    private static IAuditableCommand<Result<AccountAccessChanged>> Command(bool suspend, Guid targetId) =>
        suspend ? new SuspendAccountCommand(targetId, Grant) : new ReinstateAccountCommand(targetId, Grant);
}

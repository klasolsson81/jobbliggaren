using Jobbliggaren.Application.Admin.Accounts.Commands.RequestAccountEmailChange;
using Jobbliggaren.Application.Auth.Commands.ChangeEmail;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Behaviors;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Behaviors;

public sealed class AccountEmailChangeOwnedTransactionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_ShouldLeaveSelfRequestAuditAndSaveToTheHandler_WhenItOwnsTheTransaction(bool succeeds)
    {
        var userId = Guid.NewGuid();
        var response = succeeds
            ? Result.Success(new EmailChangeChallenge(userId, ChallengeId.Generate()))
            : Result.Failure<EmailChangeChallenge>(DomainError.Conflict("Test.Refused", "Refused."));
        await AssertBypassAsync(new ChangeEmailCommand("an-opaque-grant", "new@example.com"), response);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_ShouldLeaveAdministratorRequestAuditAndSaveToTheHandler_WhenItOwnsTheTransaction(bool succeeds)
    {
        var from = FakeDateTimeProvider.Default.UtcNow.AddHours(72);
        var response = succeeds
            ? Result.Success(new AccountEmailChangePending(from, from.AddHours(24)))
            : Result.Failure<AccountEmailChangePending>(DomainError.Conflict("Test.Refused", "Refused."));
        await AssertBypassAsync(new RequestAccountEmailChangeCommand(Guid.NewGuid(), "new@example.com", "an-opaque-grant"), response);
    }

    private static async Task AssertBypassAsync<TCommand, TValue>(TCommand command, Result<TValue> response)
        where TCommand : ICommand<Result<TValue>>
    {
        // This is a pipeline contract test: the delegate stands for the handler that already owns
        // staging and activation. Neither outer behavior may add a second audit or save its tracker.
        var db = Substitute.For<IAppDbContext>();
        var audit = new AuditBehavior<TCommand, Result<TValue>>(db, Substitute.For<ICurrentUser>(),
            FakeDateTimeProvider.Default, Substitute.For<ICorrelationIdProvider>(),
            Substitute.For<IRequestContextProvider>(), Substitute.For<IIdentifierPseudonymizer>());
        var unit = new UnitOfWorkBehavior<TCommand, Result<TValue>>(db,
            NullLogger<UnitOfWorkBehavior<TCommand, Result<TValue>>>.Instance);
        var invocations = 0;
        MessageHandlerDelegate<TCommand, Result<TValue>> next = (_, _) =>
        {
            invocations++;
            return ValueTask.FromResult(response);
        };

        var actual = await unit.Handle(command, (message, ct) => audit.Handle(message, next, ct), Ct);

        actual.ShouldBe(response);
        invocations.ShouldBe(1);
        db.ReceivedCalls().ShouldNotContain(call => call.GetMethodInfo().Name == "get_AuditLogEntries");
        await db.DidNotReceiveWithAnyArgs().SaveChangesAsync(Ct);
        db.DidNotReceive().ClearTracking();
    }
}

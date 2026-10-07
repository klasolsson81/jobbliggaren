using Jobbliggaren.Application.Admin.Accounts.Commands.SuspendAccount;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Accounts.Access;

public sealed class SuspendAccountCommandHandlerTests
{
    private const string Grant = "an-opaque-reauth-grant"; // gitleaks:allow
    private readonly Guid _actorId = Guid.NewGuid();
    private readonly Guid _targetId = Guid.NewGuid();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IAccountAccessWriter _writer = Substitute.For<IAccountAccessWriter>();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Handle_AuthenticatedActor_ReturnsTheSuspensionReceipt()
    {
        _currentUser.UserId.Returns(_actorId);
        var receipt = new AccountAccessChanged(_targetId, IsSuspended: true, AccessRevision: 1, PendingDeletion: false);
        _writer.ChangeAsync(_actorId, _targetId, true, Ct).Returns(Result.Success(receipt));

        var result = await new SuspendAccountCommandHandler(_currentUser, _writer)
            .Handle(new SuspendAccountCommand(_targetId, Grant), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeSameAs(receipt);
        await _writer.Received(1).ChangeAsync(_actorId, _targetId, true, Ct);
        await _writer.DidNotReceiveWithAnyArgs().CanRemoveAccessAsync(default, Ct);
    }

    [Fact]
    public async Task Handle_MissingActor_RefusesBeforeTheWriter()
    {
        _currentUser.UserId.Returns((Guid?)null);

        var result = await new SuspendAccountCommandHandler(_currentUser, _writer)
            .Handle(new SuspendAccountCommand(_targetId, Grant), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Auth.NotAuthenticated");
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        await _writer.DidNotReceiveWithAnyArgs().ChangeAsync(default, default, default, Ct);
    }

    [Theory]
    [InlineData(AccountAccessErrors.AlreadySuspended)]
    [InlineData(AccountAccessErrors.SelfSuspension)]
    public async Task Handle_WriterRefusal_PreservesTheConflict(string code)
    {
        _currentUser.UserId.Returns(_actorId);
        var targetId = code == AccountAccessErrors.SelfSuspension ? _actorId : _targetId;
        var refusal = DomainError.Conflict(code, "Åtgärden nekades.");
        _writer.ChangeAsync(_actorId, targetId, true, Ct)
            .Returns(Result.Failure<AccountAccessChanged>(refusal));

        var result = await new SuspendAccountCommandHandler(_currentUser, _writer)
            .Handle(new SuspendAccountCommand(targetId, Grant), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeSameAs(refusal);
        await _writer.Received(1).ChangeAsync(_actorId, targetId, true, Ct);
    }

    [Fact]
    public async Task Handle_MissingIdentity_PreservesNotFound()
    {
        _currentUser.UserId.Returns(_actorId);
        var refusal = DomainError.NotFound(AccountAccessErrors.AccountNotFound, "Kontot saknas.");
        _writer.ChangeAsync(_actorId, _targetId, true, Ct)
            .Returns(Result.Failure<AccountAccessChanged>(refusal));

        var result = await new SuspendAccountCommandHandler(_currentUser, _writer)
            .Handle(new SuspendAccountCommand(_targetId, Grant), Ct);

        result.Error.ShouldBeSameAs(refusal);
        result.Error.Kind.ShouldBe(ErrorKind.NotFound);
    }
}

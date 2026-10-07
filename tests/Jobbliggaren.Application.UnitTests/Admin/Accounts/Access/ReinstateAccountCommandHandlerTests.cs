using Jobbliggaren.Application.Admin.Accounts.Commands.ReinstateAccount;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Accounts.Access;

public sealed class ReinstateAccountCommandHandlerTests
{
    private const string Grant = "an-opaque-reauth-grant"; // gitleaks:allow
    private readonly Guid _actorId = Guid.NewGuid();
    private readonly Guid _targetId = Guid.NewGuid();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IAccountAccessWriter _writer = Substitute.For<IAccountAccessWriter>();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_SuspendedAccount_ReturnsTheReceiptIncludingPendingDeletion(bool pendingDeletion)
    {
        _currentUser.UserId.Returns(_actorId);
        var receipt = new AccountAccessChanged(_targetId, IsSuspended: false, AccessRevision: 2, pendingDeletion);
        _writer.ChangeAsync(_actorId, _targetId, false, Ct).Returns(Result.Success(receipt));

        var result = await new ReinstateAccountCommandHandler(_currentUser, _writer)
            .Handle(new ReinstateAccountCommand(_targetId, Grant), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeSameAs(receipt);
        result.Value.PendingDeletion.ShouldBe(pendingDeletion);
        await _writer.Received(1).ChangeAsync(_actorId, _targetId, false, Ct);
    }

    [Fact]
    public async Task Handle_MissingActor_RefusesBeforeTheWriter()
    {
        _currentUser.UserId.Returns((Guid?)null);

        var result = await new ReinstateAccountCommandHandler(_currentUser, _writer)
            .Handle(new ReinstateAccountCommand(_targetId, Grant), Ct);

        result.Error.Code.ShouldBe("Auth.NotAuthenticated");
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        await _writer.DidNotReceiveWithAnyArgs().ChangeAsync(default, default, default, Ct);
    }

    [Fact]
    public async Task Handle_AlreadyReinstated_PreservesTheNoOpConflict()
    {
        _currentUser.UserId.Returns(_actorId);
        var refusal = DomainError.Conflict(AccountAccessErrors.AlreadyReinstated, "Kontot är redan återaktiverat.");
        _writer.ChangeAsync(_actorId, _targetId, false, Ct)
            .Returns(Result.Failure<AccountAccessChanged>(refusal));

        var result = await new ReinstateAccountCommandHandler(_currentUser, _writer)
            .Handle(new ReinstateAccountCommand(_targetId, Grant), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeSameAs(refusal);
        await _writer.Received(1).ChangeAsync(_actorId, _targetId, false, Ct);
    }

    [Fact]
    public async Task Handle_ProfileUnavailable_PreservesGone()
    {
        _currentUser.UserId.Returns(_actorId);
        var refusal = DomainError.Gone(AccountAccessErrors.ProfileUnavailable, "Profilen är inte längre tillgänglig.");
        _writer.ChangeAsync(_actorId, _targetId, false, Ct)
            .Returns(Result.Failure<AccountAccessChanged>(refusal));

        var result = await new ReinstateAccountCommandHandler(_currentUser, _writer)
            .Handle(new ReinstateAccountCommand(_targetId, Grant), Ct);

        result.Error.ShouldBeSameAs(refusal);
        result.Error.Kind.ShouldBe(ErrorKind.Gone);
    }
}

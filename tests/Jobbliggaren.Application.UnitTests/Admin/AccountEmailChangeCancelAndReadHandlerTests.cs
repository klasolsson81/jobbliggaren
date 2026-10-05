using Jobbliggaren.Application.Admin.Accounts.Commands.CancelAccountEmailChange;
using Jobbliggaren.Application.Admin.Accounts.Queries.GetPendingAccountEmailChange;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.AccountEmailChanges;
using Jobbliggaren.Domain.Common;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin;

/// <summary>
/// #1975 (ADR 0153) — the administrator's cancel and pending read. A cancel that removed nothing is a failure, which is
/// what keeps AuditBehavior from writing a row for a change that never happened; the read answers the store's two
/// instants and state, and nothing when nothing is pending.
/// </summary>
public sealed class AccountEmailChangeCancelAndReadHandlerTests
{
    private static readonly Guid TargetId = Guid.NewGuid();
    private static readonly DateTimeOffset CompletableFrom = new(2026, 10, 8, 12, 30, 0, TimeSpan.Zero);

    private readonly IAccountEmailChangeStore _store = Substitute.For<IAccountEmailChangeStore>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_cancel_that_removed_the_change_succeeds()
    {
        _store.CancelAsync(TargetId, Arg.Any<CancellationToken>()).Returns(true);

        var result = await new CancelAccountEmailChangeCommandHandler(_store)
            .Handle(new CancelAccountEmailChangeCommand(TargetId), Ct);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_cancel_that_found_nothing_is_gone_so_no_row_claims_a_change()
    {
        _store.CancelAsync(TargetId, Arg.Any<CancellationToken>()).Returns(false);

        var result = await new CancelAccountEmailChangeCommandHandler(_store)
            .Handle(new CancelAccountEmailChangeCommand(TargetId), Ct);

        result.Error.Code.ShouldBe(AuthErrorCodes.AccountEmailChangeNothingPending);
        result.Error.Kind.ShouldBe(ErrorKind.Gone);
    }

    [Fact]
    public async Task The_read_answers_the_state_and_both_instants()
    {
        _store.FindPendingAsync(TargetId, Arg.Any<CancellationToken>()).Returns(new PendingAccountEmailChange(
            PendingAccountEmailChangeState.CodeBurned, CompletableFrom, CompletableFrom.AddHours(24)));

        var pending = await new GetPendingAccountEmailChangeQueryHandler(_store)
            .Handle(new GetPendingAccountEmailChangeQuery(TargetId), Ct);

        pending.ShouldBe(new PendingAccountEmailChangeDto(
            PendingAccountEmailChangeState.CodeBurned, CompletableFrom, CompletableFrom.AddHours(24)));
    }

    [Fact]
    public async Task The_read_answers_nothing_when_nothing_is_pending()
    {
        _store.FindPendingAsync(TargetId, Arg.Any<CancellationToken>()).Returns((PendingAccountEmailChange?)null);

        (await new GetPendingAccountEmailChangeQueryHandler(_store)
            .Handle(new GetPendingAccountEmailChangeQuery(TargetId), Ct)).ShouldBeNull();
    }
}

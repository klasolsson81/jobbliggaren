using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.AccountEmailChanges;
using Jobbliggaren.Application.Auth.Commands.ConfirmEmailChange;
using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// The change-email confirm step (#679; a grant since #1739, ADR 0142 D5). The grant is redeemed with an assertion
/// of this user AND this address, and nothing is moved until it redeems. The swap answers the address it replaced,
/// so the "your email was changed" notice reaches the previous owner (CTO-bind #4), and that notice is
/// best-effort: it never fails a completed change.
/// </summary>
public sealed class ConfirmEmailChangeCommandHandlerTests : IAsyncDisposable
{
    private const string OldEmail = "gammal.adress@example.se";
    private const string NewEmail = "ny.adress@example.se";
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly GrantToken Grant = GrantToken.Generate();

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IGrantStore _grants = Substitute.For<IGrantStore>();
    private readonly IUserAccountService _accounts = Substitute.For<IUserAccountService>();
    private readonly IEmailSender _sender = Substitute.For<IEmailSender>();
    private readonly RecordingLogger<ConfirmedAddressSwap> _logger = new();
    private readonly ConfirmedAddressSwap _addressSwap;
    private readonly AccountAccessTestKit.RecordingAccountAccessCoordinator _coordinator = AccountAccessTestKit.Coordinator();
    private readonly IAccountAccessWriter _accessWriter;
    private AccountAccessSnapshot _account = AccountAccessTestKit.Account(UserId, OldEmail);
    private readonly AppDbContext _db = TestAppDbContextFactory.Create();
    private readonly FakeDateTimeProvider _clock = FakeDateTimeProvider.Default;
    private readonly IAccountEmailChangeRequests _requests;
    private readonly EmailChangeRequestProof _original = EmailChangeRequestTestKit.Original(
        ChallengeId.Generate(), FakeDateTimeProvider.Default.UtcNow);
    private bool _commitRequest = true;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static GrantAssertion Expected => GrantAssertion.Of(new GrantSubject.ChangeEmail(UserId, NewEmail));

    public ConfirmEmailChangeCommandHandlerTests()
    {
        _currentUser.UserId.Returns(UserId);
        _currentUser.AccessRevision.Returns(0L);
        _grants.RedeemAsync(Grant, Expected, Arg.Any<CancellationToken>())
            .Returns(new GrantSubject.ChangeEmail(UserId, NewEmail)
            { Access = AccountAccessTestKit.Bound(UserId), Request = _original });
        _requests = EmailChangeRequestTestKit.Reader(_db);
        _accounts.SwapConfirmedAddressAsync(UserId, NewEmail, SwapPrecondition.None, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _account = _account with { Email = NewEmail };
                return Result.Success(new AddressSwapped(OldEmail));
            });
        _accessWriter = AccountAccessTestKit.Advancer(
            AccountAccessTestKit.Reader(id => id == UserId ? _account : null), _coordinator,
            transition => _account = transition);
        _addressSwap = new ConfirmedAddressSwap(_accounts, _sender, _logger, _accessWriter);
    }

    private ConfirmEmailChangeCommandHandler Sut() =>
        new(_currentUser, _grants, _addressSwap, AccountAccessTestKit.Reader(id => id == UserId ? _account : null),
            _requests, _clock);

    public async ValueTask DisposeAsync()
    {
        await _coordinator.DisposeAsync();
        await _db.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private async Task SeedRequestAsync()
    {
        if (!_commitRequest)
            return;
        EmailChangeRequestTestKit.AddCommittedRequest(_db, UserId, _original);
        await _db.SaveChangesAsync(Ct);
    }

    private static ConfirmEmailChangeCommand Command => new(Grant.Reveal(), NewEmail);

    private async ValueTask<Result<ConfirmedEmailChange>> HandleAsync(ConfirmEmailChangeCommand command)
    {
        await SeedRequestAsync();
        await using var scope = await _coordinator.BeginAsync([UserId], lifecycle: true, Ct);
        var result = await Sut().Handle(command, Ct);
        if (result.IsSuccess)
            await scope.CommitAsync(Ct);
        return result;
    }

    [Fact]
    public async Task A_redeemed_grant_moves_the_account_and_only_tells_the_old_address_after_commit_notification()
    {
        var result = await HandleAsync(Command);

        result.IsSuccess.ShouldBeTrue();
        // The User.EmailChanged audit aggregate id AND the id the endpoint re-issues the session for.
        result.Value.UserId.ShouldBe(UserId);
        result.Value.Authorization.AccessRevision.ShouldBe(1);
        result.Value.Authorization.Lifetime.ShouldBe(SessionLifetime.Session);
        await _sender.DidNotReceiveWithAnyArgs().SendEmailChangedNotificationAsync(default!, Ct);
        await _addressSwap.NotifyCommittedAsync(Ct);
        Received.InOrder(async () =>
        {
            await _grants.RedeemAsync(Grant, Expected, Arg.Any<CancellationToken>());
            await _accounts.SwapConfirmedAddressAsync(UserId, NewEmail, SwapPrecondition.None, Arg.Any<CancellationToken>());
            await _sender.SendEmailChangedNotificationAsync(OldEmail, Arg.Any<CancellationToken>());
        });
        await _sender.DidNotReceive().SendEmailChangedNotificationAsync(NewEmail, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_grant_is_asserted_for_the_sessions_user_and_the_commands_address()
    {
        // The store binds the user and address; the handler independently checks the redeemed proof's original generation.
        await HandleAsync(Command);

        await _grants.Received(1).RedeemAsync(
            Grant,
            Arg.Is<GrantAssertion>(a => a == Expected),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_unusable_grant_is_gone_and_moves_nothing()
    {
        _grants.RedeemAsync(Grant, Expected, Arg.Any<CancellationToken>()).Returns((GrantSubject?)null);

        var result = await HandleAsync(Command);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.EmailChangeGrantUnusable);
        result.Error.Kind.ShouldBe(ErrorKind.Gone);
        await _accounts.DidNotReceiveWithAnyArgs().SwapConfirmedAddressAsync(default, default!, default!, Ct);
        await _sender.DidNotReceiveWithAnyArgs().SendEmailChangedNotificationAsync(default!, Ct);
    }

    [Fact]
    public async Task A_refused_swap_propagates_its_error_and_notifies_nobody()
    {
        var taken = DomainError.Conflict(AuthErrorCodes.EmailTaken, AuthErrorCodes.EmailTakenMessage);
        _accounts.SwapConfirmedAddressAsync(UserId, NewEmail, SwapPrecondition.None, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<AddressSwapped>(taken));

        var result = await HandleAsync(Command);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(taken);
        await _sender.DidNotReceiveWithAnyArgs().SendEmailChangedNotificationAsync(default!, Ct);
    }

    [Fact]
    public async Task A_notice_that_throws_never_fails_the_completed_change_and_logs_no_address()
    {
        _sender.SendEmailChangedNotificationAsync(OldEmail, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("e-post-transport nere")));

        var result = await HandleAsync(Command);

        result.IsSuccess.ShouldBeTrue();
        result.Value.UserId.ShouldBe(UserId);
        result.Value.Authorization.AccessRevision.ShouldBe(1);
        await _addressSwap.NotifyCommittedAsync(Ct);

        var entry = _logger.Records.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.EventId.Id.ShouldBe(4002);
        entry.Message.ShouldContain(UserId.ToString());
        entry.Message.ShouldNotContain(OldEmail);
        entry.Message.ShouldNotContain(NewEmail);
    }

    [Fact]
    public async Task An_operator_erased_address_is_refused_without_a_swap_or_notice()
    {
        // Declared unreachable operator damage to the usable-address invariant. Only safe refusal is asserted.
        _account = _account with { Email = null };

        var result = await HandleAsync(Command);

        result.Error.Code.ShouldBe(AuthErrorCodes.EmailChangeGrantUnusable);
        await _accounts.DidNotReceiveWithAnyArgs().SwapConfirmedAddressAsync(default, default!, default!, Ct);
        await _sender.DidNotReceiveWithAnyArgs().SendEmailChangedNotificationAsync(default!, Ct);
    }

    [Theory]
    [InlineData("suspended")]
    [InlineData("reinstated")]
    [InlineData("pending-deletion")]
    [InlineData("historical-missing-profile")]
    public async Task An_original_grant_that_no_longer_admits_the_account_moves_nothing(string state)
    {
        _account = state switch
        {
            "suspended" => _account with { IsSuspended = true, AccessRevision = 1, CredentialCutoff = 1 },
            "reinstated" => _account with { AccessRevision = 2, CredentialCutoff = 2 },
            "pending-deletion" => _account with { DeletedAt = FakeDateTimeProvider.Default.UtcNow },
            // Retired registrar at 22aefd8db; current writer pin: AccountRegistrationAtomicityTests.
            "historical-missing-profile" => _account with { HasProfile = false },
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };

        var result = await HandleAsync(Command);

        result.Error.Code.ShouldBe(AuthErrorCodes.EmailChangeGrantUnusable);
        result.Error.Kind.ShouldBe(ErrorKind.Gone);
        await _accounts.DidNotReceiveWithAnyArgs().SwapConfirmedAddressAsync(default, default!, default!, Ct);
        await _sender.DidNotReceiveWithAnyArgs().SendEmailChangedNotificationAsync(default!, Ct);
    }

    [Fact]
    public async Task An_actor_session_from_an_older_revision_cannot_use_even_a_fresh_grant()
    {
        _account = _account with { AccessRevision = 2, CredentialCutoff = 2 };
        _grants.RedeemAsync(Grant, Expected, Arg.Any<CancellationToken>())
            .Returns(new GrantSubject.ChangeEmail(UserId, NewEmail)
            { Access = new AccountAccessProof(2, UserId, 2), Request = _original });

        var result = await HandleAsync(Command);

        result.Error.Code.ShouldBe(AuthErrorCodes.EmailChangeGrantUnusable);
        await _accounts.DidNotReceiveWithAnyArgs().SwapConfirmedAddressAsync(default, default!, default!, Ct);
    }

    [Fact]
    public async Task A_fresh_grant_and_session_after_reinstatement_authorize_the_next_credential_generation()
    {
        _account = _account with { AccessRevision = 2, CredentialCutoff = 2 };
        _currentUser.AccessRevision.Returns(2L);
        var proof = new AccountAccessProof(2, UserId, 2);
        _grants.RedeemAsync(Grant, Expected, Arg.Any<CancellationToken>())
            .Returns(new GrantSubject.ChangeEmail(UserId, NewEmail) { Access = proof, Request = _original });

        var result = await HandleAsync(Command);

        result.Value.UserId.ShouldBe(UserId);
        result.Value.Authorization.AccessRevision.ShouldBe(3);
        proof.AccessRevision.ShouldBe(2);
        proof.FlowEpoch.ShouldBe(2);
    }

    [Fact]
    public async Task A_queued_notice_is_discarded_when_the_outer_transaction_aborts()
    {
        await SeedRequestAsync();
        await using (var scope = await _coordinator.BeginAsync([UserId], lifecycle: true, Ct))
            (await Sut().Handle(Command, Ct)).IsSuccess.ShouldBeTrue();

        _addressSwap.DiscardNotices();
        await _addressSwap.NotifyCommittedAsync(Ct);

        _coordinator.Commits.ShouldBe(0);
        _coordinator.Rollbacks.ShouldBe(1);
        await _sender.DidNotReceiveWithAnyArgs().SendEmailChangedNotificationAsync(default!, Ct);
    }

    [Fact]
    public async Task A_grant_without_its_original_request_commit_cannot_move_the_address()
    {
        _commitRequest = false;

        var result = await HandleAsync(Command);

        result.Error.Code.ShouldBe(AuthErrorCodes.EmailChangeGrantUnusable);
        result.Error.Kind.ShouldBe(ErrorKind.Gone);
        await _accounts.DidNotReceiveWithAnyArgs().SwapConfirmedAddressAsync(default, default!, default!, Ct);
        await _sender.DidNotReceiveWithAnyArgs().SendEmailChangedNotificationAsync(default!, Ct);
    }

    [Fact]
    public async Task A_genuine_legacy_grant_is_unusable_even_on_a_never_transitioned_account()
    {
        // Pre-#1976 self-service grants lacked the request identity. The current verification writer
        // is pinned by VerifyEmailChangeChallengeCommandHandlerTests.A_verified_code_is_a_change_email_grant_for_this_user_and_the_proven_address.
        _grants.RedeemAsync(Grant, Expected, Arg.Any<CancellationToken>())
            .Returns(new GrantSubject.ChangeEmail(UserId, NewEmail));

        var result = await HandleAsync(Command);

        result.Error.Code.ShouldBe(AuthErrorCodes.EmailChangeGrantUnusable);
        await _accounts.DidNotReceiveWithAnyArgs().SwapConfirmedAddressAsync(default, default!, default!, Ct);
    }

    [Fact]
    public async Task No_signed_in_user_is_refused_before_the_grant_is_redeemed()
    {
        _currentUser.UserId.Returns((Guid?)null);

        var result = await HandleAsync(Command);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.NotAuthenticated);
        await _grants.DidNotReceiveWithAnyArgs().RedeemAsync(default, default!, Ct);
        await _accounts.DidNotReceiveWithAnyArgs().SwapConfirmedAddressAsync(default, default!, default!, Ct);
    }

    [Theory]
    [InlineData(null, NewEmail)]
    [InlineData("", NewEmail)]
    [InlineData("a-grant", null)]
    [InlineData("a-grant", "")]
    public async Task Missing_input_is_refused_before_the_grant_is_redeemed(string? grant, string? newEmail)
    {
        var result = await HandleAsync(new ConfirmEmailChangeCommand(grant, newEmail));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AuthErrorCodes.InvalidInput);
        await _grants.DidNotReceiveWithAnyArgs().RedeemAsync(default, default!, Ct);
        await _accounts.DidNotReceiveWithAnyArgs().SwapConfirmedAddressAsync(default, default!, default!, Ct);
    }
}

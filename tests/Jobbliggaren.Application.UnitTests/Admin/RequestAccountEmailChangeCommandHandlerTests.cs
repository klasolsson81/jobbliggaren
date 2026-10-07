using Jobbliggaren.Application.Admin.Accounts;
using Jobbliggaren.Application.Admin.Accounts.Commands.RequestAccountEmailChange;
using Jobbliggaren.Application.Admin.Accounts.Queries.GetPendingAccountEmailChange;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.AccountEmailChanges;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.UnitTests.Auth;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin;

/// <summary>
/// #1975 (ADR 0153) — an administrator starts a change of another account's address. The tests pin the gates' ORDER,
/// that a refusal spends nothing after it, that the budgets are the two keyed by the new address and never the
/// account's own, the record the store is asked to write, the two mails in their order, and that a mail that is not
/// accepted removes exactly the record this request wrote.
/// </summary>
public sealed class RequestAccountEmailChangeCommandHandlerTests : IAsyncDisposable
{
    private const string Grant = "an-opaque-reauth-grant"; // gitleaks:allow
    private const string NewEmail = "ny.adress@example.se";
    private const string CurrentEmail = "nuvarande@example.se";
    private static readonly Guid AdminId = Guid.NewGuid();
    private static readonly Guid TargetId = Guid.NewGuid();
    private static readonly JobSeekerId ProfileId = new(Guid.NewGuid());
    private static readonly DateTimeOffset RegisteredAt = new(2026, 4, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly LoginCode Code = LoginCode.FromRaw("042917");
    private static readonly DateTimeOffset CompletableFrom = new(2026, 10, 8, 12, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExpiresAt = CompletableFrom.AddHours(24);

    // Not the 60 s default, so a handler reading another option's window would be seen.
    private const int ChangeEmailWindowSeconds = 137;
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(ChangeEmailWindowSeconds);

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IUserAccountService _accounts = Substitute.For<IUserAccountService>();
    private readonly IRateBudget _budget = Substitute.For<IRateBudget>();
    private readonly IAccountEmailChangeStore _store = Substitute.For<IAccountEmailChangeStore>();
    private readonly IEmailSender _sender = Substitute.For<IEmailSender>();
    private readonly RecordingLogger<RequestAccountEmailChangeCommandHandler> _logger = new();
    // A record of its own rather than a substitute: a substitute intercepts the record's Equals and answers false.
    private readonly AccountEmailChangeReceipt _receipt = new TestReceipt();
    private AccountDirectoryEntry? _target = Account();
    private AccountAccessSnapshot _actor = AccountAccessTestKit.Account(AdminId, "admin@example.com") with { IsAdmin = true };
    private readonly AppDbContext _db = TestAppDbContextFactory.Create();
    private readonly AccountAccessTestKit.RecordingAccountAccessCoordinator _coordinator = AccountAccessTestKit.Coordinator();
    private readonly FakeDateTimeProvider _clock = new(CompletableFrom - AccountEmailChangePolicy.Delay);
    private NewAccountEmailChange? _written;
    private readonly IAccountAccessReader _reader;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record TestReceipt : AccountEmailChangeReceipt;

    public RequestAccountEmailChangeCommandHandlerTests()
    {
        _currentUser.UserId.Returns(AdminId);
        _currentUser.AccessRevision.Returns(0L);
        _sender.CanDeliver.Returns(true);
        _reader = AccountAccessTestKit.Reader(userId => userId == AdminId
            ? _actor
            : userId == TargetId && _target is { } target
                ? new AccountAccessSnapshot(target.UserId, target.Email, target.IsSuspended, target.AccessRevision,
                    target.CredentialCutoff, target.JobSeekerId is not null, target.DeletedAt, target.IsAdmin) : null);
        _accounts.CheckAddressIsFreeAsync(TargetId, NewEmail, Arg.Any<CancellationToken>()).Returns(Result.Success());
        _budget.TryConsumeAsync(Arg.Any<RateBudgetScope>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _store.PutAsync(Arg.Any<NewAccountEmailChange>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _written = call.Arg<NewAccountEmailChange>();
                return new AccountEmailChangePut.Written(Code, CompletableFrom, ExpiresAt, _receipt);
            });
        _store.FindPendingAsync(TargetId, Arg.Any<CancellationToken>()).Returns(_ => _written is { } written
            ? new PendingAccountEmailChange(PendingAccountEmailChangeState.Pending, CompletableFrom, ExpiresAt)
            { RequestId = written.RequestId, IssuedAt = _clock.UtcNow, AccessRevision = written.Access.AccessRevision!.Value }
            : null);
        _sender.SendAccountEmailChangeRequestedNotificationAsync(
            CurrentEmail, CompletableFrom, ExpiresAt, Arg.Any<CancellationToken>()).Returns(_ =>
            {
                _coordinator.HasActiveScope.ShouldBeFalse("the staging scope must be disposed before transport");
                return Task.CompletedTask;
            });
    }

    private static AccountDirectoryEntry Account(
        bool isAdmin = false, AccountStatus status = AccountStatus.Active, string? email = CurrentEmail) =>
        new(TargetId, email, isAdmin, EmailConfirmed: true, status,
            JobSeekerId: status == AccountStatus.ProfileMissing ? null : ProfileId,
            RegisteredAt: status == AccountStatus.ProfileMissing ? null : RegisteredAt,
            DeletedAt: status == AccountStatus.PendingDeletion ? RegisteredAt.AddDays(1) : null,
            IsSuspended: status == AccountStatus.Suspended,
            AccessRevision: status == AccountStatus.Suspended ? 1 : 0,
            CredentialCutoff: status == AccountStatus.Suspended ? 1 : 0);

    private RequestAccountEmailChangeCommandHandler Sut() => new(
        _currentUser,
        _accounts,
        _budget,
        Options.Create(new AuthEmailCooldownOptions { ChangeEmailWindowSeconds = ChangeEmailWindowSeconds }),
        _store,
        _sender,
        _reader, _logger, _coordinator, _db, _clock, Substitute.For<ICorrelationIdProvider>(),
        Substitute.For<IRequestContextProvider>());

    public async ValueTask DisposeAsync()
    {
        await _coordinator.DisposeAsync();
        await _db.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private static RequestAccountEmailChangeCommand Command => new(TargetId, NewEmail, Grant);

    private async Task NothingWasSpentOrSent()
    {
        await _budget.DidNotReceiveWithAnyArgs().TryConsumeAsync(default!, default!, Ct);
        await _store.DidNotReceiveWithAnyArgs().PutAsync(default!, Ct);
        await _sender.DidNotReceiveWithAnyArgs().SendAccountEmailChangeRequestedNotificationAsync(default!, default, default, Ct);
        await _sender.DidNotReceiveWithAnyArgs().SendLoginChallengeAsync(default!, default!, Ct);
    }

    private async Task The_surviving_staged_record_is_not_a_visible_activated_change()
    {
        var original = _written.ShouldNotBeNull();
        (await _store.FindPendingAsync(TargetId, Ct)).ShouldNotBeNull().RequestId.ShouldBe(original.RequestId);
        var requests = Substitute.For<IAccountEmailChangeRequests>();
        requests.HasCommittedRequestAsync(TargetId, original.RequestId, _clock.UtcNow, ExpiresAt,
            Arg.Any<CancellationToken>()).Returns(_ => _db.AuditLogEntries.AsNoTracking().Any(row =>
                row.EventType == RequestAccountEmailChangeCommand.RequestedEventType && row.AggregateId == TargetId
                && row.OccurredAt >= _clock.UtcNow && row.OccurredAt < ExpiresAt && row.Payload != null
                && row.Payload.Contains(original.RequestId.ToString("D"))));

        var visible = await new GetPendingAccountEmailChangeQueryHandler(_store, _reader, _coordinator, requests)
            .Handle(new GetPendingAccountEmailChangeQuery(TargetId), Ct);

        visible.ShouldBeNull("a surviving Redis record without activation cannot authorize the account panel");
    }

    [Fact]
    public async Task An_admitted_request_reads_checks_spends_writes_then_tells_the_current_address_before_it_mails_the_code()
    {
        var result = await Sut().Handle(Command, Ct);

        result.Value.ShouldBe(new AccountEmailChangePending(CompletableFrom, ExpiresAt));
        _coordinator.Commits.ShouldBe(2);
        _coordinator.BegunScopes.Count.ShouldBe(2);
        _coordinator.BegunScopes.ShouldAllBe(scope => !scope.Lifecycle
            && scope.UserIds.Contains(AdminId) && scope.UserIds.Contains(TargetId));
        var witness = await _db.AuditLogEntries.AsNoTracking().SingleAsync(Ct);
        witness.EventType.ShouldBe(RequestAccountEmailChangeCommand.RequestedEventType);
        witness.UserId.ShouldBe(AdminId);
        witness.AggregateId.ShouldBe(TargetId);
        var payload = witness.Payload.ShouldNotBeNull();
        payload.ShouldContain(_written.ShouldNotBeNull().RequestId.ToString("D"));
        payload.ShouldNotContain(NewEmail);
        payload.ShouldNotContain(CurrentEmail);
        Received.InOrder(async () =>
        {
            await _reader.ReadAsync(TargetId, Arg.Any<CancellationToken>());
            await _accounts.CheckAddressIsFreeAsync(TargetId, NewEmail, Arg.Any<CancellationToken>());
            await _budget.TryConsumeAsync(ChangeEmailPolicy.TargetCooldown(Window), NewEmail, Arg.Any<CancellationToken>());
            await _budget.TryConsumeAsync(ChangeEmailPolicy.PerTargetDailyBudget, NewEmail, Arg.Any<CancellationToken>());
            await _store.PutAsync(Arg.Any<NewAccountEmailChange>(), Arg.Any<CancellationToken>());
            await _sender.SendAccountEmailChangeRequestedNotificationAsync(
                CurrentEmail, CompletableFrom, ExpiresAt, Arg.Any<CancellationToken>());
            await _sender.SendLoginChallengeAsync(NewEmail, Arg.Any<LoginChallengeEmail>(), Arg.Any<CancellationToken>());
            await _reader.ReadAsync(TargetId, Arg.Any<CancellationToken>());
            await _accounts.CheckAddressIsFreeAsync(TargetId, NewEmail, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task The_change_is_written_for_the_account_with_the_address_the_server_read_never_one_the_request_named()
    {
        var command = Command;
        await Sut().Handle(command, Ct);

        await _store.Received(1).PutAsync(
            new NewAccountEmailChange(TargetId, NewEmail, CurrentEmail)
            { Access = AccountAccessTestKit.Bound(TargetId) with { ExpectedEmail = CurrentEmail, ExpectedCutoff = 0 }, RequestId = command.RequestId },
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_new_address_gets_the_code_with_both_instants_of_the_change()
    {
        await Sut().Handle(Command, Ct);

        await _sender.Received(1).SendLoginChallengeAsync(
            NewEmail,
            new LoginChallengeEmail.AccountEmailChangeCode(Code, CompletableFrom, ExpiresAt),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_request_spends_only_the_two_budgets_keyed_by_the_new_address_and_never_the_accounts_own()
    {
        // The per-user scopes are keyed by the account because only its session holder can spend them; an
        // administrator spending them would block the owner's own change (ADR 0142 Amendment (4)).
        await Sut().Handle(Command, Ct);

        await _budget.ReceivedWithAnyArgs(2).TryConsumeAsync(default!, default!, Ct);
        await _budget.DidNotReceive().TryConsumeAsync(Arg.Any<RateBudgetScope>(), TargetId.ToString(), Arg.Any<CancellationToken>());
        await _budget.DidNotReceive().TryConsumeAsync(Arg.Any<RateBudgetScope>(), AdminId.ToString(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_sender_that_cannot_deliver_refuses_before_anything_is_read()
    {
        _sender.CanDeliver.Returns(false);

        var result = await Sut().Handle(Command, Ct);

        result.Error.Code.ShouldBe(AuthErrorCodes.EmailDeliveryUnavailable);
        await _reader.DidNotReceiveWithAnyArgs().ReadAsync(default, Ct);
        await NothingWasSpentOrSent();
    }

    [Fact]
    public async Task The_administrators_own_account_is_refused_before_it_is_read()
    {
        var result = await Sut().Handle(new RequestAccountEmailChangeCommand(AdminId, NewEmail, Grant), Ct);

        result.Error.Code.ShouldBe(AuthErrorCodes.AccountEmailChangeAdministratorTarget);
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        await _reader.DidNotReceiveWithAnyArgs().ReadAsync(default, Ct);
        await NothingWasSpentOrSent();
    }

    [Fact]
    public async Task An_account_that_is_gone_is_not_found_and_spends_nothing()
    {
        _target = null;

        var result = await Sut().Handle(Command, Ct);

        result.Error.Kind.ShouldBe(ErrorKind.NotFound);
        await NothingWasSpentOrSent();
    }

    [Fact]
    public async Task An_administrator_account_is_refused_by_its_role_read_fresh()
    {
        // A second Admin holder: produced by IdempotentAdminRoleSeeder before #2006. Today's seeder does not, pinned by
        // AdminBootstrapHolderTests.The_bootstrap_never_grants_the_configured_address_s_next_holder_while_the_role_has_one.
        _target = Account(isAdmin: true);

        var result = await Sut().Handle(Command, Ct);

        result.Error.Code.ShouldBe(AuthErrorCodes.AccountEmailChangeAdministratorTarget);
        await _accounts.DidNotReceiveWithAnyArgs().CheckAddressIsFreeAsync(default, default!, Ct);
        await NothingWasSpentOrSent();
    }

    [Theory]
    [InlineData(AccountStatus.PendingDeletion)]
    [InlineData(AccountStatus.ProfileMissing)]
    [InlineData(AccountStatus.Suspended)]
    public async Task An_account_that_is_not_active_is_refused_and_spends_nothing(AccountStatus status)
    {
        _target = Account(status: status);

        var result = await Sut().Handle(Command, Ct);

        result.Error.Code.ShouldBe(AuthErrorCodes.AccountEmailChangeInactiveTarget);
        await NothingWasSpentOrSent();
    }

    [Fact]
    public async Task An_account_with_no_stored_address_is_refused_and_spends_nothing()
    {
        // Declared unreachable operator damage to #822's invariant: the stored address was erased.
        // Assert only that this read refuses initiation safely.
        _target = Account(email: null);

        var result = await Sut().Handle(Command, Ct);

        result.Error.Code.ShouldBe(AuthErrorCodes.AccountEmailChangeInactiveTarget);
        await NothingWasSpentOrSent();
    }

    [Theory]
    [InlineData(AuthErrorCodes.EmailTaken)]
    [InlineData(AuthErrorCodes.EmailNotStorable)]
    public async Task A_taken_or_unstorable_address_is_refused_and_spends_nothing(string code)
    {
        var refusal = code == AuthErrorCodes.EmailTaken
            ? DomainError.Conflict(code, AuthErrorCodes.EmailTakenMessage)
            : DomainError.Validation(code, AuthErrorCodes.EmailNotStorableMessage);
        _accounts.CheckAddressIsFreeAsync(TargetId, NewEmail, Arg.Any<CancellationToken>()).Returns(Result.Failure(refusal));

        var result = await Sut().Handle(Command, Ct);

        result.Error.ShouldBe(refusal);
        await NothingWasSpentOrSent();
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task A_spent_address_budget_answers_the_shared_cooldown_and_writes_nothing(bool cooldown, bool daily)
    {
        _budget.TryConsumeAsync(ChangeEmailPolicy.TargetCooldown(Window), NewEmail, Arg.Any<CancellationToken>()).Returns(cooldown);
        _budget.TryConsumeAsync(ChangeEmailPolicy.PerTargetDailyBudget, NewEmail, Arg.Any<CancellationToken>()).Returns(daily);

        var result = await Sut().Handle(Command, Ct);

        result.Error.Code.ShouldBe(AuthErrorCodes.ChangeEmailCooldown);
        await _store.DidNotReceiveWithAnyArgs().PutAsync(default!, Ct);
        await _sender.DidNotReceiveWithAnyArgs().SendLoginChallengeAsync(default!, default!, Ct);
    }

    [Fact]
    public async Task An_address_another_accounts_change_holds_is_refused_and_nothing_is_mailed()
    {
        _store.PutAsync(Arg.Any<NewAccountEmailChange>(), Arg.Any<CancellationToken>())
            .Returns(AccountEmailChangePut.AddressPendingForAnotherAccount.Instance);

        var result = await Sut().Handle(Command, Ct);

        result.Error.Code.ShouldBe(AuthErrorCodes.AccountEmailChangePendingForAnotherAccount);
        await _sender.DidNotReceiveWithAnyArgs().SendAccountEmailChangeRequestedNotificationAsync(default!, default, default, Ct);
        await _sender.DidNotReceiveWithAnyArgs().SendLoginChallengeAsync(default!, default!, Ct);
    }

    [Fact]
    public async Task A_notice_that_is_not_accepted_removes_the_change_sends_no_code_and_propagates()
    {
        var refused = new EmailDeliveryException("account-email-change-requested-notification", "HttpRequestException");
        _sender.SendAccountEmailChangeRequestedNotificationAsync(
                Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(refused);

        var thrown = await Should.ThrowAsync<EmailDeliveryException>(() => Sut().Handle(Command, Ct).AsTask());

        thrown.ShouldBeSameAs(refused);
        await _store.Received(1).RevokeAsync(_receipt, CancellationToken.None);
        await _sender.DidNotReceiveWithAnyArgs().SendLoginChallengeAsync(default!, default!, Ct);
        _logger.Records.ShouldContain(record => record.EventId.Id == 4003
            && record.Properties.Any(p => p.Key == "TargetUserId" && Equals(p.Value, TargetId)));
    }

    [Fact]
    public async Task A_code_mail_that_is_not_accepted_removes_the_change_and_propagates()
    {
        _sender.SendLoginChallengeAsync(Arg.Any<string>(), Arg.Any<LoginChallengeEmail>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(() => Sut().Handle(Command, Ct).AsTask());

        await _store.Received(1).RevokeAsync(_receipt, CancellationToken.None);
    }

    [Fact]
    public async Task A_removal_that_fails_after_a_refused_mail_is_logged_and_the_mails_failure_is_the_one_answered()
    {
        var refused = new EmailDeliveryException("login-challenge", "HttpRequestException");
        _sender.SendLoginChallengeAsync(Arg.Any<string>(), Arg.Any<LoginChallengeEmail>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(refused);
        _store.RevokeAsync(_receipt, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("store down"));

        var thrown = await Should.ThrowAsync<EmailDeliveryException>(() => Sut().Handle(Command, Ct).AsTask());

        thrown.ShouldBeSameAs(refused);
        _logger.Records.ShouldContain(record => record.EventId.Id == 4004
            && record.Properties.Any(p => p.Key == "TargetUserId" && Equals(p.Value, TargetId)));
    }

    [Fact]
    public async Task A_change_whose_mails_were_accepted_is_never_removed()
    {
        await Sut().Handle(Command, Ct);

        await _store.DidNotReceiveWithAnyArgs().RevokeAsync(default!, Ct);
        _logger.Records.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_suspension_during_transport_refuses_activation_without_promoting_the_original_target_proof()
    {
        _sender.SendLoginChallengeAsync(NewEmail, Arg.Any<LoginChallengeEmail>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _coordinator.HasActiveScope.ShouldBeFalse();
                _target = Account(status: AccountStatus.Suspended);
                return Task.CompletedTask;
            });

        var result = await Sut().Handle(Command, Ct);

        result.IsFailure.ShouldBeTrue();
        _written.ShouldNotBeNull().Access.ShouldBe(AccountAccessTestKit.Bound(TargetId)
            with
        { ExpectedEmail = CurrentEmail, ExpectedCutoff = 0 });
        (await _db.AuditLogEntries.CountAsync(Ct)).ShouldBe(0);
        _coordinator.Commits.ShouldBe(1);
        // No activation committed. The Redis artifact may survive without authorizing any change.
        await _store.DidNotReceiveWithAnyArgs().RevokeAsync(default!, Ct);
        await The_surviving_staged_record_is_not_a_visible_activated_change();
    }

    [Fact]
    public async Task The_actors_changed_revision_during_transport_cannot_activate_the_staged_target_request()
    {
        _sender.SendLoginChallengeAsync(NewEmail, Arg.Any<LoginChallengeEmail>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _coordinator.HasActiveScope.ShouldBeFalse();
                // The actor completed its own verified address change in another session during transport.
                _actor = _actor with { Email = "changed-admin@example.com", AccessRevision = 1, CredentialCutoff = 1 };
                return Task.CompletedTask;
            });

        var result = await Sut().Handle(Command, Ct);

        result.IsFailure.ShouldBeTrue();
        _written.ShouldNotBeNull().Access.ShouldBe(AccountAccessTestKit.Bound(TargetId)
            with
        { ExpectedEmail = CurrentEmail, ExpectedCutoff = 0 });
        (await _db.AuditLogEntries.CountAsync(Ct)).ShouldBe(0);
        _coordinator.Commits.ShouldBe(1);
        await _store.DidNotReceiveWithAnyArgs().RevokeAsync(default!, Ct);
        await The_surviving_staged_record_is_not_a_visible_activated_change();
    }

    [Fact]
    public async Task No_log_line_names_an_address()
    {
        _sender.SendLoginChallengeAsync(Arg.Any<string>(), Arg.Any<LoginChallengeEmail>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new EmailDeliveryException("login-challenge", "HttpRequestException"));
        _store.RevokeAsync(_receipt, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("store down"));

        await Should.ThrowAsync<EmailDeliveryException>(() => Sut().Handle(Command, Ct).AsTask());

        _logger.Records.Count.ShouldBe(2);
        foreach (var record in _logger.Records)
        {
            foreach (var address in new[] { NewEmail, CurrentEmail })
            {
                record.Message.ShouldNotContain(address, Case.Insensitive);
                record.Properties.Any(p => (p.Value?.ToString() ?? string.Empty).Contains(address)).ShouldBeFalse();
            }
        }
    }

    [Fact]
    public void The_command_prints_neither_the_address_nor_the_grant()
    {
        var printed = Command.ToString();

        printed.ShouldNotContain(NewEmail);
        printed.ShouldNotContain(Grant);
        printed.ShouldContain(TargetId.ToString());
    }
}

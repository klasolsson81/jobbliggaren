using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.AccountEmailChanges;
using Jobbliggaren.Application.Auth.Commands.CompleteAccountEmailChange;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// #1975 (ADR 0153) — the public completion of a change an administrator started. The tests pin that nothing about any
/// account is read before the store has answered, that only a full match goes further, that every refusal after it is
/// the one answer, that the account moves to the record's spelling under the change's precondition, and that the one
/// audit row names the account as its user and is never allowed to stop the teardown that follows.
/// </summary>
public sealed class CompleteAccountEmailChangeCommandHandlerTests
{
    private const string CurrentEmail = "nuvarande@example.se";
    private const string RecordsSpelling = "Ny.Adress@example.se";
    private const string Presented = "ny.adress@example.se";
    private const string Code = "042917";
    private static readonly Guid TargetId = Guid.NewGuid();
    private static readonly ExpectedCurrentAddress StartedFrom = new("fingerprint-of-the-address-it-started-from");
    private static readonly DateTimeOffset CompletableFrom = new(2026, 10, 8, 12, 30, 0, TimeSpan.Zero);
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;

    private readonly IEmailSender _sender = Substitute.For<IEmailSender>();
    private readonly IAccountEmailChangeStore _store = Substitute.For<IAccountEmailChangeStore>();
    private readonly IUserAccountService _accounts = Substitute.For<IUserAccountService>();
    private readonly ICorrelationIdProvider _correlation = Substitute.For<ICorrelationIdProvider>();
    private readonly IRequestContextProvider _request = Substitute.For<IRequestContextProvider>();
    private readonly RecordingLogger<CompleteAccountEmailChangeCommandHandler> _logger = new();
    private readonly SaveFailure _saveFailure = new();
    private readonly AppDbContext _db;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public CompleteAccountEmailChangeCommandHandlerTests()
    {
        _db = TestAppDbContextFactory.Create(_saveFailure);
        _sender.CanDeliver.Returns(true);
        _correlation.Current.Returns(Guid.NewGuid());
        _request.IpAddress.Returns("203.0.113.7");
        _request.UserAgent.Returns("probe/1.0");
        Verifies();
        _accounts.SwapConfirmedAddressAsync(
                TargetId, RecordsSpelling, SwapPrecondition.AdminInitiated(StartedFrom), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new AddressSwapped(CurrentEmail)));
    }

    private void Verifies() =>
        _store.ConsumeAsync(Presented, CurrentEmail, LoginCode.FromRaw(Code), Arg.Any<CancellationToken>())
            .Returns(new AccountEmailChangeVerdict.Verified(
                new AccountEmailChangeProof(TargetId, RecordsSpelling, StartedFrom)));

    private CompleteAccountEmailChangeCommandHandler Sut() => new(
        _sender,
        _store,
        _db,
        new ConfirmedAddressSwap(_accounts, _sender, new RecordingLogger<ConfirmedAddressSwap>()),
        Clock,
        _correlation,
        _request,
        _logger);

    private static CompleteAccountEmailChangeCommand Command => new(CurrentEmail, Presented, Code);

    private async Task SeedProfileAsync(bool softDeleted = false)
    {
        var seeker = JobSeeker.Register(TargetId, TermsAcceptance.AcceptCurrent(Clock), Clock).Value;
        if (softDeleted)
            seeker.SoftDelete(Clock);
        _db.JobSeekers.Add(seeker);
        await _db.SaveChangesAsync(Ct);
        _db.ChangeTracker.Clear();
    }

    private async Task NoAccountWasReadOrMoved() =>
        await _accounts.DidNotReceiveWithAnyArgs().SwapConfirmedAddressAsync(default, default!, default!, Ct);

    [Fact]
    public async Task A_full_match_moves_the_account_to_the_records_spelling_under_the_changes_precondition()
    {
        await SeedProfileAsync();

        var result = await Sut().Handle(Command, Ct);

        result.Value.ShouldBe(new AccountEmailChangeOutcome.Completed(TargetId, AuditRecorded: true));
        await _accounts.Received(1).SwapConfirmedAddressAsync(
            TargetId, RecordsSpelling, SwapPrecondition.AdminInitiated(StartedFrom), Arg.Any<CancellationToken>());
        await _sender.Received(1).SendEmailChangedNotificationAsync(CurrentEmail, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_one_audit_row_names_the_account_as_its_user_with_the_callers_ip_and_user_agent()
    {
        // The eraser anonymises an account's rows by their user id, so the completer's IP and user agent go with the
        // account; AuditBehavior would stamp the anonymous caller, null.
        await SeedProfileAsync();

        await Sut().Handle(Command, Ct);

        var row = (await _db.AuditLogEntries.AsNoTracking().ToListAsync(Ct)).ShouldHaveSingleItem();
        row.UserId.ShouldBe(TargetId);
        row.EventType.ShouldBe("User.EmailChangedViaAdministrator");
        row.AggregateType.ShouldBe("User");
        row.AggregateId.ShouldBe(TargetId);
        row.IpAddress.ShouldBe("203.0.113.7");
        row.UserAgent.ShouldBe("probe/1.0");
        row.Payload.ShouldBeNull();
    }

    [Fact]
    public async Task A_sender_that_cannot_deliver_refuses_before_the_store_is_asked()
    {
        _sender.CanDeliver.Returns(false);

        var result = await Sut().Handle(Command, Ct);

        result.Error.Code.ShouldBe(AuthErrorCodes.EmailDeliveryUnavailable);
        await _store.DidNotReceiveWithAnyArgs().ConsumeAsync(default!, default!, default, Ct);
        await NoAccountWasReadOrMoved();
    }

    [Fact]
    public async Task A_full_match_before_the_delay_answers_not_yet_and_reads_no_account()
    {
        _store.ConsumeAsync(Presented, CurrentEmail, LoginCode.FromRaw(Code), Arg.Any<CancellationToken>())
            .Returns(new AccountEmailChangeVerdict.NotYet(CompletableFrom));

        var result = await Sut().Handle(Command, Ct);

        result.Value.ShouldBe(new AccountEmailChangeOutcome.NotYet(CompletableFrom));
        await NoAccountWasReadOrMoved();
        (await _db.AuditLogEntries.CountAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Anything_but_a_full_match_is_the_one_refusal_and_reads_no_account()
    {
        _store.ConsumeAsync(Presented, CurrentEmail, LoginCode.FromRaw(Code), Arg.Any<CancellationToken>())
            .Returns(AccountEmailChangeVerdict.Unusable.Instance);

        var result = await Sut().Handle(Command, Ct);

        result.Error.Code.ShouldBe(AuthErrorCodes.AccountEmailChangeUnusable);
        result.Error.Kind.ShouldBe(ErrorKind.Gone);
        await NoAccountWasReadOrMoved();
        _logger.Records.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_account_whose_profile_is_gone_or_soft_deleted_is_the_one_refusal_and_never_moves(bool profileExists)
    {
        // No profile row is unreachable today: initiation refuses an account without one, and nothing removes one within
        // a change's life. That row is asserted only as the one refusal.
        if (profileExists)
            await SeedProfileAsync(softDeleted: true);

        var result = await Sut().Handle(Command, Ct);

        result.Error.Code.ShouldBe(AuthErrorCodes.AccountEmailChangeUnusable);
        await NoAccountWasReadOrMoved();
        _logger.Records.ShouldHaveSingleItem().Properties
            .ShouldContain(p => p.Key == "TargetUserId" && Equals(p.Value, TargetId));
    }

    [Theory]
    [InlineData(AuthErrorCodes.AccountEmailChangeStale)]
    [InlineData(AuthErrorCodes.EmailTaken)]
    [InlineData(AuthErrorCodes.EmailChangeIncomplete)]
    public async Task A_swap_that_is_refused_after_the_match_is_the_one_refusal_and_writes_no_row(string code)
    {
        await SeedProfileAsync();
        _accounts.SwapConfirmedAddressAsync(
                TargetId, RecordsSpelling, SwapPrecondition.AdminInitiated(StartedFrom), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<AddressSwapped>(DomainError.Conflict(code, "refused")));

        var result = await Sut().Handle(Command, Ct);

        result.Error.Code.ShouldBe(AuthErrorCodes.AccountEmailChangeUnusable);
        (await _db.AuditLogEntries.CountAsync(Ct)).ShouldBe(0);
        await _sender.DidNotReceiveWithAnyArgs().SendEmailChangedNotificationAsync(default!, Ct);
        _logger.Records.ShouldHaveSingleItem().Properties.ShouldContain(p => p.Key == "Reason" && Equals(p.Value, code));
    }

    [Fact]
    public async Task An_audit_row_that_cannot_be_written_is_answered_without_throwing_and_left_out_of_the_unit_of_work()
    {
        await SeedProfileAsync();
        _saveFailure.Armed = true;

        var result = await Sut().Handle(Command, Ct);

        result.Value.ShouldBe(new AccountEmailChangeOutcome.Completed(TargetId, AuditRecorded: false));
        var logged = _logger.Records.ShouldHaveSingleItem();
        logged.Level.ShouldBe(LogLevel.Error);
        logged.Properties.ShouldContain(p => p.Key == "TargetUserId" && Equals(p.Value, TargetId));

        // The pipeline's own save after the handler must not try the row again.
        _db.ChangeTracker.Entries().ShouldBeEmpty();
    }

    [Fact]
    public async Task No_log_line_names_an_address_or_the_code()
    {
        await SeedProfileAsync(softDeleted: true);

        await Sut().Handle(Command, Ct);

        foreach (var record in _logger.Records)
        {
            foreach (var secret in new[] { CurrentEmail, Presented, Code })
            {
                record.Message.ShouldNotContain(secret, Case.Insensitive);
                record.Properties.Any(p => (p.Value?.ToString() ?? string.Empty).Contains(secret)).ShouldBeFalse();
            }
        }
    }

    [Fact]
    public void The_command_prints_neither_address_nor_the_code()
    {
        var printed = Command.ToString();

        foreach (var secret in new[] { CurrentEmail, Presented, Code })
            printed.ShouldNotContain(secret);
    }

    // Throws on SaveChanges once armed, so the profile can be seeded first.
    private sealed class SaveFailure : SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            Armed
                ? throw new DbUpdateException("the audit row could not be written")
                : base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}

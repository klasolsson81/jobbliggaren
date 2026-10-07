using Jobbliggaren.Application.Admin.Accounts.Commands.CancelAccountEmailChange;
using Jobbliggaren.Application.Admin.Accounts.Commands.RequestAccountEmailChange;
using Jobbliggaren.Application.Admin.Accounts.Queries.GetPendingAccountEmailChange;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.AccountEmailChanges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.UnitTests.Auth;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin;

/// <summary>
/// Pending address records become visible only after their exact administrator request has committed.
/// Cancel no-ops remain failures so no success audit can claim a change.
/// </summary>
public sealed class AccountEmailChangeCancelAndReadHandlerTests : IAsyncDisposable
{
    private static readonly Guid TargetId = Guid.NewGuid();
    private static readonly DateTimeOffset CompletableFrom = new(2026, 10, 8, 12, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset IssuedAt = CompletableFrom - AccountEmailChangePolicy.Delay;
    private static readonly DateTimeOffset ExpiresAt = IssuedAt + AccountEmailChangePolicy.Ttl;
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private readonly RequestAccountEmailChangeCommand _initiatingRequest =
        new(TargetId, "new-address@example.com", "an-opaque-reauth-grant"); // gitleaks:allow
    private readonly AppDbContext _db = TestAppDbContextFactory.Create();
    private readonly IAccountEmailChangeStore _store = Substitute.For<IAccountEmailChangeStore>();
    private readonly IAccountEmailChangeRequests _requests = Substitute.For<IAccountEmailChangeRequests>();
    private readonly AccountAccessTestKit.RecordingAccountAccessCoordinator _access = AccountAccessTestKit.Coordinator();
    private AccountAccessSnapshot _account = AccountAccessTestKit.Account(TargetId, "person@example.com");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public AccountEmailChangeCancelAndReadHandlerTests()
    {
        _requests.HasCommittedRequestAsync(
                TargetId, _initiatingRequest.RequestId, IssuedAt, ExpiresAt, Arg.Any<CancellationToken>())
            .Returns(_ => _db.AuditLogEntries.AsNoTracking().Any(row =>
                row.EventType == _initiatingRequest.EventType && row.AggregateId == TargetId
                && row.OccurredAt == IssuedAt && row.Payload == RequestPayload()));
    }

    private PendingAccountEmailChange OriginalPending => new(
        PendingAccountEmailChangeState.CodeBurned, CompletableFrom, ExpiresAt)
    {
        AccessRevision = 0,
        RequestId = _initiatingRequest.RequestId,
        IssuedAt = IssuedAt,
    };

    private string RequestPayload() => _initiatingRequest.BuildAuditPayload(
        Result.Success(new AccountEmailChangePending(CompletableFrom, ExpiresAt)),
        Substitute.For<IIdentifierPseudonymizer>());

    private async Task SeedProfileAsync(bool commitRequest = true)
    {
        _db.JobSeekers.Add(JobSeeker.Register(TargetId, TermsAcceptance.AcceptCurrent(Clock), Clock).Value);
        if (commitRequest)
        {
            _db.AuditLogEntries.Add(AuditLogEntry.Create(
                IssuedAt, Guid.NewGuid(), Guid.NewGuid(), _initiatingRequest.EventType,
                _initiatingRequest.AggregateType, TargetId, null, null, payload: RequestPayload()));
        }
        await _db.SaveChangesAsync(Ct);
        _db.ChangeTracker.Clear();
    }

    private GetPendingAccountEmailChangeQueryHandler Reader() => new(
        _store,
        AccountAccessTestKit.ReaderFromProfiles(_db, id => id == TargetId ? _account.Email : null,
            _ => _account.AccessRevision, _ => _account.CredentialCutoff),
        _access,
        _requests);

    public async ValueTask DisposeAsync()
    {
        await _access.DisposeAsync();
        await _db.DisposeAsync();
        GC.SuppressFinalize(this);
    }

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
    public async Task The_read_answers_the_state_and_both_instants_of_the_committed_request()
    {
        await SeedProfileAsync();
        _store.FindPendingAsync(TargetId, Arg.Any<CancellationToken>()).Returns(OriginalPending);

        var pending = await Reader().Handle(new GetPendingAccountEmailChangeQuery(TargetId), Ct);

        pending.ShouldBe(new PendingAccountEmailChangeDto(
            PendingAccountEmailChangeState.CodeBurned, CompletableFrom, ExpiresAt));
        _access.HasActiveScope.ShouldBeFalse();
        _access.BegunScopes.ShouldHaveSingleItem().UserIds.ShouldContain(TargetId);
        await _requests.Received(1).HasCommittedRequestAsync(
            TargetId, _initiatingRequest.RequestId, IssuedAt, ExpiresAt, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_read_answers_nothing_when_nothing_is_pending()
    {
        _store.FindPendingAsync(TargetId, Arg.Any<CancellationToken>()).Returns((PendingAccountEmailChange?)null);

        (await Reader().Handle(new GetPendingAccountEmailChangeQuery(TargetId), Ct)).ShouldBeNull();

        await _requests.DidNotReceiveWithAnyArgs().HasCommittedRequestAsync(default, default, default, default, Ct);
    }

    [Fact]
    public async Task The_read_hides_a_staged_v2_record_whose_request_never_committed()
    {
        // PutAsync can finish before AuditBehavior commits, or remain after a failed commit and
        // unavailable Redis cleanup. A live account alone cannot authorize this staged record.
        await SeedProfileAsync(commitRequest: false);
        _store.FindPendingAsync(TargetId, Arg.Any<CancellationToken>()).Returns(OriginalPending);

        (await Reader().Handle(new GetPendingAccountEmailChangeQuery(TargetId), Ct)).ShouldBeNull();

        await _requests.Received(1).HasCommittedRequestAsync(
            TargetId, _initiatingRequest.RequestId, IssuedAt, ExpiresAt, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_read_hides_a_record_bound_to_another_request_even_when_one_request_committed()
    {
        await SeedProfileAsync();
        var supersedingRequest = new RequestAccountEmailChangeCommand(TargetId, "another@example.com", "another-grant");
        _store.FindPendingAsync(TargetId, Arg.Any<CancellationToken>())
            .Returns(OriginalPending with { RequestId = supersedingRequest.RequestId });

        (await Reader().Handle(new GetPendingAccountEmailChangeQuery(TargetId), Ct)).ShouldBeNull();

        await _requests.Received(1).HasCommittedRequestAsync(
            TargetId, supersedingRequest.RequestId, IssuedAt, ExpiresAt, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_read_hides_a_genuine_legacy_pending_record_even_on_a_never_transitioned_account()
    {
        // Retired #1975 v1 writer at 22aefd8db emitted no request nonce. Current producer metadata is
        // pinned by RequestAccountEmailChangeCommandHandlerTests and the committed-request case above.
        await SeedProfileAsync();
        _store.FindPendingAsync(TargetId, Arg.Any<CancellationToken>()).Returns(new PendingAccountEmailChange(
            PendingAccountEmailChangeState.Pending, CompletableFrom, ExpiresAt));

        (await Reader().Handle(new GetPendingAccountEmailChangeQuery(TargetId), Ct)).ShouldBeNull();
        await _requests.DidNotReceiveWithAnyArgs().HasCommittedRequestAsync(default, default, default, default, Ct);
    }

    [Fact]
    public async Task The_read_hides_an_old_record_after_a_credential_transition_even_with_its_original_audit()
    {
        await SeedProfileAsync();
        _store.FindPendingAsync(TargetId, Arg.Any<CancellationToken>()).Returns(OriginalPending);
        var reader = AccountAccessTestKit.Reader(id => id == TargetId ? _account : null);
        var writer = AccountAccessTestKit.Advancer(reader, _access, transition => _account = transition);
        await using (var scope = await _access.BeginAsync([TargetId], lifecycle: true, Ct))
        {
            var changed = await writer.AdvanceCredentialsAsync(TargetId, Ct);
            changed.AccessRevision.ShouldBe(1);
            await scope.CommitAsync(Ct);
        }

        (await Reader().Handle(new GetPendingAccountEmailChangeQuery(TargetId), Ct)).ShouldBeNull();
    }
}

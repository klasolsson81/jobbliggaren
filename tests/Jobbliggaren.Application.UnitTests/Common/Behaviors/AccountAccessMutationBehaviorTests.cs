using Jobbliggaren.Application.Admin.Accounts.Commands.ScheduleAccountDeletion;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.AccountEmailChanges;
using Jobbliggaren.Application.Auth.Commands.ConfirmEmailChange;
using Jobbliggaren.Application.Auth.Commands.DeleteAccount;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Behaviors;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Application.UnitTests.Auth;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Behaviors;

/// <summary>
/// The real self-service handler and mutation/audit/unit-of-work pipeline issue a credential-generation
/// capability only after known commit. The session probe executes ISessionStore's real default bridge.
/// These unit tests witness port ordering and capability admission; PostgreSQL tests own durable rollback.
/// </summary>
public sealed class AccountAccessMutationBehaviorTests : IAsyncDisposable
{
    private const string OldEmail = "old-address@example.com";
    private const string NewEmail = "new-address@example.com";
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly GrantToken Grant = GrantToken.Generate();
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IGrantStore _grants = Substitute.For<IGrantStore>();
    private readonly IUserAccountService _accounts = Substitute.For<IUserAccountService>();
    private readonly IEmailSender _sender = Substitute.For<IEmailSender>();
    private readonly AccountAccessTestKit.RecordingAccountAccessCoordinator _coordinator = AccountAccessTestKit.Coordinator();
    private readonly SaveProbe _save = new();
    private readonly AppDbContext _db;
    private readonly IAccountAccessReader _reader;
    private readonly ConfirmedAddressSwap _swap;
    private readonly IAccountEmailChangeRequests _requests;
    private readonly EmailChangeRequestProof _original = EmailChangeRequestTestKit.Original(ChallengeId.Generate(), Clock.UtcNow);
    private readonly SessionProbe _sessions;
    private AccountAccessSnapshot _account = AccountAccessTestKit.Account(UserId, OldEmail);
    // Other accounts have advanced the global epoch six times; this account has never transitioned.
    private long _epoch = 6;
    private CommittedSessionAuthorization? _issued;
    private int _handlerInvocations;
    private readonly Guid _deletionTargetId = Guid.NewGuid();
    private AccountDeletionScheduled? _deletionScheduled;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private ISessionStore Sessions => _sessions;

    public AccountAccessMutationBehaviorTests()
    {
        _db = TestAppDbContextFactory.Create(_save);
        _reader = AccountAccessTestKit.ReaderFromProfiles(_db, id => id == UserId ? _account.Email : null,
            _ => _account.AccessRevision, _ => _account.CredentialCutoff);
        _reader.ReadEpochAsync(Arg.Any<CancellationToken>()).Returns(_ => _epoch);
        _currentUser.UserId.Returns(UserId);
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.AccessRevision.Returns(0L);
        _requests = EmailChangeRequestTestKit.Reader(_db);
        _grants.RedeemAsync(Grant, GrantAssertion.Of(new GrantSubject.ChangeEmail(UserId, NewEmail)),
                Arg.Any<CancellationToken>())
            .Returns(new GrantSubject.ChangeEmail(UserId, NewEmail)
            { Access = new AccountAccessProof(6, UserId, 0), Request = _original });
        _accounts.SwapConfirmedAddressAsync(UserId, NewEmail, SwapPrecondition.None, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _account = _account with { Email = NewEmail };
                return Result.Success(new AddressSwapped(OldEmail));
            });
        var writer = AccountAccessTestKit.Advancer(_reader, _coordinator,
            transition => _account = transition, () => checked(++_epoch));
        _swap = new ConfirmedAddressSwap(_accounts, _sender, NullLogger<ConfirmedAddressSwap>.Instance, writer);
        _sessions = new SessionProbe(() => _account);
        _coordinator.BeforeCommit = () =>
        {
            _save.SawEmailAudit.ShouldBeTrue("the audit must enter Save before the outer scope commits");
            _issued.ShouldNotBeNull();
            _sessions.CreatedProofs.ShouldBeEmpty();
        };
    }

    public async ValueTask DisposeAsync()
    {
        await _coordinator.DisposeAsync();
        await _db.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private async Task SeedProfileAsync()
    {
        _db.JobSeekers.Add(JobSeeker.Register(UserId, TermsAcceptance.AcceptCurrent(Clock), Clock).Value);
        EmailChangeRequestTestKit.AddCommittedRequest(_db, UserId, _original);
        await _db.SaveChangesAsync(Ct);
        _save.SawEmailAudit = false;
    }

    private async Task<Result<ConfirmedEmailChange>> ConfirmThroughPipelineAsync(ConfirmEmailChangeCommand? input = null)
    {
        var command = input ?? new ConfirmEmailChangeCommand(Grant.Reveal(), NewEmail)
        { ReplacementLifetime = SessionLifetime.Persistent };
        var handler = new ConfirmEmailChangeCommandHandler(_currentUser, _grants, _swap, _reader, _requests, Clock);
        var audit = new AuditBehavior<ConfirmEmailChangeCommand, Result<ConfirmedEmailChange>>(
            _db, _currentUser, Clock, Substitute.For<ICorrelationIdProvider>(),
            Substitute.For<IRequestContextProvider>(), Substitute.For<IIdentifierPseudonymizer>());
        var unitOfWork = new UnitOfWorkBehavior<ConfirmEmailChangeCommand, Result<ConfirmedEmailChange>>(
            _db, NullLogger<UnitOfWorkBehavior<ConfirmEmailChangeCommand, Result<ConfirmedEmailChange>>>.Instance);
        var access = new AccountAccessMutationBehavior<ConfirmEmailChangeCommand, Result<ConfirmedEmailChange>>(
            [_coordinator], [_reader], [], _currentUser, [_swap]);
        MessageHandlerDelegate<ConfirmEmailChangeCommand, Result<ConfirmedEmailChange>> handle = async (message, ct) =>
        {
            _handlerInvocations++;
            var previousEmail = _account.Email.ShouldNotBeNull();
            var result = await handler.Handle(message, ct);
            if (result.IsSuccess)
            {
                _issued = result.Value.Authorization;
                await Should.ThrowAsync<InvalidOperationException>(
                    () => Sessions.CreateCommittedAsync(_issued, message.ReplacementLifetime, ct));
                _sessions.CreatedProofs.ShouldBeEmpty();
                await _sender.DidNotReceive().SendEmailChangedNotificationAsync(previousEmail, Arg.Any<CancellationToken>());
            }
            return result;
        };
        return await access.Handle(command,
            (message, ct) => unitOfWork.Handle(message,
                (audited, token) => audit.Handle(audited, handle, token), ct), Ct);
    }

    [Fact]
    public async Task Handle_ShouldAuthorizeExactlyTheCommittedGeneration_AfterAuditSaveAndKnownCommit()
    {
        await SeedProfileAsync();

        var result = await ConfirmThroughPipelineAsync();
        var session = await Sessions.CreateCommittedAsync(result.Value.Authorization, SessionLifetime.Persistent, Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Authorization.UserId.ShouldBe(UserId);
        result.Value.Authorization.AccessRevision.ShouldBe(1);
        result.Value.Authorization.Lifetime.ShouldBe(SessionLifetime.Persistent);
        session.ShouldNotBeNull().AccessRevision.ShouldBe(1);
        _sessions.CreatedProofs.ShouldHaveSingleItem().ShouldBe(new AccountAccessProof(7, UserId, 1)
        {
            ExpectedEmail = NewEmail,
            ExpectedCutoff = 7,
        });
        _coordinator.Commits.ShouldBe(1);
        _coordinator.HasActiveScope.ShouldBeFalse();
        _coordinator.BegunScopes.ShouldHaveSingleItem().Lifecycle.ShouldBeTrue();
        _handlerInvocations.ShouldBe(1);
        await _sender.Received(1).SendEmailChangedNotificationAsync(OldEmail, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShouldLeaveTheIssuedCapabilityUnusable_WhenAuditSaveFails()
    {
        await SeedProfileAsync();
        _save.FailEmailAudit = true;

        await Should.ThrowAsync<DbUpdateException>(() => ConfirmThroughPipelineAsync());
        await Should.ThrowAsync<InvalidOperationException>(
            () => Sessions.CreateCommittedAsync(_issued.ShouldNotBeNull(), SessionLifetime.Persistent, Ct));

        _save.SawEmailAudit.ShouldBeTrue();
        _coordinator.Commits.ShouldBe(0);
        _coordinator.HasActiveScope.ShouldBeFalse();
        _sessions.CreatedProofs.ShouldBeEmpty();
        _handlerInvocations.ShouldBe(1);
        await _swap.NotifyCommittedAsync(Ct);
        await _sender.DidNotReceiveWithAnyArgs().SendEmailChangedNotificationAsync(default!, Ct);
    }

    [Fact]
    public async Task CreateCommittedAsync_ShouldKeepTheOriginalGeneration_WhenAnotherAddressChangeHasCommitted()
    {
        await SeedProfileAsync();
        var first = await ConfirmThroughPipelineAsync();
        var originalAuthorization = first.Value.Authorization;
        const string nextAddress = "later-address@example.com";
        var nextGrant = GrantToken.Generate();
        var nextRequest = EmailChangeRequestTestKit.Original(ChallengeId.Generate(), Clock.UtcNow);
        EmailChangeRequestTestKit.AddCommittedRequest(_db, UserId, nextRequest);
        await _db.SaveChangesAsync(Ct);
        _currentUser.AccessRevision.Returns(1L);
        _grants.RedeemAsync(nextGrant, GrantAssertion.Of(new GrantSubject.ChangeEmail(UserId, nextAddress)),
                Arg.Any<CancellationToken>())
            .Returns(new GrantSubject.ChangeEmail(UserId, nextAddress)
            { Access = new AccountAccessProof(7, UserId, 1), Request = nextRequest });
        _accounts.SwapConfirmedAddressAsync(UserId, nextAddress, SwapPrecondition.None, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _account = _account with { Email = nextAddress };
                return Result.Success(new AddressSwapped(NewEmail));
            });

        var second = await ConfirmThroughPipelineAsync(new ConfirmEmailChangeCommand(nextGrant.Reveal(), nextAddress)
        { ReplacementLifetime = SessionLifetime.Persistent });
        var oldSession = await Sessions.CreateCommittedAsync(originalAuthorization, SessionLifetime.Persistent, Ct);
        var freshSession = await Sessions.CreateCommittedAsync(second.Value.Authorization, SessionLifetime.Persistent, Ct);

        second.Value.Authorization.AccessRevision.ShouldBe(2);
        _account.CredentialCutoff.ShouldBe(8);
        oldSession.ShouldBeNull();
        freshSession.ShouldNotBeNull().AccessRevision.ShouldBe(2);
        originalAuthorization.AccessRevision.ShouldBe(1);
        _sessions.CreatedProofs[0].ShouldBe(new AccountAccessProof(7, UserId, 1)
        {
            ExpectedEmail = NewEmail,
            ExpectedCutoff = 7,
        });
        _sessions.CreatedProofs[1].ShouldBe(new AccountAccessProof(8, UserId, 2)
        {
            ExpectedEmail = nextAddress,
            ExpectedCutoff = 8,
        });
        _coordinator.Commits.ShouldBe(2);
        _handlerInvocations.ShouldBe(2);
    }

    [Theory]
    [InlineData(SessionLifetime.Session, SessionLifetime.Persistent)]
    [InlineData(SessionLifetime.Persistent, SessionLifetime.Session)]
    public async Task CreateCommittedAsync_ShouldKeepTheValidatedSessionsLifetime_AndRejectAnotherLifetimeBeforeTheAdapter(
        SessionLifetime originalLifetime, SessionLifetime otherLifetime)
    {
        await SeedProfileAsync();
        var originalSession = new Session(SessionId.Generate(), UserId, Clock.UtcNow,
            Clock.UtcNow.AddHours(8), originalLifetime);
        // The endpoint copies the already validated session's profile into this command. The
        // committed authority must keep that original profile when transport issues its replacement.
        var result = await ConfirmThroughPipelineAsync(new ConfirmEmailChangeCommand(Grant.Reveal(), NewEmail)
        { ReplacementLifetime = originalSession.Lifetime });

        await Should.ThrowAsync<InvalidOperationException>(() => Sessions.CreateCommittedAsync(
            result.Value.Authorization, otherLifetime, Ct));

        result.Value.Authorization.Lifetime.ShouldBe(originalLifetime);
        _sessions.CreatedProofs.ShouldBeEmpty();
        var replacement = await Sessions.CreateCommittedAsync(result.Value.Authorization, originalLifetime, Ct);
        replacement.ShouldNotBeNull().Lifetime.ShouldBe(originalLifetime);
        replacement.AccessRevision.ShouldBe(1);
        _sessions.CreatedProofs.ShouldHaveSingleItem();
        result.Value.Authorization.Lifetime.ShouldBe(originalLifetime);
        _coordinator.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_ShouldNeverAuthorizeOrReplay_WhenTheCommitOutcomeIsUnknown()
    {
        await SeedProfileAsync();
        var uncertain = new AccountAccessCommitUncertainException(new TimeoutException());
        _coordinator.CommitFailure = uncertain;

        var thrown = await Should.ThrowAsync<AccountAccessCommitUncertainException>(() => ConfirmThroughPipelineAsync());
        await Should.ThrowAsync<InvalidOperationException>(
            () => Sessions.CreateCommittedAsync(_issued.ShouldNotBeNull(), SessionLifetime.Persistent, Ct));

        thrown.ShouldBeSameAs(uncertain);
        _coordinator.Commits.ShouldBe(0);
        _coordinator.HasActiveScope.ShouldBeFalse();
        _sessions.CreatedProofs.ShouldBeEmpty();
        _handlerInvocations.ShouldBe(1);
        await _swap.NotifyCommittedAsync(Ct);
        await _sender.DidNotReceiveWithAnyArgs().SendEmailChangedNotificationAsync(default!, Ct);
    }

    [Fact]
    public async Task Handle_ShouldRejectAnOldOriginalGrant_WithoutIssuingACapabilityOrWritingSuccessAudit()
    {
        await SeedProfileAsync();
        // After the original flow at epoch six, suspend and reinstate advance the global epoch to seven and eight.
        _epoch = 8;
        _account = _account with { AccessRevision = 2, CredentialCutoff = 8 };
        _currentUser.AccessRevision.Returns(2L);
        _coordinator.BeforeCommit = null;

        var result = await ConfirmThroughPipelineAsync();

        result.Error.Code.ShouldBe(AuthErrorCodes.EmailChangeGrantUnusable);
        _issued.ShouldBeNull();
        _sessions.CreatedProofs.ShouldBeEmpty();
        _save.SawEmailAudit.ShouldBeFalse();
        await _accounts.DidNotReceiveWithAnyArgs().SwapConfirmedAddressAsync(default, default!, default!, Ct);
        await _sender.DidNotReceiveWithAnyArgs().SendEmailChangedNotificationAsync(default!, Ct);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_ShouldCleanupTheCapturedDeletionRevision_AfterKnownCommitAndScopeDisposal(bool suspended)
    {
        var cleanup = Substitute.For<IAccountAccessCleanup>();
        var cleanupCallbacks = 0;
        var commitsObservedByCleanup = -1;
        bool? activeScopeObservedByCleanup = null;
        cleanup.CompleteAsync(Arg.Any<AccountAccessChanged>(), Arg.Is<CancellationToken>(token => !token.CanBeCanceled)).Returns(_ =>
        {
            cleanupCallbacks++;
            commitsObservedByCleanup = _coordinator.Commits;
            activeScopeObservedByCleanup = _coordinator.HasActiveScope;
            return Task.CompletedTask;
        });

        var result = await ScheduleDeletionThroughPipelineAsync(cleanup, suspended);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeSameAs(_deletionScheduled);
        result.Value.AccessRevision.ShouldBe(4);
        cleanupCallbacks.ShouldBe(1, "the callback must observe the state during cleanup");
        commitsObservedByCleanup.ShouldBe(1);
        activeScopeObservedByCleanup.ShouldBe(false);
        await cleanup.Received(1).CompleteAsync(new AccountAccessChanged(
            _deletionTargetId, suspended, AccessRevision: 4, PendingDeletion: true), Arg.Is<CancellationToken>(token => !token.CanBeCanceled));
        await cleanup.Received(1).CompleteAsync(Arg.Any<AccountAccessChanged>(), Arg.Any<CancellationToken>());
        await cleanup.DidNotReceive().CompleteAsync(
            Arg.Is<AccountAccessChanged>(change => change.UserId == UserId), Arg.Any<CancellationToken>());
        _handlerInvocations.ShouldBe(1);
        _coordinator.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_ShouldNeverCleanupOrReplayDeletion_WhenTheCommitOutcomeIsUnknown()
    {
        var cleanup = Substitute.For<IAccountAccessCleanup>();
        var uncertain = new AccountAccessCommitUncertainException(new TimeoutException());
        _coordinator.CommitFailure = uncertain;

        var actual = await Should.ThrowAsync<AccountAccessCommitUncertainException>(
            () => ScheduleDeletionThroughPipelineAsync(cleanup));

        actual.ShouldBeSameAs(uncertain);
        _deletionScheduled.ShouldNotBeNull();
        _handlerInvocations.ShouldBe(1);
        _coordinator.Commits.ShouldBe(0);
        _coordinator.HasActiveScope.ShouldBeFalse();
        await cleanup.DidNotReceiveWithAnyArgs().CompleteAsync(default!, Ct);
    }

    private async Task<Result<AccountDeletionScheduled>> ScheduleDeletionThroughPipelineAsync(
        IAccountAccessCleanup cleanup, bool suspended = false)
    {
        _db.JobSeekers.AddRange(
            JobSeeker.Register(UserId, TermsAcceptance.AcceptCurrent(Clock), Clock).Value,
            JobSeeker.Register(_deletionTargetId, TermsAcceptance.AcceptCurrent(Clock), Clock).Value);
        await _db.SaveChangesAsync(Ct);
        var targetAccount = AccountAccessTestKit.Account(_deletionTargetId, "target@example.test") with
        {
            IsSuspended = suspended,
            AccessRevision = 3,
            CredentialCutoff = 6,
        };
        var deletionEpoch = 6L;
        var reader = AccountAccessTestKit.Reader(id =>
        {
            if (id != UserId && id != _deletionTargetId)
                return null;
            var profile = _db.JobSeekers.IgnoreQueryFilters().AsNoTracking().Single(value => value.UserId == id);
            var account = id == UserId ? AccountAccessTestKit.Account(UserId, OldEmail) : targetAccount;
            return account with
            {
                IsAdmin = id == UserId,
                DeletedAt = profile.DeletedAt,
            };
        });
        reader.ReadEpochAsync(Arg.Any<CancellationToken>()).Returns(_ => deletionEpoch);
        var writer = Substitute.For<IAccountAccessWriter>();
        writer.CanRemoveAccessAsync(_deletionTargetId, Ct).Returns(true);
        writer.AdvanceDeletionAsync(_deletionTargetId, Ct)
            .Returns(_ =>
            {
                targetAccount = targetAccount with { AccessRevision = 4, CredentialCutoff = ++deletionEpoch };
                return targetAccount;
            });
        var scheduler = new AccountDeletionScheduler(_db, Clock, reader, writer, Substitute.For<IExternalLoginEraser>());
        var handler = new ScheduleAccountDeletionCommandHandler(scheduler, _currentUser);
        var command = new ScheduleAccountDeletionCommand(_deletionTargetId, Grant.Reveal());
        var audit = new AuditBehavior<ScheduleAccountDeletionCommand, Result<AccountDeletionScheduled>>(
            _db, _currentUser, Clock, Substitute.For<ICorrelationIdProvider>(),
            Substitute.For<IRequestContextProvider>(), Substitute.For<IIdentifierPseudonymizer>());
        var unitOfWork = new UnitOfWorkBehavior<ScheduleAccountDeletionCommand, Result<AccountDeletionScheduled>>(
            _db, NullLogger<UnitOfWorkBehavior<ScheduleAccountDeletionCommand, Result<AccountDeletionScheduled>>>.Instance);
        var access = new AccountAccessMutationBehavior<ScheduleAccountDeletionCommand, Result<AccountDeletionScheduled>>(
            [_coordinator], [reader], [cleanup], _currentUser, []);
        _coordinator.BeforeCommit = () =>
        {
            var entry = _db.AuditLogEntries.Single(value => value.EventType == "Admin.AccountDeletionScheduled");
            entry.UserId.ShouldBe(UserId);
            entry.AggregateId.ShouldBe(_deletionTargetId);
            _deletionScheduled.ShouldNotBeNull();
            cleanup.DidNotReceiveWithAnyArgs().CompleteAsync(default!, Ct);
        };
        MessageHandlerDelegate<ScheduleAccountDeletionCommand, Result<AccountDeletionScheduled>> handle = async (message, token) =>
        {
            _handlerInvocations++;
            var result = await handler.Handle(message, token);
            if (result.IsSuccess)
                _deletionScheduled = result.Value;
            await cleanup.DidNotReceiveWithAnyArgs().CompleteAsync(default!, Ct);
            return result;
        };
        return await access.Handle(command,
            (message, token) => unitOfWork.Handle(message,
                (audited, cancellation) => audit.Handle(audited, handle, cancellation), token), Ct);
    }

    private sealed class SaveProbe : SaveChangesInterceptor
    {
        public bool SawEmailAudit { get; set; }
        public bool FailEmailAudit { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var audit = eventData.Context?.ChangeTracker.Entries<Jobbliggaren.Domain.Auditing.AuditLogEntry>()
                .Any(entry => entry.State == EntityState.Added && entry.Entity.EventType == "User.EmailChanged") == true;
            SawEmailAudit |= audit;
            if (audit && FailEmailAudit)
                throw new DbUpdateException("Injected failure saving the address-change audit.");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class SessionProbe(Func<AccountAccessSnapshot> current) : ISessionStore
    {
        private readonly List<AccountAccessProof> _createdProofs = [];
        public List<AccountAccessProof> CreatedProofs => _createdProofs;

        public Task<Session?> CreateAsync(Guid userId, AccountAccessProof proof, SessionLifetime lifetime, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _createdProofs.Add(proof);
            if (userId != current().UserId || !proof.Admits(current()))
                return Task.FromResult<Session?>(null);
            return Task.FromResult<Session?>(new Session(SessionId.Generate(), userId, Clock.UtcNow,
                Clock.UtcNow.AddDays(30), lifetime, proof.AccessRevision!.Value));
        }

        public Task<Session?> GetAsync(SessionId sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<Session> CreateAsync(Guid userId, SessionLifetime lifetime, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> InvalidateBeforeRevisionAsync(Guid userId, long accessRevision, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRotation?> RotateAsync(SessionId current, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> InvalidateAsync(SessionId sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> InvalidateAllForUserAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task MarkUserDeletedAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
    }
}

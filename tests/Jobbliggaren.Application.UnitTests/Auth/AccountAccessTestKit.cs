using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Common.Abstractions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Jobbliggaren.Application.UnitTests.Auth;

internal static class AccountAccessTestKit
{
    public static AccountAccessProof Bound(Guid userId) => new(0, userId, 0);

    public static IAccountAccessReader Reader(Guid userId, string email) =>
        Reader(id => id == userId ? Account(userId, email) : null);

    public static IAccountAccessReader Reader(Func<Guid, AccountAccessSnapshot?> account)
    {
        var reader = Substitute.For<IAccountAccessReader>();
        reader.ReadEpochAsync(Arg.Any<CancellationToken>()).Returns(0L);
        reader.ReadAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => account(call.Arg<Guid>()));
        return reader;
    }

    public static IAccountAccessReader ReaderFromProfiles(IAppDbContext db, Func<Guid, string?> email,
        Func<Guid, long>? revision = null, Func<Guid, long>? cutoff = null, Func<Guid, bool>? inboxConfirmed = null) =>
        Reader(userId =>
        {
            var address = email(userId);
            if (address is null)
                return null;
            var profile = db.JobSeekers.IgnoreQueryFilters().AsNoTracking()
                .Where(seeker => seeker.UserId == userId)
                .Select(seeker => new { seeker.DeletedAt })
                .FirstOrDefault();
            return Account(userId, address) with
            {
                HasProfile = profile is not null,
                DeletedAt = profile?.DeletedAt,
                AccessRevision = revision?.Invoke(userId) ?? 0,
                CredentialCutoff = cutoff?.Invoke(userId) ?? 0,
                InboxConfirmed = inboxConfirmed?.Invoke(userId) ?? true,
            };
        });

    public static IAccountAccessWriter Advancer(IAccountAccessReader reader, IAccountAccessCoordinator coordinator,
        Action<AccountAccessSnapshot>? publish = null, Func<long>? advanceEpoch = null)
    {
        var writer = Substitute.For<IAccountAccessWriter>();
        writer.AdvanceCredentialsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            var userId = call.Arg<Guid>();
            var ct = call.Arg<CancellationToken>();
            if (!coordinator.Holds(userId))
                throw new InvalidOperationException("A credential transition requires its protected account scope.");
            if (coordinator is RecordingAccountAccessCoordinator recording && !recording.HoldsLifecycle)
                throw new InvalidOperationException("A credential transition requires the lifecycle lock first.");
            var current = await reader.ReadAsync(userId, ct);
            if (current?.CanAuthenticate != true)
                throw new InvalidOperationException("A credential transition requires a live admitted account.");
            // These fixtures transition one account. Other-account epoch jumps are exercised separately.
            var transition = current with
            {
                AccessRevision = checked(current.AccessRevision + 1),
                CredentialCutoff = advanceEpoch?.Invoke() ?? checked(current.CredentialCutoff + 1),
            };
            publish?.Invoke(transition);
            return transition;
        });
        return writer;
    }

    public static AccountAccessSnapshot Account(Guid userId, string email) =>
        new(userId, email, IsSuspended: false, AccessRevision: 0, CredentialCutoff: 0,
            HasProfile: true, DeletedAt: null, IsAdmin: false);

    public static IAccountAccessWriter OrdinaryRemoval(ICurrentUser currentUser)
    {
        var writer = Substitute.For<IAccountAccessWriter>();
        if (currentUser.UserId is { } userId)
            writer.CanRemoveAccessAsync(userId, Arg.Any<CancellationToken>()).Returns(true);
        return writer;
    }

    public static RecordingAccountAccessCoordinator Coordinator() => new();

    internal sealed class RecordingAccountAccessCoordinator : IAccountAccessCoordinator, IAsyncDisposable
    {
        private Scope? _active;
        private readonly List<ScopeRequest> _begunScopes = [];
        public bool HasActiveScope => _active is not null;
        public bool HasLifecycleScope => HoldsLifecycle;
        public bool HoldsLifecycle => _active?.Lifecycle == true;
        public Exception? CommitFailure { get; set; }
        public Action? BeforeCommit { get; set; }
        public IReadOnlyList<ScopeRequest> BegunScopes => _begunScopes;
        public int Commits { get; private set; }
        public int Rollbacks { get; private set; }
        public bool Holds(Guid userId) => _active?.UserIds.Contains(userId) == true;

        public ValueTask DisposeAsync() => _active?.DisposeAsync() ?? ValueTask.CompletedTask;

        public Task<IAccountAccessScope> BeginAsync(
            IReadOnlyCollection<Guid> userIds, bool lifecycle, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _begunScopes.Add(new ScopeRequest([.. userIds], lifecycle));
            if (_active is { } active)
            {
                if (userIds.Any(userId => !active.UserIds.Contains(userId)))
                    throw new InvalidOperationException("A nested scope cannot extend its held accounts.");
                if (lifecycle && !active.Lifecycle)
                    throw new InvalidOperationException("A nested scope cannot acquire the lifecycle lock after account locks.");
                return Task.FromResult<IAccountAccessScope>(new Scope(this, userIds, lifecycle, ownsCommit: false));
            }
            _active = new Scope(this, userIds, lifecycle, ownsCommit: true);
            return Task.FromResult<IAccountAccessScope>(_active);
        }

        internal sealed record ScopeRequest(IReadOnlyCollection<Guid> UserIds, bool Lifecycle);

        private sealed class Scope(
            RecordingAccountAccessCoordinator owner, IReadOnlyCollection<Guid> userIds, bool lifecycle, bool ownsCommit)
            : IAccountAccessScope
        {
            private bool _committed;
            private bool _disposed;
            public IReadOnlyCollection<Guid> UserIds { get; } = userIds;
            public bool Lifecycle { get; } = lifecycle;
            public bool OwnsCommit { get; } = ownsCommit;

            public Task CommitAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (OwnsCommit)
                {
                    owner.BeforeCommit?.Invoke();
                    if (owner.CommitFailure is { } failure)
                        return Task.FromException(failure);
                    owner.Commits++;
                    _committed = true;
                }
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync()
            {
                if (!_disposed && OwnsCommit)
                {
                    if (!_committed)
                        owner.Rollbacks++;
                    owner._active = null;
                }
                _disposed = true;
                return ValueTask.CompletedTask;
            }
        }
    }
}

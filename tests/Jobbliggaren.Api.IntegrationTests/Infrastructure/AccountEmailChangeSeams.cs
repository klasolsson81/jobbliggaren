using Jobbliggaren.Application.Auth.AccountEmailChanges;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Infrastructure.Auth.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Jobbliggaren.Api.IntegrationTests.Infrastructure;

/// <summary>
/// #1975 — the address change's store, out of reach while <see cref="LoginChallengeFaults"/> is: the same volatile
/// instance and the same fault the adapter's own contract throws.
/// </summary>
internal sealed class FaultableAccountEmailChangeStore(IAccountEmailChangeStore inner, LoginChallengeFaults faults)
    : IAccountEmailChangeStore
{
    public Task<AccountEmailChangePut> PutAsync(NewAccountEmailChange change, CancellationToken ct)
    {
        faults.ThrowIfUnavailable();
        return inner.PutAsync(change, ct);
    }

    public Task RevokeAsync(AccountEmailChangeReceipt receipt, CancellationToken ct)
    {
        faults.ThrowIfUnavailable();
        return inner.RevokeAsync(receipt, ct);
    }

    public Task<AccountEmailChangeVerdict> ConsumeAsync(
        string newEmail, string currentEmail, LoginCode code, CancellationToken ct)
    {
        faults.ThrowIfUnavailable();
        return inner.ConsumeAsync(newEmail, currentEmail, code, ct);
    }

    public Task<bool> CancelAsync(Guid userId, CancellationToken ct)
    {
        faults.ThrowIfUnavailable();
        return inner.CancelAsync(userId, ct);
    }

    public Task<PendingAccountEmailChange?> FindPendingAsync(Guid userId, CancellationToken ct)
    {
        faults.ThrowIfUnavailable();
        return inner.FindPendingAsync(userId, ct);
    }
}

/// <summary>
/// #1975 (security-auditor T-9) — makes the session store's invalidation of one account fail for a scope, so the
/// completion's teardown can be driven to fail over a committed change. Only that account and only that call: every
/// other session operation of the shared host passes straight through. The fault is the one the production decorator
/// throws for an unreachable Redis, which the Api would answer 503 if it ever escaped the endpoint.
/// </summary>
internal sealed class SessionTeardownFaults
{
    private Guid? _userId;

    internal IDisposable FailingFor(Guid userId)
    {
        _userId = userId;
        return new Scope(this);
    }

    internal void ThrowIfFailing(Guid userId)
    {
        if (_userId == userId)
            throw new SessionStoreUnavailableException(
                "The session store refused the invalidation.", new InvalidOperationException("RedisConnectionException"));
    }

    private sealed class Scope(SessionTeardownFaults owner) : IDisposable
    {
        public void Dispose() => owner._userId = null;
    }
}

internal sealed class FaultableSessionStore(ISessionStore inner, SessionTeardownFaults faults) : ISessionStore
{
    public Task<Session?> GetAsync(SessionId sessionId, CancellationToken ct) => inner.GetAsync(sessionId, ct);

    public Task<Session> CreateAsync(Guid userId, SessionLifetime lifetime, CancellationToken ct) =>
        inner.CreateAsync(userId, lifetime, ct);

    public Task<SessionRotation?> RotateAsync(SessionId current, CancellationToken ct) => inner.RotateAsync(current, ct);

    public Task<bool> InvalidateAsync(SessionId sessionId, CancellationToken ct) => inner.InvalidateAsync(sessionId, ct);

    public Task<int> InvalidateAllForUserAsync(Guid userId, CancellationToken ct)
    {
        faults.ThrowIfFailing(userId);
        return inner.InvalidateAllForUserAsync(userId, ct);
    }

    public Task MarkUserDeletedAsync(Guid userId, CancellationToken ct) => inner.MarkUserDeletedAsync(userId, ct);
}

/// <summary>
/// #1975 — refuses the save that carries an audit row of one event type for one account, for a scope: the state a
/// database fault leaves after the swap has committed. Every other save passes straight through.
/// </summary>
internal sealed class AuditRowSaveFailure : SaveChangesInterceptor
{
    private (string EventType, Guid UserId)? _armed;

    internal IDisposable FailingFor(string eventType, Guid userId)
    {
        _armed = (eventType, userId);
        return new Scope(this);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (_armed is { } armed && eventData.Context is { } context
            && context.ChangeTracker.Entries<AuditLogEntry>().Any(entry =>
                entry.State == EntityState.Added
                && entry.Entity.EventType == armed.EventType
                && entry.Entity.UserId == armed.UserId))
        {
            throw new DbUpdateException("The audit row could not be written.");
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private sealed class Scope(AuditRowSaveFailure owner) : IDisposable
    {
        public void Dispose() => owner._armed = null;
    }
}

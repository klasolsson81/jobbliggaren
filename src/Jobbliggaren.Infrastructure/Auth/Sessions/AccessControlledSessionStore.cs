using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Common.Abstractions;

namespace Jobbliggaren.Infrastructure.Auth.Sessions;

public sealed class AccessControlledSessionStore(
    ISessionStore inner,
    IAccountAccessReader access,
    IAccountAccessCoordinator coordinator) : ISessionStore
{
    public async Task<Session?> GetAsync(SessionId sessionId, CancellationToken ct)
    {
        var session = await inner.GetAsync(sessionId, ct);
        if (session is null)
            return null;
        var account = await access.ReadAsync(session.UserId, ct);
        return account is { CanAuthenticate: true } && account.AccessRevision == session.AccessRevision
            ? session : null;
    }

    public async Task<Session> CreateAsync(Guid userId, SessionLifetime lifetime, CancellationToken ct) =>
        await CreateAsync(userId, AccountAccessProof.Legacy, lifetime, ct)
        ?? throw new InvalidOperationException("A legacy session cannot be issued for this account.");

    public async Task<Session?> CreateAsync(
        Guid userId, AccountAccessProof proof, SessionLifetime lifetime, CancellationToken ct)
    {
        if (coordinator.HasActiveScope)
            throw new InvalidOperationException("Session issuance requires the preceding database mutation to be committed.");
        await using var scope = await coordinator.BeginAsync([userId], false, ct);
        var account = await access.ReadAsync(userId, ct);
        if (account is null || !proof.Admits(account))
            return null;
        var session = await inner.CreateAsync(userId, proof.Bind(account), lifetime, ct);
        // A lost lock backend must refuse the return even if Redis accepted the write.
        var after = await access.ReadAsync(userId, ct);
        if (session is null || after is null || !proof.Admits(after)
            || session.AccessRevision != after.AccessRevision)
            return null;
        await scope.CommitAsync(ct);
        return session;
    }

    public async Task<SessionRotation?> RotateAsync(SessionId current, CancellationToken ct)
    {
        if (coordinator.HasActiveScope)
            throw new InvalidOperationException("Session rotation cannot borrow a database mutation scope.");
        var session = await GetAsync(current, ct);
        if (session is null)
            return null;
        await using var scope = await coordinator.BeginAsync([session.UserId], false, ct);
        var before = await access.ReadAsync(session.UserId, ct);
        if (before is not { CanAuthenticate: true } || before.AccessRevision != session.AccessRevision)
            return null;
        var rotation = await inner.RotateAsync(current, ct);
        var after = await access.ReadAsync(session.UserId, ct);
        if (after is not { CanAuthenticate: true } || after.AccessRevision != session.AccessRevision)
            return null;
        await scope.CommitAsync(ct);
        return rotation;
    }

    public Task<bool> InvalidateAsync(SessionId sessionId, CancellationToken ct) => inner.InvalidateAsync(sessionId, ct);
    public Task<int> InvalidateAllForUserAsync(Guid userId, CancellationToken ct) => inner.InvalidateAllForUserAsync(userId, ct);
    public Task<int> InvalidateBeforeRevisionAsync(Guid userId, long accessRevision, CancellationToken ct) =>
        inner.InvalidateBeforeRevisionAsync(userId, accessRevision, ct);
    public Task MarkUserDeletedAsync(Guid userId, CancellationToken ct) => inner.MarkUserDeletedAsync(userId, ct);
}

using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Infrastructure.Auth.Access;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Jobbliggaren.Api.IntegrationTests.Infrastructure;

internal sealed class AccountLifecycleRaceGate(
    Guid firstParticipantId, string firstPath, string secondPath, Guid? secondParticipantId = null) : IDisposable
{
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _claimed;

    internal TaskCompletionSource<HeldTransaction> FirstHeld { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<HeldTransaction> SecondHeld { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource SecondAttempted { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal bool Matches(IReadOnlyCollection<Guid> ids, bool lifecycle, string? path) =>
        lifecycle && (MatchesFirst(ids, path) || MatchesSecond(ids, path));

    private bool MatchesFirst(IReadOnlyCollection<Guid> ids, string? path) =>
        path == firstPath && ids.Contains(firstParticipantId);

    private bool MatchesSecond(IReadOnlyCollection<Guid> ids, string? path) =>
        path == secondPath && ids.Contains(secondParticipantId ?? firstParticipantId);

    internal void BeforeBegin(IReadOnlyCollection<Guid> ids, string path)
    {
        if (MatchesSecond(ids, path))
            SecondAttempted.TrySetResult();
    }

    internal async Task AfterBeginAsync(
        IReadOnlyCollection<Guid> ids, string path, HeldTransaction transaction, CancellationToken ct)
    {
        if (MatchesSecond(ids, path))
            SecondHeld.TrySetResult(transaction);
        if (!MatchesFirst(ids, path) || Interlocked.CompareExchange(ref _claimed, 1, 0) != 0)
            return;
        FirstHeld.TrySetResult(transaction);
        await _release.Task.WaitAsync(ct);
    }

    internal void Release() => _release.TrySetResult();

    public void Dispose()
    {
        Release();
        GC.SuppressFinalize(this);
    }

    internal sealed record HeldTransaction(bool Lifecycle, bool HoldsTarget, bool SameConnection, bool SameTransaction);
}

internal sealed class LifecycleRaceCoordinator(
    SqlAccountAccess inner,
    AppDbContext app,
    AppIdentityDbContext identity,
    IHttpContextAccessor http,
    AccountLifecycleRaceGate gate) : IAccountAccessCoordinator
{
    public bool HasActiveScope => inner.HasActiveScope;
    public bool HasLifecycleScope => inner.HasLifecycleScope;
    public bool Holds(Guid userId) => inner.Holds(userId);

    public async Task<IAccountAccessScope> BeginAsync(
        IReadOnlyCollection<Guid> userIds, bool lifecycle, CancellationToken cancellationToken)
    {
        var path = http.HttpContext?.Request.Path.Value;
        if (!gate.Matches(userIds, lifecycle, path) || inner.HasActiveScope)
            return await inner.BeginAsync(userIds, lifecycle, cancellationToken);

        gate.BeforeBegin(userIds, path!);
        var scope = await inner.BeginAsync(userIds, lifecycle, cancellationToken);
        try
        {
            var appTransaction = app.Database.CurrentTransaction?.GetDbTransaction();
            var identityTransaction = identity.Database.CurrentTransaction?.GetDbTransaction();
            var held = new AccountLifecycleRaceGate.HeldTransaction(
                inner.HasLifecycleScope,
                userIds.All(inner.Holds),
                ReferenceEquals(app.Database.GetDbConnection(), identity.Database.GetDbConnection()),
                appTransaction is not null && ReferenceEquals(appTransaction, identityTransaction));
            await gate.AfterBeginAsync(userIds, path!, held, cancellationToken);
            return scope;
        }
        catch
        {
            await scope.DisposeAsync();
            throw;
        }
    }
}

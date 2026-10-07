using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.AccountEmailChanges;
using Mediator;

namespace Jobbliggaren.Application.Admin.Accounts.Queries.GetPendingAccountEmailChange;

public sealed class GetPendingAccountEmailChangeQueryHandler(
    IAccountEmailChangeStore store, IAccountAccessReader access,
    IAccountAccessCoordinator coordinator, IAccountEmailChangeRequests requests)
    : IQueryHandler<GetPendingAccountEmailChangeQuery, PendingAccountEmailChangeDto?>
{
    public async ValueTask<PendingAccountEmailChangeDto?> Handle(
        GetPendingAccountEmailChangeQuery query, CancellationToken cancellationToken)
    {
        await using var scope = await coordinator.BeginAsync([query.UserId], false, cancellationToken);
        var pending = await store.FindPendingAsync(query.UserId, cancellationToken);
        if (pending is null)
            return null;
        var account = await access.ReadAsync(query.UserId, cancellationToken);
        if (account is not { CanAuthenticate: true } || pending.AccessRevision != account.AccessRevision
            || pending.RequestId is not { } requestId || requestId == Guid.Empty
            || pending.ExpiresAt - pending.IssuedAt != AccountEmailChangePolicy.Ttl
            || !await requests.HasCommittedRequestAsync(
                query.UserId, requestId, pending.IssuedAt, pending.ExpiresAt, cancellationToken))
            return null;
        await scope.CommitAsync(cancellationToken);
        return new PendingAccountEmailChangeDto(pending.State, pending.CompletableFrom, pending.ExpiresAt);
    }
}

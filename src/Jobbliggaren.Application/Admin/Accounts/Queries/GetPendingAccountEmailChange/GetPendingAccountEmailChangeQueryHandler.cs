using Jobbliggaren.Application.Auth.AccountEmailChanges;
using Mediator;

namespace Jobbliggaren.Application.Admin.Accounts.Queries.GetPendingAccountEmailChange;

public sealed class GetPendingAccountEmailChangeQueryHandler(IAccountEmailChangeStore store)
    : IQueryHandler<GetPendingAccountEmailChangeQuery, PendingAccountEmailChangeDto?>
{
    public async ValueTask<PendingAccountEmailChangeDto?> Handle(
        GetPendingAccountEmailChangeQuery query, CancellationToken cancellationToken) =>
        await store.FindPendingAsync(query.UserId, cancellationToken) is { } pending
            ? new PendingAccountEmailChangeDto(pending.State, pending.CompletableFrom, pending.ExpiresAt)
            : null;
}

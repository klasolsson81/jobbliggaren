using Mediator;

namespace Jobbliggaren.Application.Admin.Accounts.Queries.CountAccountsByStatus;

public sealed class CountAccountsByStatusQueryHandler(IAccountDirectory directory)
    : IQueryHandler<CountAccountsByStatusQuery, AccountStatusCountsDto>
{
    public async ValueTask<AccountStatusCountsDto> Handle(
        CountAccountsByStatusQuery query, CancellationToken cancellationToken)
    {
        var counts = await directory.CountByStatusAsync(query.Address, cancellationToken, query.RegisteredFrom, query.RegisteredBefore);
        return new AccountStatusCountsDto(counts.Total, counts.Active, counts.PendingDeletion, counts.ProfileMissing, counts.Suspended);
    }
}

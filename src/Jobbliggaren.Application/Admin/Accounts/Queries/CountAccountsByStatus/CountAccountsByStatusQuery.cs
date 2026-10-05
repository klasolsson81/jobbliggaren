using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.Admin.Accounts.Queries.CountAccountsByStatus;

/// <summary>
/// How many accounts match the address term, in all and per status (ADR 0151). The counts follow the term
/// but not the status filter, so each filter option shows how many rows choosing it would list.
/// </summary>
public sealed record CountAccountsByStatusQuery(string? Address) : IQuery<AccountStatusCountsDto>, IAdminRequest
{
    public override string ToString() =>
        $"CountAccountsByStatusQuery(address {(string.IsNullOrWhiteSpace(Address) ? "none" : "redacted")})";
}

public sealed record AccountStatusCountsDto(int Total, int Active, int PendingDeletion, int ProfileMissing);

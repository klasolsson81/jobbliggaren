using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.Admin.Accounts.Queries.SearchAccounts;

/// <summary>
/// One page of all accounts, searched by a substring of the address and filtered by status (ADR 0151).
/// The search never records a recent search: the term names a person.
/// </summary>
public sealed record SearchAccountsQuery(
    string? Address,
    AccountStatus? Status,
    AccountSort Sort,
    int Page,
    int PageSize)
    : IQuery<PagedResult<AccountListItemDto>>, IAdminRequest
{
    public const int MaxPageSize = 100;

    /// <summary>The largest page whose offset, <c>(Page - 1) * PageSize</c>, an int holds at any page size.</summary>
    public const int MaxPage = int.MaxValue / MaxPageSize;

    public override string ToString() =>
        $"SearchAccountsQuery(address {(string.IsNullOrWhiteSpace(Address) ? "none" : "redacted")}, "
        + $"status {Status?.ToString() ?? "any"}, {Sort}, page {Page}/{PageSize})";
}

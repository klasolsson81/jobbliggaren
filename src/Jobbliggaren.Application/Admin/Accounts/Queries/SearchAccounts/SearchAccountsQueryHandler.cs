using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.JobSeekers;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Admin.Accounts.Queries.SearchAccounts;

/// <summary>
/// The page comes from the directory. Its application counts come from <see cref="IAppDbContext"/>, whose
/// query filters are the one definition of a live application, in one grouped read over the page's active
/// profiles.
/// </summary>
public sealed class SearchAccountsQueryHandler(IAccountDirectory directory, IAppDbContext db)
    : IQueryHandler<SearchAccountsQuery, PagedResult<AccountListItemDto>>
{
    public async ValueTask<PagedResult<AccountListItemDto>> Handle(
        SearchAccountsQuery query, CancellationToken cancellationToken)
    {
        var page = await directory.SearchAsync(
            new AccountDirectorySearch(query.Address, query.Status, query.Sort, query.Page, query.PageSize),
            cancellationToken);

        var active = page.Entries
            .Where(entry => entry.Status == AccountStatus.Active)
            .Select(entry => entry.JobSeekerId)
            .OfType<JobSeekerId>()
            .ToList();

        var applications = active.Count == 0
            ? new Dictionary<JobSeekerId, int>()
            : await db.Applications
                .AsNoTracking()
                .Where(application => active.Contains(application.JobSeekerId))
                .GroupBy(application => application.JobSeekerId)
                .Select(group => new { group.Key, Count = group.Count() })
                .ToDictionaryAsync(row => row.Key, row => row.Count, cancellationToken);

        var items = page.Entries
            .Select(entry => new AccountListItemDto(
                entry.UserId,
                entry.Email,
                entry.IsAdmin ? AccountRole.Admin : AccountRole.User,
                entry.Status,
                entry.EmailConfirmed,
                entry.RegisteredAt,
                entry.PermanentDeletionEarliest,
                entry.Status == AccountStatus.Active && entry.JobSeekerId is { } id
                    ? applications.GetValueOrDefault(id)
                    : null, entry.IsSuspended)
            { Deletion = entry.Deletion })
            .ToList();

        return new PagedResult<AccountListItemDto>(items, page.TotalCount, query.Page, query.PageSize);
    }
}

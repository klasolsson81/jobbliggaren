using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Waitlist.Dtos;
using Jobbliggaren.Domain.Waitlist;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Waitlist.Queries.ListWaitlistEntries;

public sealed class ListWaitlistEntriesQueryHandler(IAppDbContext db)
    : IQueryHandler<ListWaitlistEntriesQuery, IReadOnlyList<WaitlistEntryListItemDto>>
{
    private const int MaxItems = 500;

    public async ValueTask<IReadOnlyList<WaitlistEntryListItemDto>> Handle(
        ListWaitlistEntriesQuery query, CancellationToken cancellationToken)
    {
        var q = db.WaitlistEntries.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Status)
            && WaitlistStatus.TryFromName(query.Status, ignoreCase: true, out var status))
        {
            q = q.Where(w => w.Status == status);
        }

        var items = await q
            .OrderBy(w => w.RequestedAt)
            .Take(MaxItems)
            .ToListAsync(cancellationToken);

        return items.Select(WaitlistEntryListItemDto.From).ToList();
    }
}

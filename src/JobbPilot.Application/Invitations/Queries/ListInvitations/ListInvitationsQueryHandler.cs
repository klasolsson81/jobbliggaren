using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Invitations.Dtos;
using JobbPilot.Domain.Invitations;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Application.Invitations.Queries.ListInvitations;

public sealed class ListInvitationsQueryHandler(IAppDbContext db)
    : IQueryHandler<ListInvitationsQuery, IReadOnlyList<InvitationListItemDto>>
{
    private const int MaxItems = 200;

    public async ValueTask<IReadOnlyList<InvitationListItemDto>> Handle(
        ListInvitationsQuery query, CancellationToken cancellationToken)
    {
        var q = db.Invitations.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Status)
            && InvitationStatus.TryFromName(query.Status, ignoreCase: true, out var status))
        {
            q = q.Where(i => i.Status == status);
        }

        var items = await q
            .OrderByDescending(i => i.IssuedAt)
            .Take(MaxItems)
            .ToListAsync(cancellationToken);

        return items.Select(InvitationListItemDto.From).ToList();
    }
}

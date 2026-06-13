using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Invitations.Dtos;
using Mediator;

namespace Jobbliggaren.Application.Invitations.Queries.ListInvitations;

/// <summary>
/// Admin-listning av invitations. Filter via <paramref name="Status"/>
/// (Pending|Redeemed|Expired|Revoked). Returnerar nyaste först.
/// Begränsat till 200 senaste — paginering tilläggs om Klas ber om det
/// vid Fas 6 admin-UI.
/// </summary>
public sealed record ListInvitationsQuery(string? Status)
    : IQuery<IReadOnlyList<InvitationListItemDto>>, IAdminRequest;

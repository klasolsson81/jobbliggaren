using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Invitations.Dtos;
using Mediator;

namespace JobbPilot.Application.Invitations.Queries.ListInvitations;

/// <summary>
/// Admin-listning av invitations. Filter via <paramref name="Status"/>
/// (Pending|Redeemed|Expired|Revoked). Returnerar nyaste först.
/// Begränsat till 200 senaste — paginering tilläggs om Klas ber om det
/// vid Fas 6 admin-UI.
/// </summary>
public sealed record ListInvitationsQuery(string? Status)
    : IQuery<IReadOnlyList<InvitationListItemDto>>, IAdminRequest;

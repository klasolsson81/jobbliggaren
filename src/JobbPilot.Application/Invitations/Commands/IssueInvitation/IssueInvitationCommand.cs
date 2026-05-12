using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Invitations.Dtos;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Invitations.Commands.IssueInvitation;

/// <summary>
/// Admin-utfärdande av direktinvitation per ADR 0005 amendment 2026-05-12.
/// Origin = DirectInvite. Kräver SuperAdmin-roll. Default-giltighet: 7 dagar.
/// </summary>
public sealed record IssueInvitationCommand(string? Email, int? ValidForDays)
    : ICommand<Result<InvitationIssuedDto>>, IAdminRequest;

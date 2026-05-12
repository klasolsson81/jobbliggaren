using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Invitations.Commands.RevokeInvitation;

/// <summary>
/// Admin återkallar en pending Invitation. Bara Pending → Revoked.
/// </summary>
public sealed record RevokeInvitationCommand(Guid InvitationId)
    : ICommand<Result>, IAdminRequest;

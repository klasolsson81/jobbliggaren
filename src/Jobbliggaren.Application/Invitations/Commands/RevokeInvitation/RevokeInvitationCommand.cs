using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Invitations.Commands.RevokeInvitation;

/// <summary>
/// Admin återkallar en pending Invitation. Bara Pending → Revoked.
/// </summary>
public sealed record RevokeInvitationCommand(Guid InvitationId)
    : ICommand<Result>, IAdminRequest;

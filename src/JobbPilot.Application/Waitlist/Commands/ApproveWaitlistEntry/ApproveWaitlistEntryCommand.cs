using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Invitations.Dtos;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Waitlist.Commands.ApproveWaitlistEntry;

/// <summary>
/// Admin godkänner en pending waitlist-post. Skapar Invitation
/// (Origin=WaitlistApproved) i samma UoW och länkar den till waitlist-posten.
/// Returnerar InvitationIssuedDto (admin kan se token-utfärdande direkt;
/// plaintext-token finns bara i email-utskicket).
/// </summary>
public sealed record ApproveWaitlistEntryCommand(Guid WaitlistEntryId, int? ValidForDays)
    : ICommand<Result<InvitationIssuedDto>>, IAdminRequest;

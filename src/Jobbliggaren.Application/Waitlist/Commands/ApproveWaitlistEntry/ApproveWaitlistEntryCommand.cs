using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Invitations.Dtos;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Waitlist.Commands.ApproveWaitlistEntry;

/// <summary>
/// Admin godkänner en pending waitlist-post. Skapar Invitation
/// (Origin=WaitlistApproved) i samma UoW och länkar den till waitlist-posten.
/// Returnerar InvitationIssuedDto (admin kan se token-utfärdande direkt;
/// plaintext-token finns bara i email-utskicket).
/// </summary>
public sealed record ApproveWaitlistEntryCommand(Guid WaitlistEntryId, int? ValidForDays)
    : ICommand<Result<InvitationIssuedDto>>, IAdminRequest;

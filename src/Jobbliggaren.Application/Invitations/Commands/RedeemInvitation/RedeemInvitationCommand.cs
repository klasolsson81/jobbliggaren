using Jobbliggaren.Application.Auth.Dtos;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Invitations.Commands.RedeemInvitation;

/// <summary>
/// Inlösen av invitation-token + registrering av konto i ett steg.
/// Email hämtas från Invitation-aggregaten (inte från command body) för att
/// förhindra token-stöld där angripare lurar offer klicka länk + komprometterar
/// offers konto. Returnerar SessionDto — samma resultat som RegisterCommand.
/// Anonym endpoint (ingen IAuthenticatedRequest).
/// </summary>
public sealed record RedeemInvitationCommand(
    string? Token,
    string? Password,
    string? DisplayName) : ICommand<Result<SessionDto>>;

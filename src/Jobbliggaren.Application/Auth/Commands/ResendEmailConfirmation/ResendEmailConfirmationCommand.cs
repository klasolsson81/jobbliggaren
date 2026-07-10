using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.ResendEmailConfirmation;

/// <summary>
/// #733 — re-issue a registration email-confirmation link for an unconfirmed account. A fast-follow to
/// #714: with login gated on <c>EmailConfirmed</c> and a 24h token lifespan, a user whose token expired
/// or who lost the email is otherwise stuck at login. ALWAYS resolves to a uniform 202 (a malformed email
/// is the only 400 — existence-INDEPENDENT, so not an oracle): a fresh-unconfirmed, taken-confirmed and
/// non-existent address are indistinguishable on status AND body; the confirmation link is the only
/// out-of-band signal, delivered only to an inbox the requester controls.
/// </summary>
public sealed record ResendEmailConfirmationCommand(string? Email) : ICommand<Result>;

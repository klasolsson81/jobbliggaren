using JobbPilot.Application.Waitlist.Dtos;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Waitlist.Commands.RequestWaitlistEntry;

/// <summary>
/// Anonym besökare skriver upp sig på väntelistan via /vantelista.
/// Ingen autentisering krävs. Rate-limit hanteras av API-lagret
/// (WaitlistSignupPolicy 3/24h per IP).
/// </summary>
public sealed record RequestWaitlistEntryCommand(string? Email)
    : ICommand<Result<WaitlistEntryRequestedDto>>;

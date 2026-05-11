using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Auth.Queries.VerifyCredentials;

/// <summary>
/// Re-autentisering före destruktiv operation (TD-28 / OWASP ASVS V6.2.5).
/// Validerar att den inloggade användarens lösenord stämmer — INGEN session-
/// mutation, ingen cookie-set. Endast Result.Success / Result.Failure
/// (Auth.InvalidCredentials).
///
/// Skapad som dedikerad endpoint istället för att återanvända /auth/login
/// (CTO-triage 2026-05-11 — SRP/ISP). Använder ICurrentUser.Email för att
/// hämta email från claim, så klienten skickar endast password.
/// </summary>
public sealed record VerifyCredentialsQuery(string? Password)
    : IQuery<Result>, IAuthenticatedRequest;

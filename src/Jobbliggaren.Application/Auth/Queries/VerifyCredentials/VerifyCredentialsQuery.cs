using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Queries.VerifyCredentials;

/// <summary>
/// Re-autentisering före destruktiv operation (TD-28 / OWASP ASVS V6.2.5).
/// Validerar att den inloggade användarens lösenord stämmer — INGEN session-
/// mutation, ingen cookie-set. Endast Result.Success / Result.Failure
/// (Auth.InvalidCredentials).
///
/// Skapad som dedikerad endpoint istället för att återanvända /auth/login
/// (CTO-triage 2026-05-11 — SRP/ISP). Klienten skickar endast password: adressen
/// slås upp per userId i identity-storen via IReauthenticationService
/// (IUserAccountService.GetEmailAsync). (Kommentaren påstod tidigare att den lästes
/// ur ICurrentUser.Email — den vägen fanns aldrig under opaka sessioner och är
/// borttagen i #822.)
/// </summary>
public sealed record VerifyCredentialsQuery(string? Password)
    : IQuery<Result>, IAuthenticatedRequest;

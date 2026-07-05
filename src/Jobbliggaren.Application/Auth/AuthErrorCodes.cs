namespace Jobbliggaren.Application.Auth;

/// <summary>
/// Centralized <see cref="Jobbliggaren.Domain.Common.DomainError"/> codes for the
/// auth flow. Keeps control-flow discriminants and wire mapping in ONE place
/// (§5 — no magic strings scattered across Application/Infrastructure/Api).
/// </summary>
public static class AuthErrorCodes
{
    /// <summary>
    /// Generic, deliberately vague credential failure: unknown email, wrong password
    /// or a soft-deleted account. Rendered as 401 (AuthEndpoints) with copy that never
    /// reveals which of the causes applied (account-enumeration avoidance).
    /// </summary>
    public const string InvalidCredentials = "Auth.InvalidCredentials";

    /// <summary>
    /// The single user-facing detail for the <see cref="InvalidCredentials"/> 401. Rendered on the
    /// wire ONLY via <c>AuthProblem.InvalidCredentials()</c> (Api); referenced from here so the
    /// Result-idiom <c>DomainError</c> message in <c>ReauthenticationService</c> (which never reaches
    /// the wire — normalized by AuthProblem in both the behavior and /auth/verify paths) cannot
    /// silently drift from the authoritative copy (dotnet-architect PR2c-1 Minor — single source).
    /// </summary>
    public const string InvalidCredentialsMessage = "E-post eller lösenord är felaktigt.";

    /// <summary>
    /// Internal lockout verdict (#503, OWASP A07): the account is temporarily locked
    /// after too many failed attempts. Discriminates the audit event
    /// (<c>account_locked_out</c>) in the Api handler BUT is normalized to a
    /// byte-identical <see cref="InvalidCredentials"/> response on the wire
    /// (<c>AuthEndpoints.ToErrorResult</c>) so lockout state never leaks as an
    /// account-enumeration or DoS-target oracle. This code must NEVER reach the client.
    /// </summary>
    public const string AccountLocked = "Auth.AccountLocked";
}

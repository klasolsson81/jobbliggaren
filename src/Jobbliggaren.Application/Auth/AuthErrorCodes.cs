namespace Jobbliggaren.Application.Auth;

/// <summary>
/// Centraliserade <see cref="Jobbliggaren.Domain.Common.DomainError"/>-koder för
/// auth-flödet. Håller kontroll-flödes-diskriminanter och wire-mappning på EN plats
/// (§5 — inga magiska strängar spridda över Application/Infrastructure/Api).
/// </summary>
public static class AuthErrorCodes
{
    /// <summary>
    /// Generiskt, medvetet vagt credential-fel: okänd e-post, fel lösenord eller
    /// soft-deletat konto. Renderas som 401 (AuthEndpoints) med copy som aldrig
    /// avslöjar vilken av orsakerna som gällde (konto-enumererings-skydd).
    /// </summary>
    public const string InvalidCredentials = "Auth.InvalidCredentials";

    /// <summary>
    /// Internt lockout-verdikt (#503, OWASP A07): kontot är tillfälligt låst efter
    /// för många misslyckade försök. Diskriminerar audit-eventet
    /// (<c>account_locked_out</c>) i Api-handlern MEN normaliseras till ett
    /// byte-identiskt <see cref="InvalidCredentials"/>-svar på wire
    /// (<c>AuthEndpoints.ToErrorResult</c>) så lockout-tillståndet aldrig läcker som
    /// konto-enumererings- eller DoS-orakel. Denna kod får ALDRIG nå klienten.
    /// </summary>
    public const string AccountLocked = "Auth.AccountLocked";
}

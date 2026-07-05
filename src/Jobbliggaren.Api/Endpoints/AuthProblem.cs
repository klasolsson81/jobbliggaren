using Jobbliggaren.Application.Auth;

namespace Jobbliggaren.Api.Endpoints;

/// <summary>
/// Single source of the byte-identical <c>Auth.InvalidCredentials</c> 401 ProblemDetails, shared by
/// <see cref="AuthEndpoints"/> (login/register/verify credential failures) AND the central
/// <c>ReauthenticationFailedException</c> arm in <c>Program.cs</c>. Keeping the three literals
/// (status, title, detail) in exactly ONE place is what makes the oracle hold by construction —
/// wrong-password ≡ locked ≡ soft-deleted ≡ re-auth-failed all render identically, so no failure
/// mode leaks which cause applied (GDPR Art. 32 oracle-avoidance; pinned by the oracle-parity tests).
/// </summary>
public static class AuthProblem
{
    public static IResult InvalidCredentials() => Results.Problem(
        detail: AuthErrorCodes.InvalidCredentialsMessage,
        title: AuthErrorCodes.InvalidCredentials,
        statusCode: StatusCodes.Status401Unauthorized);
}

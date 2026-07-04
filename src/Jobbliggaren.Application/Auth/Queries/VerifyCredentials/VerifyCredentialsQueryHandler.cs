using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Queries.VerifyCredentials;

public sealed class VerifyCredentialsQueryHandler(
    ICurrentUser currentUser,
    IUserAccountService userAccountService)
    : IQueryHandler<VerifyCredentialsQuery, Result>
{
    public async ValueTask<Result> Handle(
        VerifyCredentialsQuery query, CancellationToken cancellationToken)
    {
        // Defense: endpoint kräver RequireAuthorization → ICurrentUser ska vara
        // satt. Failsafe-check så ett konfigurations-fel inte exponerar verify
        // för anonym caller.
        if (!currentUser.UserId.HasValue)
            return InvalidCredentials();

        var userId = currentUser.UserId.Value;

        // SessionAuthenticationHandler sätter bara NameIdentifier/Sub-claims —
        // ingen email-claim. Hämta från Identity via userId så vi inte är
        // beroende av claim-shape.
        var email = await userAccountService.GetEmailAsync(userId, cancellationToken);
        if (string.IsNullOrEmpty(email))
            return InvalidCredentials();

        // #503 (OWASP A07): ValidateCredentialsAsync now honors Identity's lockout — it
        // short-circuits locked accounts (IsLockedOutAsync) and counts failed attempts
        // (AccessFailedAsync). That is a deliberate, observable side-effect (a write) even
        // in this "read-only" query: re-authentication is as valid a brute-force surface as
        // login and shares the same anti-automation (without it /verify would be an unlocked
        // bypass of the login lockout). A locked account yields Auth.AccountLocked
        // internally, but the wire response is normalized to Auth.InvalidCredentials
        // (AuthEndpoints) so lockout state does not leak. (Verify does not audit the lockout
        // event — just as it does not audit login_failed; the login handler is the primary
        // brute-force telemetry surface.)
        var credentialsResult = await userAccountService.ValidateCredentialsAsync(
            email, query.Password!, cancellationToken);

        if (credentialsResult.IsFailure)
            return Result.Failure(credentialsResult.Error);

        // Defense-in-depth: email måste resolvera till samma userId som i
        // sessionen. Skyddar primärt mot framtida ändringar (t.ex. om
        // GetEmailAsync framgent cachas och kan bli stale, eller om Identity
        // tillåter email-byte mellan sessioner). Idag är TOCTOU-fönstret
        // försumbart men checken kostar 0 så den behålls.
        if (credentialsResult.Value.UserId != userId)
            return InvalidCredentials();

        return Result.Success();
    }

    private static Result InvalidCredentials() =>
        Result.Failure(
            DomainError.Validation(AuthErrorCodes.InvalidCredentials, "E-post eller lösenord är felaktigt."));
}

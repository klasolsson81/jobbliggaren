using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Auth.Queries.VerifyCredentials;

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

        // NOTE: ValidateCredentialsAsync uppdaterar Identitys lockout-räknare
        // vid failure även när vi returnerar Result.Failure — det är önskat
        // beteende (brute-force-skydd) men är ett observable side-effect även
        // för "read-only" query.
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
            DomainError.Validation("Auth.InvalidCredentials", "E-post eller lösenord är felaktigt."));
}

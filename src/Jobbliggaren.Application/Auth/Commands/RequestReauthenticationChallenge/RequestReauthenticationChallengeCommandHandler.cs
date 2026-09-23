using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.Auth.Commands.RequestReauthenticationChallenge;

/// <summary>
/// #1739 — the request path of a re-authentication challenge (ADR 0142 D5). Unlike the public login
/// challenge it is authenticated and synchronous: the caller is the account holder, so every refusal is
/// visible and the mail is sent before the answer. The record is bound to the user and the purpose, in the
/// bound key family, so a login challenge for the same address can neither burn it nor be presented as it.
/// </summary>
public sealed class RequestReauthenticationChallengeCommandHandler(
    ICurrentUser currentUser,
    IUserAccountService userAccountService,
    IEmailSender emailSender,
    IRateBudget budget,
    IOptions<AuthEmailCooldownOptions> cooldownOptions,
    ILoginChallengeStore store)
    : ICommandHandler<RequestReauthenticationChallengeCommand, Result<ChallengeId>>
{
    private readonly RateBudgetScope _cooldown = LoginChallengePolicy.ReauthCooldown(
        TimeSpan.FromSeconds(cooldownOptions.Value.LoginChallengeWindowSeconds));

    public async ValueTask<Result<ChallengeId>> Handle(
        RequestReauthenticationChallengeCommand command, CancellationToken cancellationToken)
    {
        // Self-defending: AuthorizationBehavior ran, but the handler does not depend on pipeline configuration.
        if (!currentUser.UserId.HasValue)
            return Result.Failure<ChallengeId>(
                DomainError.Validation(AuthErrorCodes.NotAuthenticated, "Inloggning krävs för att begära en kod."));

        var userId = currentUser.UserId.Value;

        // 1. CAPABILITY, first, reading no input and spending no budget (the RequestLoginChallenge precedent).
        if (!emailSender.CanDeliver)
        {
            return Result.Failure<ChallengeId>(DomainError.Validation(
                AuthErrorCodes.EmailDeliveryUnavailable,
                AuthErrorCodes.EmailDeliveryUnavailableMessage));
        }

        // The account's OWN address, resolved by the session's user id: the client sends none.
        var email = await userAccountService.GetEmailAsync(userId, cancellationToken);
        if (string.IsNullOrEmpty(email))
            return Result.Failure<ChallengeId>(
                DomainError.Validation(AuthErrorCodes.InvalidCredentials, AuthErrorCodes.InvalidCredentialsMessage));

        // 2–3. The gates, each run only when the one before admitted the request. Both are keyed by the user id:
        // only a holder of the session can spend them, so a stranger who knows the address cannot block the
        // owner's deletion or address change (ADR 0142 D5).
        if (!await budget.TryConsumeAsync(_cooldown, userId.ToString(), cancellationToken))
        {
            return Result.Failure<ChallengeId>(
                DomainError.Conflict(AuthErrorCodes.ReauthCooldown, AuthErrorCodes.ReauthCooldownMessage));
        }

        // Terminal: a re-authentication cannot fall back to a link, so past the day's codes the request is refused.
        if (!await budget.TryConsumeAsync(LoginChallengePolicy.ReauthCodeBudget, userId.ToString(), cancellationToken))
        {
            return Result.Failure<ChallengeId>(DomainError.Conflict(
                AuthErrorCodes.ReauthCodeBudgetExhausted, AuthErrorCodes.ReauthCodeBudgetExhaustedMessage));
        }

        // 4. The record BEFORE the mail (a code that arrives before its record would read as expired), then the
        // mail, synchronously: the caller is signed in and the dialog needs a definite answer. A send that
        // throws propagates, as ChangeEmailCommandHandler's does.
        var challengeId = ChallengeId.Generate();
        var code = await store.PutBoundAsync(
            new NewBoundChallenge(challengeId, email, new ChallengeBinding(ChallengePurpose.Reauthentication, userId)),
            cancellationToken);

        await emailSender.SendLoginChallengeAsync(
            email, new LoginChallengeEmail.ReauthenticationCode(code), cancellationToken);

        return Result.Success(challengeId);
    }
}

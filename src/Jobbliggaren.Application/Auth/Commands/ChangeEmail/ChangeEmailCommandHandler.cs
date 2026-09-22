using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.Auth.Commands.ChangeEmail;

/// <summary>
/// #1739 — the change-email request step (ADR 0142 D5): gates, then a bound challenge addressed to the NEW address
/// and a code mailed to it, synchronously. Every refusal is visible, because the caller is the account holder and
/// has just re-authenticated.
/// </summary>
public sealed class ChangeEmailCommandHandler(
    ICurrentUser currentUser,
    IUserAccountService userAccountService,
    IEmailSender emailSender,
    IRateBudget budget,
    IOptions<AuthEmailCooldownOptions> cooldownOptions,
    ILoginChallengeStore store)
    : ICommandHandler<ChangeEmailCommand, Result<EmailChangeChallenge>>
{
    private readonly TimeSpan _window = TimeSpan.FromSeconds(cooldownOptions.Value.ChangeEmailWindowSeconds);

    public async ValueTask<Result<EmailChangeChallenge>> Handle(
        ChangeEmailCommand command, CancellationToken cancellationToken)
    {
        // Self-defending (mirrors ChangePassword / DeleteAccount): Authorization + Reauthentication ran
        // before this handler, but we do not take a dependency on pipeline configuration.
        if (!currentUser.UserId.HasValue)
            return Result.Failure<EmailChangeChallenge>(
                DomainError.Validation(AuthErrorCodes.NotAuthenticated, "Inloggning krävs för att byta e-postadress."));

        // The validator guarantees both are non-empty; re-assert so the handler is correct in isolation.
        if (string.IsNullOrEmpty(command.ReauthGrant) || string.IsNullOrEmpty(command.NewEmail))
            return Result.Failure<EmailChangeChallenge>(
                DomainError.Validation("Auth.InvalidInput", "Ny e-postadress krävs."));

        // #1087 — refuse BEFORE anything happens rather than lie after: a code that cannot be delivered leaves
        // the user with no way forward. Ahead of the budgets, so the server's configuration spends none of them.
        // The capability is asked of the PORT, never of the environment (CLAUDE.md §2.1).
        if (!emailSender.CanDeliver)
            return Result.Failure<EmailChangeChallenge>(DomainError.Validation(
                AuthErrorCodes.EmailDeliveryUnavailable,
                AuthErrorCodes.EmailDeliveryUnavailableMessage));

        var userId = currentUser.UserId.Value;
        var newEmail = command.NewEmail;

        // The budgets, each spent only when the one before admitted the request (security-auditor, PR 4's
        // pre-code round). The two keyed by the USER come first, so a refusal on a user key never spends the
        // one shared between users; the target cooldown keeps the cooldown's own code, because a code of its
        // own would tell the caller that someone asked for the same address a moment ago. The shared login
        // mail budget is not consulted: the public login arm spends it anonymously, before any lookup.
        if (!await budget.TryConsumeAsync(ChangeEmailPolicy.UserCooldown(_window), userId.ToString(), cancellationToken))
            return Result.Failure<EmailChangeChallenge>(Cooldown());

        if (!await budget.TryConsumeAsync(ChangeEmailPolicy.DailyTargetBudget, userId.ToString(), cancellationToken))
            return Result.Failure<EmailChangeChallenge>(DomainError.Conflict(
                AuthErrorCodes.ChangeEmailTargetBudgetExhausted, AuthErrorCodes.ChangeEmailTargetBudgetExhaustedMessage));

        if (!await budget.TryConsumeAsync(ChangeEmailPolicy.TargetCooldown(_window), newEmail, cancellationToken))
            return Result.Failure<EmailChangeChallenge>(Cooldown());

        // Storable and free, after the user budgets: probing addresses for existence costs a re-authentication
        // and is capped per user. The address is the validated command's, never a stored row's.
        var free = await userAccountService.CheckAddressIsFreeAsync(userId, newEmail, cancellationToken);
        if (free.IsFailure)
            return Result.Failure<EmailChangeChallenge>(free.Error);

        // The record BEFORE the mail (a code that arrives before its record would read as expired), then the
        // mail, synchronously. A send that throws propagates, and no User.EmailChangeRequested row is written.
        var challengeId = ChallengeId.Generate();
        var code = await store.PutBoundAsync(
            new NewBoundChallenge(challengeId, newEmail, new ChallengeBinding(ChallengePurpose.ChangeEmail, userId)),
            cancellationToken);

        await emailSender.SendLoginChallengeAsync(
            newEmail, new LoginChallengeEmail.AddressChangeCode(code, _window), cancellationToken);

        return Result.Success(new EmailChangeChallenge(userId, challengeId));
    }

    private static DomainError Cooldown() =>
        DomainError.Conflict(AuthErrorCodes.ChangeEmailCooldown, AuthErrorCodes.ChangeEmailCooldownMessage);
}

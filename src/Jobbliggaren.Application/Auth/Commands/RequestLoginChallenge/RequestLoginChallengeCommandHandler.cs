using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.Auth.Commands.RequestLoginChallenge;

/// <summary>
/// #1735 — the request path of the login challenge (ADR 0142 D2). It never reads the account: the
/// constructor takes nothing that could, which a test pins, so its cost and its answer are the same for
/// every well-formed address. Everything that depends on the account happens in the dispatch consumer.
/// </summary>
public sealed class RequestLoginChallengeCommandHandler(
    IEmailSender emailSender,
    IRateBudget budget,
    IOptions<AuthEmailCooldownOptions> cooldownOptions,
    ILoginChallengeDispatcher dispatcher,
    IRequestContextProvider requestContext)
    : ICommandHandler<RequestLoginChallengeCommand, Result<ChallengeId>>
{
    private readonly RateBudgetScope _cooldown = LoginChallengePolicy.Cooldown(
        TimeSpan.FromSeconds(cooldownOptions.Value.LoginChallengeWindowSeconds));

    public async ValueTask<Result<ChallengeId>> Handle(
        RequestLoginChallengeCommand command, CancellationToken cancellationToken)
    {
        // 1. CAPABILITY, first, reading no input: the 503/202 split is then a property of the server's
        // configuration and carries nothing about any address.
        if (!emailSender.CanDeliver)
        {
            return Result.Failure<ChallengeId>(DomainError.Validation(
                AuthErrorCodes.EmailDeliveryUnavailable,
                AuthErrorCodes.EmailDeliveryUnavailableMessage));
        }

        var email = command.Email!;
        var challengeId = ChallengeId.Generate();

        // 2. The gates, each run only when the one before admitted the request, so a refused request spends
        // nothing further (security-auditor Q18). The cooldown and the mail budget refuse SILENTLY — the same
        // 202 and id, no record, no mail; a visible throttle on this surface would answer differently for an
        // address someone had just used. The code budget refuses nothing: past it the mail carries a link
        // only (Klas, 2026-09-19, option (A)).
        if (!await budget.TryConsumeAsync(_cooldown, email, cancellationToken))
            return Result.Success(challengeId);

        if (!await budget.TryConsumeAsync(LoginChallengePolicy.MailBudget, email, cancellationToken))
            return Result.Success(challengeId);

        var codeBudget = await budget.TryConsumeAsync(LoginChallengePolicy.CodeBudget, email, cancellationToken)
            ? CodeBudgetState.Admitted
            : CodeBudgetState.Exhausted;

        // 3. Hand off. Void: there is no result to branch on, so a full queue answers like an empty one.
        dispatcher.Enqueue(new LoginChallengeDispatch(
            challengeId, email, codeBudget, requestContext.IpAddress, requestContext.UserAgent));

        return Result.Success(challengeId);
    }
}

using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.Auth.LoginChallenges;

/// <summary>
/// The gates a login challenge passes before it is minted, and the hand-off to the dispatch consumer (ADR 0142 D2):
/// one home for every path that sends a login code (#1735; #1745 added the provider path). It never reads the
/// account: nothing it takes could, which a test pins transitively, so its cost and its answer are the same for every
/// well-formed address. Everything that depends on the account happens in the dispatch consumer.
/// </summary>
public sealed class LoginChallengeAdmission(
    IEmailSender emailSender,
    IRateBudget budget,
    IOptions<AuthEmailCooldownOptions> cooldownOptions,
    ILoginChallengeDispatcher dispatcher,
    IRequestContextProvider requestContext)
{
    private readonly RateBudgetScope _cooldown = LoginChallengePolicy.Cooldown(
        TimeSpan.FromSeconds(cooldownOptions.Value.LoginChallengeWindowSeconds));

    public async Task<Result<ChallengeId>> AdmitAsync(string email, CancellationToken ct)
    {
        // 1. CAPABILITY, first, reading no input: the 503/202 split is then a property of the server's
        // configuration and carries nothing about any address.
        if (!emailSender.CanDeliver)
        {
            return Result.Failure<ChallengeId>(DomainError.Validation(
                AuthErrorCodes.EmailDeliveryUnavailable,
                AuthErrorCodes.EmailDeliveryUnavailableMessage));
        }

        var challengeId = ChallengeId.Generate();

        // 2. The gates, each run only when the one before admitted the request, so a refused request spends
        // nothing further (security-auditor Q18). The cooldown and the mail budget refuse SILENTLY — the same
        // 202 and id, no record, no mail; a visible throttle on this surface would answer differently for an
        // address someone had just used. The code budget refuses nothing: past it the mail carries a link
        // only (Klas, 2026-09-19, option (A)).
        if (!await budget.TryConsumeAsync(_cooldown, email, ct))
            return Result.Success(challengeId);

        if (!await budget.TryConsumeAsync(LoginChallengePolicy.MailBudget, email, ct))
            return Result.Success(challengeId);

        var codeBudget = await budget.TryConsumeAsync(LoginChallengePolicy.CodeBudget, email, ct)
            ? CodeBudgetState.Admitted
            : CodeBudgetState.Exhausted;

        // 3. Hand off. Void: there is no result to branch on, so a full queue answers like an empty one.
        dispatcher.Enqueue(new LoginChallengeDispatch(
            challengeId, email, codeBudget, requestContext.IpAddress, requestContext.UserAgent));

        return Result.Success(challengeId);
    }
}

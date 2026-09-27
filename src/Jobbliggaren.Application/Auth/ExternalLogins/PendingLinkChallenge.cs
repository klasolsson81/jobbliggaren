using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Application.Auth.ExternalLogins;

/// <summary>
/// A provider login with an asserted address and no link yet (ADR 0142 Amendment (16), #1745): the address only
/// chooses where a login code goes, through the same gates as <c>POST /auth/challenge</c>, and a pending-link grant
/// waits for the code. It reads no account, so the answer is the same whether the address has one; the grant is
/// issued whatever the gates decided, so the answer does not tell a cooldown from a sent mail either.
/// </summary>
public sealed class PendingLinkChallenge(LoginChallengeAdmission admission, IGrantStore grants)
{
    public async Task<Result<PendingLinkRequested>> RequestAsync(AssertedLoginProof proof, CancellationToken ct)
    {
        var admitted = await admission.AdmitAsync(proof.Address.Value, ct);
        if (admitted.IsFailure)
            return Result.Failure<PendingLinkRequested>(admitted.Error);

        var linkGrant = await grants.IssueAsync(
            new GrantSubject.PendingExternalLink(proof.Address, proof.Provider, proof.Subject), ct);
        return Result.Success(new PendingLinkRequested(admitted.Value, linkGrant));
    }
}

/// <summary>The challenge id, and the grant the code's verification may redeem to bind the login.</summary>
public sealed record PendingLinkRequested(ChallengeId ChallengeId, GrantToken LinkGrant);

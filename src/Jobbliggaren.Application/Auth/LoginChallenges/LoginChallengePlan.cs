using System.Diagnostics;

namespace Jobbliggaren.Application.Auth.LoginChallenges;

/// <summary>What the request path's code budget said. A classifier, never a refusal (ADR 0142 D2).</summary>
public enum CodeBudgetState
{
    Admitted,
    Exhausted,
}

/// <summary>Which mail a login challenge sends. Exactly one per admitted request.</summary>
public enum LoginChallengeKind
{
    CodeAndLink,
    LinkOnly,
    PendingDeletion,
    RegistrationClosed,
}

/// <summary>
/// The login challenge's plan for part 1a: which mail an address gets (ADR 0142 D2; senior-cto-advisor,
/// 2026-09-19). An address with no account is never given a code here, whatever
/// <c>RegistrationsOpen</c> says — the arm that registers a new address is part 1c.
/// </summary>
public static class LoginChallengePlan
{
    public static LoginChallengeKind Decide(LoginSubject subject, CodeBudgetState codeBudget) =>
        (subject, codeBudget) switch
        {
            (LoginSubject.Active, CodeBudgetState.Admitted) => LoginChallengeKind.CodeAndLink,
            (LoginSubject.Active, CodeBudgetState.Exhausted) => LoginChallengeKind.LinkOnly,
            (LoginSubject.PendingDeletion, _) => LoginChallengeKind.PendingDeletion,
            (LoginSubject.NoAccount or LoginSubject.ProfileMissing, _) => LoginChallengeKind.RegistrationClosed,
            _ => throw new UnreachableException("The login challenge plan has no cell for this input."),
        };

    public static ChallengeCredentials CredentialsFor(LoginChallengeKind kind) => kind switch
    {
        LoginChallengeKind.CodeAndLink => ChallengeCredentials.CodeAndLink,
        LoginChallengeKind.LinkOnly => ChallengeCredentials.LinkOnly,
        LoginChallengeKind.PendingDeletion or LoginChallengeKind.RegistrationClosed => ChallengeCredentials.None,
        _ => throw new UnreachableException("A LoginChallengeKind has no credentials."),
    };
}

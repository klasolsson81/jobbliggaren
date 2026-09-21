using System.Diagnostics;

namespace Jobbliggaren.Application.Auth.LoginChallenges;

/// <summary>What the request path's code budget said. A classifier, never a refusal (ADR 0142 D2).</summary>
public enum CodeBudgetState
{
    Admitted,
    Exhausted,
}

/// <summary>
/// Whether the registration kill-switch (<c>Auth:RegistrationsOpen</c>, ADR 0083) admits new accounts.
/// <see cref="Closed"/> is the zero value, so an unset state never opens the gate.
/// </summary>
public enum RegistrationState
{
    Closed = 0,
    Open = 1,
}

/// <summary>Which mail a login challenge sends. Exactly one per admitted request.</summary>
public enum LoginChallengeKind
{
    CodeAndLink,
    LinkOnly,
    PendingDeletion,
    RegistrationClosed,
    NewAccountCode,
    NewAccountCodeLimitReached,
}

/// <summary>
/// The login challenge's plan: which mail an address gets (ADR 0142 D2; senior-cto-advisor, 2026-09-19 and
/// 2026-09-20). The registration state moves only a subject with no usable account.
/// </summary>
public static class LoginChallengePlan
{
    public static LoginChallengeKind Decide(
        LoginSubject subject, CodeBudgetState codeBudget, RegistrationState registration) =>
        (subject, codeBudget, registration) switch
        {
            (LoginSubject.Active, CodeBudgetState.Admitted, _) => LoginChallengeKind.CodeAndLink,
            (LoginSubject.Active, CodeBudgetState.Exhausted, _) => LoginChallengeKind.LinkOnly,
            (LoginSubject.PendingDeletion, _, _) => LoginChallengeKind.PendingDeletion,
            (LoginSubject.NoAccount or LoginSubject.ProfileMissing, _, RegistrationState.Closed) =>
                LoginChallengeKind.RegistrationClosed,
            (LoginSubject.NoAccount or LoginSubject.ProfileMissing, CodeBudgetState.Admitted, RegistrationState.Open) =>
                LoginChallengeKind.NewAccountCode,
            (LoginSubject.NoAccount or LoginSubject.ProfileMissing, CodeBudgetState.Exhausted, RegistrationState.Open) =>
                LoginChallengeKind.NewAccountCodeLimitReached,
            _ => throw new UnreachableException("The login challenge plan has no cell for this input."),
        };

    public static ChallengeCredentials CredentialsFor(LoginChallengeKind kind) => kind switch
    {
        LoginChallengeKind.CodeAndLink => ChallengeCredentials.CodeAndLink,
        LoginChallengeKind.LinkOnly => ChallengeCredentials.LinkOnly,
        LoginChallengeKind.NewAccountCode => ChallengeCredentials.CodeOnly,
        LoginChallengeKind.PendingDeletion
            or LoginChallengeKind.RegistrationClosed
            or LoginChallengeKind.NewAccountCodeLimitReached => ChallengeCredentials.None,
        _ => throw new UnreachableException("A LoginChallengeKind has no credentials."),
    };
}

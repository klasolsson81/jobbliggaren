using System.Diagnostics;
using Jobbliggaren.Application.Auth.Jobs.HardDeleteAccounts;

namespace Jobbliggaren.Application.Auth.LoginChallenges;

/// <summary>
/// The one function both proofs — a code and a link — end in (senior-cto-advisor Q1, 2026-09-19). It
/// resolves the proven address at proof time, not at issue time, so an account deleted or a kill-switch
/// thrown inside the challenge's 15 minutes is honoured.
/// </summary>
public sealed class LoginProofOutcome(LoginSubjectResolver subjects, PasswordlessSessionGrant grant)
{
    public async Task<LoginOutcome> ResolveAsync(LoginChallengeProof proof, LoginMethod method, CancellationToken ct) =>
        await subjects.ResolveAsync(proof.ProvenEmail, ct) switch
        {
            LoginSubject.Active active =>
                new LoginOutcome.SignedIn((await grant.GrantAsync(active, method, ct)).SessionId),
            LoginSubject.PendingDeletion pending =>
                new LoginOutcome.PendingDeletion(AccountRestoreWindow.PermanentDeletionEarliest(pending.DeletedAt)),
            LoginSubject.NoAccount or LoginSubject.ProfileMissing => new LoginOutcome.RegistrationClosed(),
            var other => throw new UnreachableException($"Unclassified login subject {other.GetType().Name}."),
        };
}

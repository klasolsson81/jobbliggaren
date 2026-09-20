using System.Diagnostics;
using Jobbliggaren.Application.Auth.Jobs.HardDeleteAccounts;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.Auth.LoginChallenges;

/// <summary>
/// The one function both proofs — a code and a link — end in (senior-cto-advisor Q1, 2026-09-19). It
/// resolves the proven address at proof time, not at issue time, so an account deleted or a kill-switch
/// thrown inside the challenge's 15 minutes is honoured.
/// </summary>
public sealed partial class LoginProofOutcome(
    LoginSubjectResolver subjects, PasswordlessSessionGrant grant, ILogger<LoginProofOutcome> logger)
{
    public async Task<LoginOutcome> ResolveAsync(LoginChallengeProof proof, LoginMethod method, CancellationToken ct)
    {
        var subject = await subjects.ResolveAsync(proof.ProvenEmail, ct);

        // The proven inbox must be the account's own address, spelling for spelling.
        if (subject is LoginSubject.KnownAccount known
            && !string.Equals(known.AccountEmail.Trim(), proof.ProvenEmail.Trim(), StringComparison.Ordinal))
        {
            LogProvenAddressNotTheAccountsOwn(logger, known.UserId, method);
            return NotThisAccountsAddress();
        }

        return subject switch
        {
            LoginSubject.Active active =>
                new LoginOutcome.SignedIn((await grant.GrantAsync(active, method, ct)).SessionId),
            LoginSubject.PendingDeletion pending =>
                new LoginOutcome.PendingDeletion(AccountRestoreWindow.PermanentDeletionEarliest(pending.DeletedAt)),
            LoginSubject.NoAccount or LoginSubject.ProfileMissing => new LoginOutcome.RegistrationClosed(),
            var other => throw new UnreachableException($"Unclassified login subject {other.GetType().Name}."),
        };
    }

    private static LoginOutcome.RegistrationClosed NotThisAccountsAddress() => new();

    [LoggerMessage(1016, LogLevel.Warning,
        "Login proof refused: the proven address is another spelling than the account's own ({UserId}, "
        + "{LoginMethod})")]
    private static partial void LogProvenAddressNotTheAccountsOwn(ILogger logger, Guid userId, LoginMethod loginMethod);
}

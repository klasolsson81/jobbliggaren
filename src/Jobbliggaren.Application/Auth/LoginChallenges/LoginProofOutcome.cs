using System.Diagnostics;
using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Auth.Jobs.HardDeleteAccounts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.Auth.LoginChallenges;

/// <summary>
/// The one function both proofs — a code and a link — end in (senior-cto-advisor Q1, 2026-09-19). It
/// resolves the proven address at proof time, not at issue time, so an account deleted or a kill-switch
/// thrown inside the challenge's 15 minutes is honoured.
/// </summary>
public sealed partial class LoginProofOutcome(
    LoginSubjectResolver subjects,
    PasswordlessSessionGrant grant,
    IGrantStore grants,
    IOptions<AuthOptions> authOptions,
    ILogger<LoginProofOutcome> logger)
{
    public async Task<LoginOutcome> ResolveAsync(LoginChallengeProof proof, LoginMethod method, CancellationToken ct)
    {
        var registration = authOptions.Value.RegistrationsOpen ? RegistrationState.Open : RegistrationState.Closed;
        var subject = await subjects.ResolveAsync(proof.ProvenEmail, ct);

        // The proven inbox must be the account's own address, spelling for spelling.
        if (subject is LoginSubject.KnownAccount known
            && !string.Equals(known.AccountEmail, proof.ProvenEmail, StringComparison.Ordinal))
        {
            LogProvenAddressNotTheAccountsOwn(logger, known.UserId, method);
            return NotThisAccountsAddress(registration);
        }

        return (subject, method, registration) switch
        {
            (LoginSubject.Active active, _, _) =>
                new LoginOutcome.SignedIn((await grant.GrantAsync(active, method, ct)).SessionId),
            (LoginSubject.PendingDeletion pending, _, _) =>
                new LoginOutcome.PendingDeletion(AccountRestoreWindow.PermanentDeletionEarliest(pending.DeletedAt)),

            // Closed: no grant is issued, so nothing here reaches the grant store while the switch is off.
            (LoginSubject.NoAccount or LoginSubject.ProfileMissing, _, RegistrationState.Closed) =>
                new LoginOutcome.RegistrationClosed(),

            // Only a CODE proves a new address. A link reaches this arm only when the account it was mailed
            // to went away inside the challenge's lifetime, and a new account must not rise from it.
            (LoginSubject.NoAccount, LoginMethod.Code, RegistrationState.Open) => new LoginOutcome.ConsentRequired(
                await grants.IssueAsync(new GrantSubject.LoginComplete(proof.ProvenEmail), ct)),
            (LoginSubject.NoAccount, _, RegistrationState.Open) => new LoginOutcome.AccountUnavailable(),

            // Never adopted: the row is an in-flight sibling registration or the residue of a failed hard
            // delete, and the two cannot be told apart here. The orphan sweep collects it.
            (LoginSubject.ProfileMissing, _, RegistrationState.Open) => new LoginOutcome.AccountUnavailable(),

            var other => throw new UnreachableException($"Unclassified login outcome input {other}."),
        };
    }

    // Neither a session nor a grant, whatever the method. Closed: the answer every address without an account
    // gets. Open: it can neither log in nor register, since its spelling resolves to another account's row.
    private static LoginOutcome NotThisAccountsAddress(RegistrationState registration) => registration switch
    {
        RegistrationState.Open => new LoginOutcome.AccountUnavailable(),
        _ => new LoginOutcome.RegistrationClosed(),
    };

    [LoggerMessage(1016, LogLevel.Warning,
        "Login proof refused: the proven address is another spelling than the account's own ({UserId}, "
        + "{LoginMethod})")]
    private static partial void LogProvenAddressNotTheAccountsOwn(ILogger logger, Guid userId, LoginMethod loginMethod);
}

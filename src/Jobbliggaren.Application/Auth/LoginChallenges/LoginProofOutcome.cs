using System.Diagnostics;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Auth.Jobs.HardDeleteAccounts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.Auth.LoginChallenges;

/// <summary>
/// The one function every proof ends in: a code, a link and a provider's (senior-cto-advisor Q1, 2026-09-19;
/// #1744). It resolves the proven address at proof time, not at issue time, so an account deleted or a kill-switch
/// thrown inside the challenge's 15 minutes is honoured. One switch decides; what a proof of a NEW address may earn
/// is decided by the entry point, never by comparing a method.
/// </summary>
public sealed partial class LoginProofOutcome(
    LoginSubjectResolver subjects,
    PasswordlessSessionGrant grant,
    IGrantStore grants,
    ExternalLoginLinker externalLogins,
    IOptions<AuthOptions> authOptions,
    ILogger<LoginProofOutcome> logger)
{
    public async Task<LoginOutcome> ResolveAsync(LoginChallengeProof proof, LoginMethod method, CancellationToken ct)
    {
        var registration = Registration();
        var subject = await subjects.ResolveAsync(proof.ProvenEmail, ct);

        // The proven inbox must be the account's own address, spelling for spelling.
        if (subject is LoginSubject.KnownAccount known
            && !string.Equals(known.AccountEmail, proof.ProvenEmail, StringComparison.Ordinal))
        {
            LogProvenAddressNotTheAccountsOwn(logger, known.UserId, method);
            return NotThisAccountsAddress(registration);
        }

        // Only a CODE proves a new address. A link reaches the no-account arm only when the account it was mailed
        // to went away inside the challenge's lifetime, and a new account must not rise from it.
        var consent = method == LoginMethod.Code ? new GrantSubject.LoginComplete(proof.ProvenEmail) : null;
        return await DecideAsync(subject, method, registration, consent, linkBeforeSession: null, ct);
    }

    /// <summary>
    /// A provider's proof (ADR 0142 D8, security-auditor M-1). Address first: the verified address names the account,
    /// matched by <see cref="ExternalAddressMatch"/>; then the provider's identifier may belong to no other account.
    /// So every provider session proves the account's own inbox, as a code does, and on its first use the link and its
    /// audit row are committed before the session exists.
    /// </summary>
    public async Task<LoginOutcome> ResolveExternalAsync(ExternalLoginProof proof, CancellationToken ct)
    {
        var registration = Registration();
        var method = proof.Provider.LoginMethod;
        var resolved = await subjects.ResolveExternalAsync(proof, ct);

        if (resolved.Subject is LoginSubject.KnownAccount known
            && !ExternalAddressMatch.IsSameAddress(known.AccountEmail, proof.Email.Value))
        {
            LogProvenAddressNotTheAccountsOwn(logger, known.UserId, method);
            return NotThisAccountsAddress(registration);
        }

        if (resolved.IsLinkedElsewhere)
        {
            LogLinkedToAnotherAccount(logger, resolved.LinkedUserId!.Value, method);
            return NotThisAccountsAddress(registration);
        }

        var consent = new GrantSubject.LoginCompleteExternal(proof.Email.Value, proof.Provider, proof.Subject);
        Func<LoginSubject.Active, CancellationToken, Task<bool>>? link = resolved.LinkedUserId is null
            ? (active, token) => externalLogins.LinkAsync(active.UserId, proof.Provider, proof.Subject, token)
            : null;

        return await DecideAsync(resolved.Subject, method, registration, consent, link, ct);
    }

    private async Task<LoginOutcome> DecideAsync(
        LoginSubject subject,
        LoginMethod method,
        RegistrationState registration,
        GrantSubject? consent,
        Func<LoginSubject.Active, CancellationToken, Task<bool>>? linkBeforeSession,
        CancellationToken ct) => (subject, registration) switch
        {
            (LoginSubject.Active active, _) => await SignInAsync(active, method, registration, linkBeforeSession, ct),
            (LoginSubject.PendingDeletion pending, _) =>
                new LoginOutcome.PendingDeletion(AccountRestoreWindow.PermanentDeletionEarliest(pending.DeletedAt)),

            // Closed: no grant is issued, so nothing here reaches the grant store while the switch is off.
            (LoginSubject.NoAccount or LoginSubject.ProfileMissing, RegistrationState.Closed) =>
                new LoginOutcome.RegistrationClosed(),

            (LoginSubject.NoAccount, RegistrationState.Open) when consent is not null =>
                new LoginOutcome.ConsentRequired(await grants.IssueAsync(consent, ct)),
            (LoginSubject.NoAccount, RegistrationState.Open) => new LoginOutcome.AccountUnavailable(),

            // Never adopted: the row is an in-flight sibling registration or the residue of a failed hard
            // delete, and the two cannot be told apart here. The orphan sweep collects it.
            (LoginSubject.ProfileMissing, RegistrationState.Open) => new LoginOutcome.AccountUnavailable(),

            var other => throw new UnreachableException(
                $"Unclassified login outcome input {other.subject.GetType().Name}, {method}, {other.registration}."),
        };

    private async Task<LoginOutcome> SignInAsync(
        LoginSubject.Active active,
        LoginMethod method,
        RegistrationState registration,
        Func<LoginSubject.Active, CancellationToken, Task<bool>>? linkBeforeSession,
        CancellationToken ct)
    {
        // The link commits before the session, so a link another account won in the meantime opens none.
        if (linkBeforeSession is not null && !await linkBeforeSession(active, ct))
        {
            LogLinkLostToAnotherAccount(logger, active.UserId, method);
            return NotThisAccountsAddress(registration);
        }

        return new LoginOutcome.SignedIn((await grant.GrantAsync(active, method, ct)).SessionId);
    }

    private RegistrationState Registration() =>
        authOptions.Value.RegistrationsOpen ? RegistrationState.Open : RegistrationState.Closed;

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

    // #1744 — never the provider's identifier or the address: the account that holds the login, and the method.
    [LoggerMessage(1024, LogLevel.Warning,
        "Login proof refused: the provider login belongs to another account than the address names ({UserId}, "
        + "{LoginMethod})")]
    private static partial void LogLinkedToAnotherAccount(ILogger logger, Guid userId, LoginMethod loginMethod);

    [LoggerMessage(1025, LogLevel.Warning,
        "Login proof refused: another account linked the provider login first ({UserId}, {LoginMethod})")]
    private static partial void LogLinkLostToAnotherAccount(ILogger logger, Guid userId, LoginMethod loginMethod);
}

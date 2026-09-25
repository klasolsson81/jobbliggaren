using System.Diagnostics;
using Jobbliggaren.Application.Auth.Jobs.HardDeleteAccounts;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.Auth.LoginChallenges;

/// <summary>
/// The consumer's decision for one queued login challenge (ADR 0142 D2): classify the address, choose the
/// mail, write the record, send, audit. Application logic, so the Infrastructure drain only resolves this per
/// item and it can be tested with fakes (dotnet-architect R10). Everything that depends on whether the address
/// has an account happens here, off the request path.
/// </summary>
public sealed partial class LoginChallengeIssuer(
    LoginSubjectResolver subjects,
    ILoginChallengeStore store,
    IRateBudget budget,
    IEmailSender emailSender,
    IAuthAuditLogger audit,
    IOptions<AuthOptions> authOptions,
    ILogger<LoginChallengeIssuer> logger)
{
    public async Task IssueAsync(LoginChallengeDispatch dispatch, CancellationToken ct)
    {
        var subject = await subjects.ResolveAsync(dispatch.Email, ct);

        // Read here and not carried on the dispatch: the request path needs no policy, and the queue could
        // hold a stale value for as long as the drain takes.
        var registration = authOptions.Value.RegistrationsOpen ? RegistrationState.Open : RegistrationState.Closed;
        var kind = LoginChallengePlan.Decide(subject, dispatch.CodeBudget, registration);

        // The cap on mails to addresses without an account is consulted BEFORE the record is written, and a
        // capped record carries no credential: a code nobody is mailed would still take three guesses, so
        // the cap would stop bounding them (security-auditor, 2026-09-20).
        var capAdmits = await CapAdmitsAsync(subject, ct);

        // The account's OWN address, never the submitted spelling:
        // Identity's lookup folds case, and with it a few non-ASCII letters, so the typed spelling can be
        // another inbox than the account's.
        var recipient = subject is LoginSubject.KnownAccount known ? known.AccountEmail : dispatch.Email;

        // A record for every admitted request, written BEFORE the mail: a code that arrives before its record
        // would read as expired (security-auditor, Q2 binding). The record replaces the address's live
        // challenge only when the request path's code budget admitted the mint, which it decided without
        // reading the account (security-auditor Q-S1).
        var issued = await store.PutAsync(
            new NewLoginChallenge(
                dispatch.ChallengeId,
                recipient,
                capAdmits ? LoginChallengePlan.CredentialsFor(kind) : ChallengeCredentials.None,
                ReplacesLiveChallenge: dispatch.CodeBudget == CodeBudgetState.Admitted),
            ct);

        if (kind == LoginChallengeKind.RecordOnly)
        {
            LogRecordOnly(logger, UserIdOf(subject));
            return;
        }

        if (!capAdmits)
        {
            LogUnknownAddressMailCapped(
                logger,
                LoginChallengePolicy.UnknownAddressMailBudget.Limit,
                LoginChallengePolicy.UnknownAddressMailBudget.Window);
            return;
        }

        try
        {
            await emailSender.SendLoginChallengeAsync(recipient, Content(kind, subject, issued), ct);
        }
        catch (EmailDeliveryException ex)
        {
            // With no break-glass, a delivery failure nobody sees is a login stop nobody sees
            // (security-auditor Q19): its own event, the kind and the underlying type, never the address.
            LogSendFailed(logger, kind, ex.UnderlyingErrorType);
            return;
        }

        if (UserIdOf(subject) is { } userId)
            audit.LoginChallengeIssued(userId, kind, dispatch.IpAddress, dispatch.UserAgent);
    }

    private static LoginChallengeEmail Content(
        LoginChallengeKind kind, LoginSubject subject, IssuedCredentials issued) =>
        (kind, subject) switch
        {
            (LoginChallengeKind.CodeAndLink, _) =>
                new LoginChallengeEmail.CodeAndLink(Required(issued.Code), Required(issued.Link)),
            (LoginChallengeKind.LinkOnly, _) => new LoginChallengeEmail.LinkOnly(Required(issued.Link)),
            (LoginChallengeKind.PendingDeletion, LoginSubject.PendingDeletion pending) =>
                new LoginChallengeEmail.PendingDeletion(AccountRestoreWindow.PermanentDeletionEarliest(pending.DeletedAt)),
            (LoginChallengeKind.RegistrationClosed, _) => new LoginChallengeEmail.RegistrationClosed(),
            (LoginChallengeKind.NewAccountCode, _) => new LoginChallengeEmail.NewAccountCode(Required(issued.Code)),
            (LoginChallengeKind.NewAccountCodeLimitReached, _) => new LoginChallengeEmail.NewAccountCodeLimitReached(),
            _ => throw new InvalidOperationException($"No mail content for {kind}."),
        };

    private static T Required<T>(T? value) where T : struct =>
        value ?? throw new InvalidOperationException("The store did not mint a credential the plan asked for.");

    private Task<bool> CapAdmitsAsync(LoginSubject subject, CancellationToken ct) => subject switch
    {
        LoginSubject.NoAccount => budget.TryConsumeAsync(
            LoginChallengePolicy.UnknownAddressMailBudget, LoginChallengePolicy.UnknownAddressMailSubject, ct),
        LoginSubject.KnownAccount => Task.FromResult(true),
        var other => throw new UnreachableException($"Unclassified login subject {other.GetType().Name}."),
    };

    private static Guid? UserIdOf(LoginSubject subject) =>
        subject is LoginSubject.KnownAccount known ? known.UserId : null;

    [LoggerMessage(1014, LogLevel.Warning,
        "Login challenge mail not sent ({ChallengeKind}, {ErrorType}) — the requester already received the "
        + "uniform 202 and cannot be told")]
    private static partial void LogSendFailed(ILogger logger, LoginChallengeKind challengeKind, string errorType);

    [LoggerMessage(1015, LogLevel.Warning,
        "Login challenge mail to an address without an account not sent: the global cap ({Limit} per "
        + "{Window}) is spent; the record is written")]
    private static partial void LogUnknownAddressMailCapped(ILogger logger, int limit, TimeSpan window);

    [LoggerMessage(1018, LogLevel.Warning,
        "Login challenge not mailed: the address has an Identity row without a profile ({UserId}); the "
        + "record is written without a credential")]
    private static partial void LogRecordOnly(ILogger logger, Guid? userId);
}

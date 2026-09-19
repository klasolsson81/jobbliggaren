using System.Diagnostics;
using Jobbliggaren.Application.Auth.Jobs.HardDeleteAccounts;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Exceptions;
using Microsoft.Extensions.Logging;

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
    ILogger<LoginChallengeIssuer> logger)
{
    public async Task IssueAsync(LoginChallengeDispatch dispatch, CancellationToken ct)
    {
        var subject = await subjects.ResolveAsync(dispatch.Email, ct);
        var kind = LoginChallengePlan.Decide(subject, dispatch.CodeBudget);

        // A record for every admitted request, written BEFORE the mail: a code that arrives before its record
        // would read as expired (security-auditor, Q2 binding). The record replaces the address's live
        // challenge only when the request path's code budget admitted the mint, which it decided without
        // reading the account (security-auditor Q-S1).
        var issued = await store.PutAsync(
            new NewLoginChallenge(
                dispatch.ChallengeId,
                dispatch.Email,
                LoginChallengePlan.CredentialsFor(kind),
                ReplacesLiveChallenge: dispatch.CodeBudget == CodeBudgetState.Admitted),
            ct);

        if (!await AdmitsMailAsync(subject, ct))
        {
            LogUnknownAddressMailCapped(
                logger,
                LoginChallengePolicy.UnknownAddressMailBudget.Limit,
                LoginChallengePolicy.UnknownAddressMailBudget.Window);
            return;
        }

        try
        {
            await emailSender.SendLoginChallengeAsync(dispatch.Email, Content(kind, subject, issued), ct);
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
            _ => throw new InvalidOperationException($"No mail content for {kind}."),
        };

    private static T Required<T>(T? value) where T : struct =>
        value ?? throw new InvalidOperationException("The store did not mint a credential the plan asked for.");

    private Task<bool> AdmitsMailAsync(LoginSubject subject, CancellationToken ct) => subject switch
    {
        LoginSubject.NoAccount or LoginSubject.ProfileMissing => budget.TryConsumeAsync(
            LoginChallengePolicy.UnknownAddressMailBudget, LoginChallengePolicy.UnknownAddressMailSubject, ct),
        LoginSubject.Active or LoginSubject.PendingDeletion => Task.FromResult(true),
        var other => throw new UnreachableException($"Unclassified login subject {other.GetType().Name}."),
    };

    private static Guid? UserIdOf(LoginSubject subject) => subject switch
    {
        LoginSubject.Active active => active.UserId,
        LoginSubject.PendingDeletion pending => pending.UserId,
        LoginSubject.ProfileMissing orphan => orphan.UserId,
        _ => null,
    };

    [LoggerMessage(1014, LogLevel.Warning,
        "Login challenge mail not sent ({ChallengeKind}, {ErrorType}) — the requester already received the "
        + "uniform 202 and cannot be told")]
    private static partial void LogSendFailed(ILogger logger, LoginChallengeKind challengeKind, string errorType);

    [LoggerMessage(1015, LogLevel.Warning,
        "Login challenge mail to an address without an account not sent: the global cap ({Limit} per "
        + "{Window}) is spent; the record is written")]
    private static partial void LogUnknownAddressMailCapped(ILogger logger, int limit, TimeSpan window);
}

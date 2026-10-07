using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.Dtos;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Application.Auth.LoginChallenges;

/// <summary>
/// The one grant of a passwordless session (ADR 0142 D3/D4). It takes an <see cref="LoginSubject.Active"/>
/// only, so the #1349 rule — no session for a soft-deleted or missing profile — holds by type. Every
/// session it creates is <see cref="SessionLifetime.Persistent"/>.
/// </summary>
public sealed class PasswordlessSessionGrant(
    IInboxProofRecorder inboxProof,
    ISessionStore sessions,
    IAuthAuditLogger audit,
    IAppDbContext db,
    IDateTimeProvider clock,
    ICorrelationIdProvider correlationId,
    IRequestContextProvider requestContext,
    IAccountAccessReader access,
    IAccountAccessCoordinator coordinator,
    IAccountAccessWriter accessWriter,
    IAccountAccessCleanup cleanup)
{
    /// <summary>The <c>audit_log</c> event for a first passwordless proof of an unconfirmed address.</summary>
    public const string InboxProvenAuditEventType = "User.InboxProvenByLogin";

    public async Task<SessionDto?> GrantAsync(
        LoginSubject.Active subject, LoginMethod method, AccountAccessProof original,
        Func<LoginSubject.Active, CancellationToken, Task<bool>>? linkBeforeSession, CancellationToken ct)
    {
        AccountAccessProof admitted;
        InboxProof proof;
        CommittedSessionAuthorization? authorization = null;
        if (coordinator.HasActiveScope)
            throw new InvalidOperationException("A login proof must own its transaction before issuing a session.");
        var hint = await access.ReadAsync(subject.UserId, ct);
        if (hint is null)
            return null;
        var firstProofCandidate = !hint.InboxConfirmed;
        await using (var scope = await coordinator.BeginAsync([subject.UserId], firstProofCandidate, ct))
        {
            if (!scope.OwnsCommit)
                throw new InvalidOperationException("A login proof cannot confirm a borrowed commit.");
            var account = await access.ReadAsync(subject.UserId, ct);
            if (account is null || !original.Admits(account)
                || (!firstProofCandidate && !account.InboxConfirmed)
                || !(ExternalProviderKey.Known.Any(provider => provider.LoginMethod == method)
                    ? ExternalAddressMatch.IsSameAddress(subject.AccountEmail, account.Email!)
                    : string.Equals(subject.AccountEmail, account.Email, StringComparison.Ordinal)))
                return null;
            admitted = original.Bind(account) with { ExpectedEmail = account.Email };
            if (linkBeforeSession is not null && !await linkBeforeSession(subject, ct))
                return null;
            proof = await inboxProof.RecordAsync(subject.UserId, ct);

            if (proof == InboxProof.FirstProofRecorded)
            {
                var transition = await accessWriter.AdvanceCredentialsAsync(subject.UserId, ct);
                authorization = CommittedSessionAuthorization.AfterFirstInboxProof(transition);
                // The address is confirmed and every earlier session is revoked, a change to the account's security
                // state on a known user id, so it is an audit_log row, not an ops line; the row is written after
                // proof, so it says nothing about whether any other address has an account (security-auditor Q-S3).
                db.AuditLogEntries.Add(AuditLogEntry.Create(
                    occurredAt: clock.UtcNow,
                    correlationId: correlationId.Current,
                    userId: subject.UserId,
                    eventType: InboxProvenAuditEventType,
                    aggregateType: "User",
                    aggregateId: subject.UserId,
                    ipAddress: requestContext.IpAddress,
                    userAgent: requestContext.UserAgent));
            }
            await db.SaveChangesAsync(ct);
            await scope.CommitAsync(ct);
        }
        if (authorization is not null)
        {
            authorization.ConfirmCommit();
            await cleanup.CompleteAsync(new AccountAccessChanged(
                subject.UserId, false, authorization.AccessRevision, false), CancellationToken.None);
        }

        var session = authorization is null
            ? await sessions.CreateAsync(subject.UserId, admitted, SessionLifetime.Persistent, CancellationToken.None)
            : await sessions.CreateCommittedAsync(authorization, SessionLifetime.Persistent, CancellationToken.None);
        if (session is null)
            return null;
        audit.LoginSucceeded(subject.UserId, session.Id.ToString(), method);
        return new SessionDto(session.Id.Reveal());
    }
}

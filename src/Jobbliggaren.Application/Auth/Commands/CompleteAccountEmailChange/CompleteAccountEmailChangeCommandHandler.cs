using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.AccountEmailChanges;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Application.Auth.Commands.CompleteAccountEmailChange;

/// <summary>
/// #1975 (ADR 0153) — the store judges the presentation before anything about any account is read; only a full match
/// goes further. From there the change is consumed, and every refusal is the same one answer: an account that is no
/// longer active, one that now holds Admin or holds another address than the one the change started from, or a new
/// address someone else has taken. The swap, the notice to the address it replaced and the audit row follow, and
/// post-commit notification failures retain the known success receipt (ADR 0155).
/// </summary>
public sealed partial class CompleteAccountEmailChangeCommandHandler(
    IEmailSender emailSender,
    IAccountEmailChangeStore store,
    IAppDbContext db,
    ConfirmedAddressSwap addressSwap,
    IDateTimeProvider clock,
    ICorrelationIdProvider correlationId,
    IRequestContextProvider requestContext,
    ILogger<CompleteAccountEmailChangeCommandHandler> logger,
    IAccountAccessCoordinator coordinator,
    IAccountAccessReader access,
    IAccountEmailChangeRequests requests)
    : ICommandHandler<CompleteAccountEmailChangeCommand, Result<AccountEmailChangeOutcome>>
{
    /// <summary>The <c>audit_log</c> event of a completed change, told apart from self-service's <c>User.EmailChanged</c>.</summary>
    public const string CompletedAuditEventType = "User.EmailChangedViaAdministrator";

    public async ValueTask<Result<AccountEmailChangeOutcome>> Handle(
        CompleteAccountEmailChangeCommand command, CancellationToken cancellationToken)
    {
        // Decided by the handler's first statement, before the store is asked.
        if (!emailSender.CanDeliver)
            return Failure(DomainError.Validation(
                AuthErrorCodes.EmailDeliveryUnavailable, AuthErrorCodes.EmailDeliveryUnavailableMessage));

        // The validator guarantees all three; re-asserted so the handler is correct in isolation.
        if (string.IsNullOrEmpty(command.CurrentEmail)
            || string.IsNullOrEmpty(command.NewEmail)
            || string.IsNullOrEmpty(command.Code))
        {
            return Failure(DomainError.Validation(AuthErrorCodes.InvalidInput, "Adresserna och koden krävs."));
        }

        var verdict = await store.ConsumeAsync(
            command.NewEmail, command.CurrentEmail, LoginCode.FromRaw(command.Code), cancellationToken);
        if (verdict is AccountEmailChangeVerdict.NotYet notYet)
            return Outcome(new AccountEmailChangeOutcome.NotYet(notYet.CompletableFrom));

        if (verdict is not AccountEmailChangeVerdict.Verified { Proof: var proof })
            return Unusable();

        long revision;
        if (coordinator.HasActiveScope)
            throw new InvalidOperationException("Public address completion must own its transaction.");
        try
        {
            await using (var scope = await coordinator.BeginAsync([proof.UserId], true, cancellationToken))
            {
                var account = await access.ReadAsync(proof.UserId, cancellationToken);
                if (account is null || !proof.Access.Admits(account))
                    return RefusedAfterMatch(proof.UserId, "AccessProofStale");
                if (proof.RequestId is not { } requestId || requestId == Guid.Empty
                    || proof.ExpiresAt <= proof.IssuedAt || proof.ExpiresAt - proof.IssuedAt != AccountEmailChangePolicy.Ttl
                    || !await requests.HasCommittedRequestAsync(
                        proof.UserId, requestId, proof.IssuedAt, proof.ExpiresAt, cancellationToken))
                    return RefusedAfterMatch(proof.UserId, "CommittedRequestUnavailable");
                var moved = await addressSwap.MoveAsync(
                    proof.UserId, proof.NewEmail, SwapPrecondition.AdminInitiated(proof.ExpectedCurrent), cancellationToken);
                if (moved.IsFailure)
                    return RefusedAfterMatch(proof.UserId, moved.Error.Code);
                revision = moved.Value.AccessRevision;
                RecordCompletion(proof.UserId);
                await db.SaveChangesAsync(cancellationToken);
                await scope.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            addressSwap.DiscardNotices();
            throw;
        }
        await addressSwap.NotifyCommittedAsync(CancellationToken.None);
        return Outcome(new AccountEmailChangeOutcome.Completed(proof.UserId, revision));
    }

    // The account's own user id, so the eraser that anonymises an account's rows reaches this one, IP and user agent
    // included; AuditBehavior would stamp the anonymous caller, null.
    private void RecordCompletion(Guid targetUserId)
    {
        db.AuditLogEntries.Add(AuditLogEntry.Create(
            occurredAt: clock.UtcNow,
            correlationId: correlationId.Current,
            userId: targetUserId,
            eventType: CompletedAuditEventType,
            aggregateType: "User",
            aggregateId: targetUserId,
            ipAddress: requestContext.IpAddress,
            userAgent: requestContext.UserAgent));
    }

    private Result<AccountEmailChangeOutcome> RefusedAfterMatch(Guid targetUserId, string reason)
    {
        LogRefusedAfterMatch(targetUserId, reason);
        return Unusable();
    }

    private static Result<AccountEmailChangeOutcome> Unusable() =>
        Failure(DomainError.Gone(
            AuthErrorCodes.AccountEmailChangeUnusable, AuthErrorCodes.AccountEmailChangeUnusableMessage));

    private static Result<AccountEmailChangeOutcome> Outcome(AccountEmailChangeOutcome outcome) =>
        Result.Success(outcome);

    private static Result<AccountEmailChangeOutcome> Failure(DomainError error) =>
        Result.Failure<AccountEmailChangeOutcome>(error);

    [LoggerMessage(4005, LogLevel.Warning,
        "Account email change: refused after a full match for user {TargetUserId} ({Reason}); the change is consumed")]
    private partial void LogRefusedAfterMatch(Guid targetUserId, string reason);

}

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
/// nothing here throws once the swap has committed, so the endpoint's teardown always runs.
/// </summary>
public sealed partial class CompleteAccountEmailChangeCommandHandler(
    IEmailSender emailSender,
    IAccountEmailChangeStore store,
    IAppDbContext db,
    ConfirmedAddressSwap addressSwap,
    IDateTimeProvider clock,
    ICorrelationIdProvider correlationId,
    IRequestContextProvider requestContext,
    ILogger<CompleteAccountEmailChangeCommandHandler> logger)
    : ICommandHandler<CompleteAccountEmailChangeCommand, Result<AccountEmailChangeOutcome>>
{
    /// <summary>The <c>audit_log</c> event of a completed change, told apart from self-service's <c>User.EmailChanged</c>.</summary>
    public const string CompletedAuditEventType = "User.EmailChangedViaAdministrator";

    public async ValueTask<Result<AccountEmailChangeOutcome>> Handle(
        CompleteAccountEmailChangeCommand command, CancellationToken cancellationToken)
    {
        // Decided before any input is read, so the 503 cannot vary with what was presented.
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

        if (!await db.HasLiveProfileAsync(proof.UserId, cancellationToken))
            return RefusedAfterMatch(proof.UserId, "ProfileNotLive");

        // The account must still hold the address the change was started from and must not hold Admin, checked on the
        // instance the swap loads, whose concurrency stamp guards its first write.
        var moved = await addressSwap.MoveAsync(
            proof.UserId,
            proof.NewEmail,
            SwapPrecondition.AdminInitiated(proof.ExpectedCurrent),
            cancellationToken);
        if (moved.IsFailure)
            return RefusedAfterMatch(proof.UserId, moved.Error.Code);

        return Outcome(new AccountEmailChangeOutcome.Completed(proof.UserId, await RecordCompletionAsync(proof.UserId)));
    }

    // The account's own user id, so the eraser that anonymises an account's rows reaches this one, IP and user agent
    // included; AuditBehavior would stamp the anonymous caller, null.
    private async Task<bool> RecordCompletionAsync(Guid targetUserId)
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

        try
        {
            await db.SaveChangesAsync(CancellationToken.None);
            return true;
        }
        catch (Exception ex)
        {
            // The address has moved, so this must not throw: the teardown has yet to run. Cleared, so the pipeline's
            // own save does not try the row again.
            db.ClearTracking();
            LogAuditRowNotWritten(targetUserId, ex.GetType().Name);
            return false;
        }
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

    [LoggerMessage(4006, LogLevel.Error,
        "Account email change: the audit row was not written for user {TargetUserId} ({ErrorType}); the address had moved")]
    private partial void LogAuditRowNotWritten(Guid targetUserId, string errorType);
}

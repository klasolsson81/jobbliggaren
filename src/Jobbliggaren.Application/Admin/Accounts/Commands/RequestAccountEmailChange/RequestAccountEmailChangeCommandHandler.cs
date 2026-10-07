using System.Text.Json;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.AccountEmailChanges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.Admin.Accounts.Commands.RequestAccountEmailChange;

/// <summary>Admits original authority, sends both mails without database locks, then commits one exact activation.</summary>
public sealed partial class RequestAccountEmailChangeCommandHandler(
    ICurrentUser currentUser,
    IUserAccountService userAccountService,
    IRateBudget budget,
    IOptions<AuthEmailCooldownOptions> cooldownOptions,
    IAccountEmailChangeStore store,
    IEmailSender emailSender,
    IAccountAccessReader access,
    ILogger<RequestAccountEmailChangeCommandHandler> logger,
    IAccountAccessCoordinator coordinator,
    IAppDbContext db,
    IDateTimeProvider clock,
    ICorrelationIdProvider correlationId,
    IRequestContextProvider requestContext)
    : ICommandHandler<RequestAccountEmailChangeCommand, Result<AccountEmailChangePending>>
{
    private readonly TimeSpan _window = TimeSpan.FromSeconds(cooldownOptions.Value.ChangeEmailWindowSeconds);

    public async ValueTask<Result<AccountEmailChangePending>> Handle(RequestAccountEmailChangeCommand command, CancellationToken cancellationToken)
    {
        if (coordinator.HasActiveScope)
            throw new InvalidOperationException("An administrative address request must own its split-phase transactions.");
        if (!emailSender.CanDeliver)
            return Failure(DomainError.Validation(AuthErrorCodes.EmailDeliveryUnavailable, AuthErrorCodes.EmailDeliveryUnavailableMessage));
        if (currentUser.UserId is not { } actorId)
            return Failure(DomainError.Validation(AuthErrorCodes.NotAuthenticated, "Inloggning krävs för att byta e-postadress."));
        if (string.IsNullOrEmpty(command.NewEmail) || string.IsNullOrEmpty(command.ReauthGrant))
            return Failure(DomainError.Validation(AuthErrorCodes.InvalidInput, "Ny e-postadress krävs."));
        if (command.UserId == actorId)
            return Failure(AdministratorTarget());

        var targetId = command.UserId;
        var newEmail = command.NewEmail;
        AccountAccessProof originalActor;
        AccountAccessProof originalTarget;
        string currentEmail;
        AccountEmailChangePut.Written written;
        await using (var scope = await coordinator.BeginAsync([actorId, targetId], false, cancellationToken))
        {
            RequireOwner(scope);
            var actor = await access.ReadAsync(actorId, cancellationToken);
            var actorProof = await access.ReadCurrentProofAsync(currentUser, cancellationToken);
            if (actor is null || !actor.IsAdmin || actorProof is null)
                return Unusable();
            originalActor = actorProof with { ExpectedEmail = actor.Email, ExpectedCutoff = actor.CredentialCutoff };
            var target = await access.ReadAsync(targetId, cancellationToken);
            if (target is null)
                return Failure(DomainError.NotFound(AuthErrorCodes.UserNotFound, "Användaren hittades inte."));
            if (target.IsAdmin)
                return Failure(AdministratorTarget());
            if (!target.CanAuthenticate)
                return Failure(InactiveTarget());
            currentEmail = target.Email!;
            var free = await userAccountService.CheckAddressIsFreeAsync(targetId, newEmail, cancellationToken);
            if (free.IsFailure)
                return Failure(free.Error);
            if (!await budget.TryConsumeAsync(ChangeEmailPolicy.TargetCooldown(_window), newEmail, cancellationToken)
                || !await budget.TryConsumeAsync(ChangeEmailPolicy.PerTargetDailyBudget, newEmail, cancellationToken))
                return Failure(DomainError.Conflict(AuthErrorCodes.ChangeEmailCooldown, AuthErrorCodes.ChangeEmailCooldownMessage));
            originalTarget = new AccountAccessProof(originalActor.FlowEpoch, targetId, target.AccessRevision)
            { ExpectedEmail = currentEmail, ExpectedCutoff = target.CredentialCutoff };
            var put = await store.PutAsync(new NewAccountEmailChange(targetId, newEmail, currentEmail)
            { Access = originalTarget, RequestId = command.RequestId }, cancellationToken);
            if (put is not AccountEmailChangePut.Written stored)
                return Failure(DomainError.Conflict(AuthErrorCodes.AccountEmailChangePendingForAnotherAccount,
                    AuthErrorCodes.AccountEmailChangePendingForAnotherAccountMessage));
            written = stored;
            await scope.CommitAsync(cancellationToken);
        }

        try
        {
            await emailSender.SendAccountEmailChangeRequestedNotificationAsync(
                currentEmail, written.CompletableFrom, written.ExpiresAt, cancellationToken);
            await emailSender.SendLoginChallengeAsync(newEmail,
                new LoginChallengeEmail.AccountEmailChangeCode(written.Code, written.CompletableFrom, written.ExpiresAt), cancellationToken);
        }
        catch (Exception failure)
        {
            await RevokeAsync(written.Receipt, targetId);
            LogSendFailed(targetId, failure.GetType().Name);
            throw;
        }

        await using (var scope = await coordinator.BeginAsync([actorId, targetId], false, cancellationToken))
        {
            RequireOwner(scope);
            var actor = await access.ReadAsync(actorId, cancellationToken);
            var target = await access.ReadAsync(targetId, cancellationToken);
            var pending = await store.FindPendingAsync(targetId, cancellationToken);
            if (actor is null || !actor.IsAdmin || !originalActor.Admits(actor))
                return Unusable();
            if (target is null || !originalTarget.Admits(target) || target.IsAdmin)
                return Failure(InactiveTarget());
            if (pending is null || pending.State != PendingAccountEmailChangeState.Pending
                || pending.RequestId != command.RequestId || pending.AccessRevision != originalTarget.AccessRevision
                || pending.CompletableFrom != written.CompletableFrom || pending.ExpiresAt != written.ExpiresAt
                || pending.ExpiresAt <= clock.UtcNow)
                return Unusable();
            var free = await userAccountService.CheckAddressIsFreeAsync(targetId, newEmail, cancellationToken);
            if (free.IsFailure)
                return Failure(free.Error);
            db.AuditLogEntries.Add(AuditLogEntry.Create(
                occurredAt: clock.UtcNow, correlationId: correlationId.Current, userId: actorId,
                eventType: RequestAccountEmailChangeCommand.RequestedEventType, aggregateType: "User", aggregateId: targetId,
                ipAddress: requestContext.IpAddress, userAgent: requestContext.UserAgent,
                payload: JsonSerializer.Serialize(new { requestId = command.RequestId })));
            await db.SaveChangesAsync(cancellationToken);
            await scope.CommitAsync(cancellationToken);
        }
        return Result.Success(new AccountEmailChangePending(written.CompletableFrom, written.ExpiresAt));
    }

    private async Task RevokeAsync(AccountEmailChangeReceipt receipt, Guid targetId)
    {
        try { await store.RevokeAsync(receipt, CancellationToken.None); }
        catch (Exception failure) { LogRevokeFailed(targetId, failure.GetType().Name); }
    }

    private static void RequireOwner(IAccountAccessScope scope)
    {
        if (!scope.OwnsCommit)
            throw new InvalidOperationException("An administrative address request cannot borrow a commit.");
    }

    private static DomainError AdministratorTarget() => DomainError.Conflict(
        AuthErrorCodes.AccountEmailChangeAdministratorTarget, AuthErrorCodes.AccountEmailChangeAdministratorTargetMessage);
    private static DomainError InactiveTarget() => DomainError.Conflict(
        AuthErrorCodes.AccountEmailChangeInactiveTarget, AuthErrorCodes.AccountEmailChangeInactiveTargetMessage);
    private static Result<AccountEmailChangePending> Unusable() => Failure(DomainError.Gone(
        AuthErrorCodes.AccountEmailChangeUnusable, AuthErrorCodes.AccountEmailChangeUnusableMessage));
    private static Result<AccountEmailChangePending> Failure(DomainError error) => Result.Failure<AccountEmailChangePending>(error);

    [LoggerMessage(4003, LogLevel.Warning,
        "Account email change: transport failed for user {TargetUserId} ({ErrorType}); activation was not committed")]
    private partial void LogSendFailed(Guid targetUserId, string errorType);
    [LoggerMessage(4004, LogLevel.Error,
        "Account email change: request for user {TargetUserId} could not be removed ({ErrorType}); its uncommitted proof remains inert")]
    private partial void LogRevokeFailed(Guid targetUserId, string errorType);
}

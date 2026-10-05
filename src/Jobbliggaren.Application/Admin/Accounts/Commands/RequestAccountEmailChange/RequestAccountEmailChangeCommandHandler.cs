using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.AccountEmailChanges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.Admin.Accounts.Commands.RequestAccountEmailChange;

/// <summary>
/// #1975 (ADR 0153) — the gates, cheapest first, then the pending change and its two mails. Every refusal comes before
/// any mail is sent or any budget is spent, except the one only the store can answer (another account's change already
/// holds the address), which comes after the budgets.
/// </summary>
public sealed partial class RequestAccountEmailChangeCommandHandler(
    ICurrentUser currentUser,
    IAccountDirectory directory,
    IUserAccountService userAccountService,
    IRateBudget budget,
    IOptions<AuthEmailCooldownOptions> cooldownOptions,
    IAccountEmailChangeStore store,
    IEmailSender emailSender,
    ILogger<RequestAccountEmailChangeCommandHandler> logger)
    : ICommandHandler<RequestAccountEmailChangeCommand, Result<AccountEmailChangePending>>
{
    private readonly TimeSpan _window = TimeSpan.FromSeconds(cooldownOptions.Value.ChangeEmailWindowSeconds);

    public async ValueTask<Result<AccountEmailChangePending>> Handle(
        RequestAccountEmailChangeCommand command, CancellationToken cancellationToken)
    {
        // Two mails are the change: refused before anything is read when they cannot be sent.
        if (!emailSender.CanDeliver)
            return Failure(DomainError.Validation(
                AuthErrorCodes.EmailDeliveryUnavailable, AuthErrorCodes.EmailDeliveryUnavailableMessage));

        // The pipeline's gates ran before this handler; re-asserted so the handler is correct in isolation.
        if (!currentUser.UserId.HasValue)
            return Failure(DomainError.Validation(
                AuthErrorCodes.NotAuthenticated, "Inloggning krävs för att byta e-postadress."));

        if (string.IsNullOrEmpty(command.NewEmail))
            return Failure(DomainError.Validation(AuthErrorCodes.InvalidInput, "Ny e-postadress krävs."));

        var newEmail = command.NewEmail;

        // The administrator's own address changes on Mina sidor, which proves both inboxes.
        if (command.UserId == currentUser.UserId.Value)
            return Failure(AdministratorTarget());

        // The role, the status and the stored address, read fresh in one statement: never inferred from the address.
        if (await directory.FindAsync(command.UserId, cancellationToken) is not { } account)
            return Failure(DomainError.NotFound(AuthErrorCodes.UserNotFound, "Användaren hittades inte."));

        if (account.IsAdmin)
            return Failure(AdministratorTarget());

        if (account.Status != AccountStatus.Active || account.Email is not { Length: > 0 } currentEmail)
            return Failure(DomainError.Conflict(
                AuthErrorCodes.AccountEmailChangeInactiveTarget, AuthErrorCodes.AccountEmailChangeInactiveTargetMessage));

        // Storable, and free as an address and as a user name; the account's own address is taken by the account.
        var free = await userAccountService.CheckAddressIsFreeAsync(account.UserId, newEmail, cancellationToken);
        if (free.IsFailure)
            return Failure(free.Error);

        // The two budgets keyed by the new address, shared with self-service, so that whoever asks, an address gets
        // the same number of codes a day. Never the account's own per-user budgets: those are its owner's.
        if (!await budget.TryConsumeAsync(ChangeEmailPolicy.TargetCooldown(_window), newEmail, cancellationToken)
            || !await budget.TryConsumeAsync(ChangeEmailPolicy.PerTargetDailyBudget, newEmail, cancellationToken))
        {
            return Failure(DomainError.Conflict(
                AuthErrorCodes.ChangeEmailCooldown, AuthErrorCodes.ChangeEmailCooldownMessage));
        }

        var put = await store.PutAsync(new NewAccountEmailChange(account.UserId, newEmail, currentEmail), cancellationToken);
        if (put is not AccountEmailChangePut.Written written)
            return Failure(DomainError.Conflict(
                AuthErrorCodes.AccountEmailChangePendingForAnotherAccount,
                AuthErrorCodes.AccountEmailChangePendingForAnotherAccountMessage));

        // The notice first, then the code, both awaited: a change whose two mails were not both accepted never
        // becomes completable, and no request row is written for it.
        try
        {
            await emailSender.SendAccountEmailChangeRequestedNotificationAsync(
                currentEmail, written.CompletableFrom, written.ExpiresAt, cancellationToken);
            await emailSender.SendLoginChallengeAsync(
                newEmail,
                new LoginChallengeEmail.AccountEmailChangeCode(written.Code, written.CompletableFrom, written.ExpiresAt),
                cancellationToken);
        }
        catch (Exception sendFailure)
        {
            // Every exception, not a list of them: one that slipped past would leave a live change with no request
            // row. The record this request wrote, and no newer one, is removed; the send's own failure is rethrown.
            await RevokeAsync(written.Receipt, account.UserId);
            LogSendFailed(account.UserId, sendFailure.GetType().Name);
            throw;
        }

        return Result.Success(new AccountEmailChangePending(written.CompletableFrom, written.ExpiresAt));
    }

    private async Task RevokeAsync(AccountEmailChangeReceipt receipt, Guid targetUserId)
    {
        try
        {
            // Not the request's token: a disconnect is among the failures this removal answers.
            await store.RevokeAsync(receipt, CancellationToken.None);
        }
        catch (Exception revokeFailure)
        {
            // The send's failure is the one the caller gets; this one leaves a change nobody holds a code for.
            LogRevokeFailed(targetUserId, revokeFailure.GetType().Name);
        }
    }

    private static DomainError AdministratorTarget() =>
        DomainError.Conflict(
            AuthErrorCodes.AccountEmailChangeAdministratorTarget,
            AuthErrorCodes.AccountEmailChangeAdministratorTargetMessage);

    private static Result<AccountEmailChangePending> Failure(DomainError error) =>
        Result.Failure<AccountEmailChangePending>(error);

    [LoggerMessage(4003, LogLevel.Warning,
        "Account email change: a mail was not accepted for user {TargetUserId} ({ErrorType}); the change was removed")]
    private partial void LogSendFailed(Guid targetUserId, string errorType);

    [LoggerMessage(4004, LogLevel.Error,
        "Account email change: the change for user {TargetUserId} could not be removed after a failed mail "
        + "({ErrorType}); it stays pending until it expires or is cancelled")]
    private partial void LogRevokeFailed(Guid targetUserId, string errorType);
}

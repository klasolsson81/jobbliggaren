using System.Text.Json;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.Auth.Commands.ChangeEmail;

/// <summary>Admits one original request, sends outside database locks, then commits its exact activation witness.</summary>
public sealed partial class ChangeEmailCommandHandler(
    ICurrentUser currentUser,
    IUserAccountService userAccountService,
    IEmailSender emailSender,
    IRateBudget budget,
    IOptions<AuthEmailCooldownOptions> cooldownOptions,
    ILoginChallengeStore store,
    IAccountAccessReader access,
    IAccountAccessCoordinator coordinator,
    IAppDbContext db,
    IDateTimeProvider clock,
    ICorrelationIdProvider correlationId,
    IRequestContextProvider requestContext,
    ILogger<ChangeEmailCommandHandler> logger)
    : ICommandHandler<ChangeEmailCommand, Result<EmailChangeChallenge>>
{
    private readonly TimeSpan _window = TimeSpan.FromSeconds(cooldownOptions.Value.ChangeEmailWindowSeconds);

    public async ValueTask<Result<EmailChangeChallenge>> Handle(ChangeEmailCommand command, CancellationToken cancellationToken)
    {
        if (coordinator.HasActiveScope)
            throw new InvalidOperationException("An address request must own its split-phase transactions.");
        if (currentUser.UserId is not { } userId)
            return Failure(DomainError.Validation(AuthErrorCodes.NotAuthenticated, "Inloggning krävs för att byta e-postadress."));
        if (string.IsNullOrEmpty(command.ReauthGrant) || string.IsNullOrEmpty(command.NewEmail))
            return Failure(DomainError.Validation(AuthErrorCodes.InvalidInput, "Ny e-postadress krävs."));
        if (!emailSender.CanDeliver)
            return Failure(DomainError.Validation(AuthErrorCodes.EmailDeliveryUnavailable, AuthErrorCodes.EmailDeliveryUnavailableMessage));

        var newEmail = command.NewEmail;
        if (!await budget.TryConsumeAsync(ChangeEmailPolicy.UserCooldown(_window), userId.ToString(), cancellationToken))
            return Failure(Cooldown());
        if (!await budget.TryConsumeAsync(ChangeEmailPolicy.UserTargetsDailyBudget, userId.ToString(), cancellationToken))
            return Failure(DomainError.Conflict(AuthErrorCodes.ChangeEmailTargetBudgetExhausted, AuthErrorCodes.ChangeEmailTargetBudgetExhaustedMessage));
        if (!await budget.TryConsumeAsync(ChangeEmailPolicy.TargetCooldown(_window), newEmail, cancellationToken)
            || !await budget.TryConsumeAsync(ChangeEmailPolicy.PerTargetDailyBudget, newEmail, cancellationToken))
            return Failure(Cooldown());

        AccountAccessProof original;
        var challengeId = ChallengeId.Generate();
        var binding = new ChallengeBinding(ChallengePurpose.ChangeEmail, userId);
        EmailChangeRequestProof request;
        LoginCode code;
        await using (var scope = await coordinator.BeginAsync([userId], false, cancellationToken))
        {
            RequireOwner(scope);
            var account = await access.ReadAsync(userId, cancellationToken);
            var proof = await access.ReadCurrentProofAsync(currentUser, cancellationToken);
            if (account is null || proof is null)
                return Unusable();
            original = proof with { ExpectedEmail = account.Email, ExpectedCutoff = account.CredentialCutoff };
            var free = await userAccountService.CheckAddressIsFreeAsync(userId, newEmail, cancellationToken);
            if (free.IsFailure)
                return Failure(free.Error);
            var issuedAt = DateTimeOffset.FromUnixTimeMilliseconds(clock.UtcNow.ToUnixTimeMilliseconds());
            request = new EmailChangeRequestProof(challengeId.Reveal(), issuedAt, issuedAt + LoginChallengePolicy.ChallengeTtl);
            code = await store.PutBoundAsync(new NewBoundChallenge(challengeId, newEmail, binding)
            { Access = original, EmailChangeRequest = request }, cancellationToken);
            await scope.CommitAsync(cancellationToken);
        }

        try
        {
            await emailSender.SendLoginChallengeAsync(newEmail, new LoginChallengeEmail.AddressChangeCode(code), cancellationToken);
        }
        catch (Exception failure)
        {
            await RevokeAsync(challengeId, binding);
            LogActivationFailed(userId, failure.GetType().Name);
            throw;
        }

        await using (var scope = await coordinator.BeginAsync([userId], false, cancellationToken))
        {
            RequireOwner(scope);
            var account = await access.ReadAsync(userId, cancellationToken);
            var pending = await store.ReadEmailChangeRequestAsync(challengeId, userId, cancellationToken);
            if (account is null || !original.Admits(account) || request.ExpiresAt <= clock.UtcNow
                || pending?.EmailChangeRequest != request || pending.Access.FlowEpoch != original.FlowEpoch
                || pending.Access.UserId != userId || pending.Access.AccessRevision != original.AccessRevision
                || !string.Equals(pending.ProvenEmail, newEmail, StringComparison.Ordinal))
                return Unusable();
            var free = await userAccountService.CheckAddressIsFreeAsync(userId, newEmail, cancellationToken);
            if (free.IsFailure)
                return Failure(free.Error);
            db.AuditLogEntries.Add(AuditLogEntry.Create(
                occurredAt: clock.UtcNow, correlationId: correlationId.Current, userId: userId,
                eventType: ChangeEmailCommand.RequestedEventType, aggregateType: "User", aggregateId: userId,
                ipAddress: requestContext.IpAddress, userAgent: requestContext.UserAgent,
                payload: JsonSerializer.Serialize(new { requestId = request.RequestId })));
            await db.SaveChangesAsync(cancellationToken);
            await scope.CommitAsync(cancellationToken);
        }
        return Result.Success(new EmailChangeChallenge(userId, challengeId));
    }

    private async Task RevokeAsync(ChallengeId id, ChallengeBinding binding)
    {
        try { await store.RevokeBoundAsync(id, binding, CancellationToken.None); }
        catch (Exception failure) { LogCleanupFailed(binding.UserId, failure.GetType().Name); }
    }

    private static void RequireOwner(IAccountAccessScope scope)
    {
        if (!scope.OwnsCommit)
            throw new InvalidOperationException("An address request cannot borrow a commit.");
    }

    private static Result<EmailChangeChallenge> Failure(DomainError error) => Result.Failure<EmailChangeChallenge>(error);
    private static Result<EmailChangeChallenge> Unusable() => Failure(DomainError.Gone(
        AuthErrorCodes.EmailChangeGrantUnusable, AuthErrorCodes.EmailChangeGrantUnusableMessage));
    private static DomainError Cooldown() => DomainError.Conflict(AuthErrorCodes.ChangeEmailCooldown, AuthErrorCodes.ChangeEmailCooldownMessage);

    [LoggerMessage(1032, LogLevel.Warning, "Address request for user {UserId} was not activated after transport failed ({ErrorType})")]
    private partial void LogActivationFailed(Guid userId, string errorType);
    [LoggerMessage(1033, LogLevel.Warning, "Unactivated address request for user {UserId} could not be removed ({ErrorType}); committed witness remains authoritative")]
    private partial void LogCleanupFailed(Guid userId, string errorType);
}

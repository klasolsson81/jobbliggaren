using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;

namespace Jobbliggaren.Application.Auth.Registration;

/// <summary>
/// Opens the account an address will log in to (ADR 0142 D3, D6): the passwordless Identity row, the profile
/// stamped with the current terms, and the <see cref="AccountCreatedAuditEventType"/> row. The one writer of a
/// new account, shared by <c>complete</c> and the Development seed seam, so the seam can only produce the
/// state <c>complete</c> produces.
/// </summary>
public sealed class AccountRegistrar(
    IPasswordlessAccountCreator accounts,
    IAppDbContext db,
    IDateTimeProvider clock,
    ICorrelationIdProvider correlationId,
    IRequestContextProvider requestContext,
    IAccountAccessCoordinator coordinator)
{
    public const string AccountCreatedAuditEventType = "User.AccountCreated";

    /// <summary>
    /// Success when the address has an account afterwards: this one, or one that won a race the caller's
    /// claim does not cover. Uniqueness lives in Identity, so the duplicate is handled even for the caller
    /// that won the claim.
    /// </summary>
    public async Task<Result> OpenAsync(string email, CancellationToken ct)
    {
        var userId = Guid.NewGuid();
        await using var scope = await coordinator.BeginAsync([userId], false, ct);
        var created = await accounts.CreatePasswordlessUserAsync(userId, email, ct);
        if (created.IsFailure)
            return created.Error.Code == AuthErrorCodes.DuplicateAccount ? Result.Success() : Result.Failure(created.Error);

        var seeker = JobSeeker.Register(created.Value, TermsAcceptance.AcceptCurrent(clock), clock);
        if (seeker.IsFailure)
            return Result.Failure(seeker.Error);

        db.JobSeekers.Add(seeker.Value);
        db.AuditLogEntries.Add(AuditLogEntry.Create(
            occurredAt: clock.UtcNow,
            correlationId: correlationId.Current,
            userId: created.Value,
            eventType: AccountCreatedAuditEventType,
            aggregateType: "User",
            aggregateId: created.Value,
            ipAddress: requestContext.IpAddress,
            userAgent: requestContext.UserAgent));

        await db.SaveChangesAsync(ct);
        await scope.CommitAsync(ct);
        return Result.Success();
    }
}

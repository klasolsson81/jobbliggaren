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
    IRequestContextProvider requestContext)
{
    public const string AccountCreatedAuditEventType = "User.AccountCreated";

    /// <summary>
    /// Success when the address has an account afterwards: this one, or one that won a race the caller's
    /// claim does not cover. Uniqueness lives in Identity, so the duplicate is handled even for the caller
    /// that won the claim.
    /// </summary>
    public async Task<Result> OpenAsync(string email, CancellationToken ct)
    {
        var created = await accounts.CreatePasswordlessUserAsync(email, ct);
        if (created.IsFailure)
            return created.Error.Code == AuthErrorCodes.DuplicateAccount ? Result.Success() : Result.Failure(created.Error);

        var seeker = JobSeeker.Register(created.Value, TermsAcceptance.AcceptCurrent(clock), clock);
        if (seeker.IsFailure)
        {
            await accounts.DeleteAsync(created.Value, ct);
            return Result.Failure(seeker.Error);
        }

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

        // Saved here, not by the unit-of-work behavior after the handler: the caller reads the profile back
        // from the database, and an unsaved Add is invisible to it. A save that throws is followed by nothing,
        // so no session exists for an account whose profile did not commit (#1349); the orphan sweep collects
        // the Identity row.
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

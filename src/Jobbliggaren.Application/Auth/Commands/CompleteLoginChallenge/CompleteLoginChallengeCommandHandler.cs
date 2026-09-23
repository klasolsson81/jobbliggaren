using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Auth.Registration;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Mediator;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.Auth.Commands.CompleteLoginChallenge;

/// <summary>
/// #1737 — the last step of registering by a proven inbox (ADR 0142 D3). Every refusal of the grant is one
/// answer, and the session is opened by the outcome function the two proof handlers share, so this handler
/// reaches neither the session grant nor the password surface.
/// </summary>
public sealed class CompleteLoginChallengeCommandHandler(
    IOptions<AuthOptions> authOptions,
    IGrantStore grants,
    IRegistrationClaim claim,
    LoginSubjectResolver subjects,
    IPasswordlessAccountCreator accounts,
    IAppDbContext db,
    IDateTimeProvider clock,
    ICorrelationIdProvider correlationId,
    IRequestContextProvider requestContext,
    LoginProofOutcome outcome)
    : ICommandHandler<CompleteLoginChallengeCommand, Result<LoginOutcome>>
{
    public const string AccountCreatedAuditEventType = "User.AccountCreated";

    public async ValueTask<Result<LoginOutcome>> Handle(
        CompleteLoginChallengeCommand command, CancellationToken cancellationToken)
    {
        // The kill-switch, first and reading no input, as RegisterCommandHandler has it. Nothing below runs
        // while it is off, so a closed host never reaches the grant store or the claim.
        if (!authOptions.Value.RegistrationsOpen)
        {
            return Result.Failure<LoginOutcome>(DomainError.Validation(
                AuthErrorCodes.RegistrationsClosed, AuthErrorCodes.RegistrationsClosedMessage));
        }

        var subject = await grants.RedeemAsync(
            GrantToken.FromRaw(command.GrantToken!), GrantAssertion.Bearer(GrantPurpose.LoginComplete), cancellationToken);
        if (subject is not GrantSubject.LoginComplete { ProvenEmail: var email })
            return GrantUnusable();

        // Taken AFTER the redeem: a loser still holding a live grant could retry into the winner's account.
        // The loser is answered like a replayed grant, so nothing says an address is being registered.
        if (!await claim.TryClaimAsync(email, cancellationToken))
            return GrantUnusable();

        if (await subjects.ResolveAsync(email, cancellationToken) is LoginSubject.NoAccount)
        {
            var failure = await CreateAccountAsync(email, cancellationToken);
            if (failure is not null)
                return Result.Failure<LoginOutcome>(failure);
        }

        // Whatever the address now resolves to: the account just created, one registered meanwhile, one
        // pending deletion, or a row without a profile. The Identity write may have committed, so from here
        // on CancellationToken.None.
        return Result.Success(await outcome.ResolveAsync(
            new LoginChallengeProof(email), LoginMethod.Code, CancellationToken.None));
    }

    // Null when the address has an account afterwards: this one, or one that won a race the claim does not
    // cover. Uniqueness lives in Identity, so the duplicate is handled even by the caller that won the claim.
    private async Task<DomainError?> CreateAccountAsync(string email, CancellationToken ct)
    {
        var created = await accounts.CreatePasswordlessUserAsync(email, ct);
        if (created.IsFailure)
            return created.Error.Code == AuthErrorCodes.DuplicateAccount ? null : created.Error;

        var seeker = JobSeeker.Register(created.Value, TermsAcceptance.AcceptCurrent(clock), clock);
        if (seeker.IsFailure)
        {
            await accounts.DeleteAsync(created.Value, ct);
            return seeker.Error;
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

        // Saved here, not by the unit-of-work behavior after the handler: the outcome function reads the
        // profile back from the database, and an unsaved Add is invisible to it. A save that throws is
        // followed by nothing, so no session exists for an account whose profile did not commit (#1349); the
        // orphan sweep collects the Identity row.
        await db.SaveChangesAsync(ct);
        return null;
    }

    private static Result<LoginOutcome> GrantUnusable() => Result.Failure<LoginOutcome>(
        DomainError.Gone(AuthErrorCodes.LoginGrantUnusable, AuthErrorCodes.LoginGrantUnusableMessage));
}

using Jobbliggaren.Application.Auth.Dtos;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Mediator;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.Auth.Commands.Register;

public sealed class RegisterCommandHandler(
    IAppDbContext db,
    IUserAccountService userAccountService,
    ISessionStore sessionStore,
    IAuthAuditLogger auditLogger,
    IEmailSender emailSender,
    ICooldownGate cooldown,
    IOptions<AuthOptions> authOptions,
    IOptions<AuthEmailCooldownOptions> cooldownOptions,
    IDateTimeProvider clock)
    : ICommandHandler<RegisterCommand, Result<RegisterOutcome>>
{
    public async ValueTask<Result<RegisterOutcome>> Handle(
        RegisterCommand command, CancellationToken cancellationToken)
    {
        var requireConfirmation = authOptions.Value.RequireEmailConfirmation;

        var createResult = await userAccountService.CreateUserAsync(
            command.Email!, command.Password!, cancellationToken);

        if (createResult.IsFailure)
        {
            // #714: on the email-confirmation-first path a DUPLICATE address must NOT leak via a
            // distinct 400 — that IS the 200-vs-400 status oracle. Swallow it: touch nothing, email an
            // out-of-band account-exists notice to the taken address, and return the SAME 202 outcome
            // as a fresh signup (Session = null). A taken and a fresh address are then indistinguishable
            // on both status and body; the only differentiator is the mail, which reaches only an inbox
            // the requester controls (a fresh address, by definition). Every OTHER CreateUserAsync
            // failure (breached password #616, exotic invalid address) is credential/format-dependent
            // and existence-INDEPENDENT — it stays a genuine 400 and is identical for a taken and a
            // fresh address (Identity validates the password before uniqueness), so it is not an oracle
            // (CTO-bind Beslut 2 + Risk 1). Legacy flag-OFF keeps the 400 duplicate (the oracle is
            // acknowledged-deferred there and the feature is not enabled).
            if (requireConfirmation && createResult.Error.Code == AuthErrorCodes.DuplicateAccount)
            {
                // #703: per-target anti-email-bomb cooldown on the account-exists notice. A cooled address
                // silently SKIPS the send but returns the SAME uniform 202 — a visible throttle here would
                // itself be an enumeration channel (this is the UNAUTHENTICATED register surface), and the
                // notice is informational so suppression strands no one. Keyed per-target only (no
                // authenticated actor on this path). The Resend idempotency-key already dedupes per-address
                // within Resend's own window, but that is provider-specific; this is the provider-independent
                // throttle the #679 gate requires before Resend activation.
                if (await cooldown.TryBeginAsync(
                        CooldownScopes.AccountExists,
                        command.Email!,
                        TimeSpan.FromSeconds(cooldownOptions.Value.AccountExistsNoticeWindowSeconds),
                        cancellationToken))
                {
                    await emailSender.SendAccountExistsNoticeAsync(
                        command.Email!,
                        AccountExistsNoticeIdempotencyKey.For(command.Email!),
                        cancellationToken);
                }

                return Result.Success(new RegisterOutcome(Session: null));
            }

            return Result.Failure<RegisterOutcome>(createResult.Error);
        }

        var userId = createResult.Value;

        var seekerResult = JobSeeker.Register(userId, command.DisplayName, clock);
        if (seekerResult.IsFailure)
        {
            await userAccountService.DeleteUserAsync(userId, cancellationToken);
            return Result.Failure<RegisterOutcome>(seekerResult.Error);
        }

        db.JobSeekers.Add(seekerResult.Value);

        if (requireConfirmation)
        {
            // #714: email-confirmation-first — do NOT mint a session and do NOT emit a LoginSucceeded
            // audit (no login happened). Mint the opaque confirmation token and email the activation
            // link, then return a 202 outcome (Session = null). The send is the FINAL action: a
            // transport failure surfaces as a 500 and the not-yet-committed JobSeeker rolls back (the
            // Identity user is left for the #508 orphan-sweep's 1h grace), mirroring the legacy
            // session-mint's before-commit side-effect ordering below (CTO-bind D).
            var tokenResult = await userAccountService.GenerateEmailConfirmationTokenAsync(
                userId, cancellationToken);
            if (tokenResult.IsFailure)
                return Result.Failure<RegisterOutcome>(tokenResult.Error);

            var urlSafeToken = tokenResult.Value;
            await emailSender.SendEmailConfirmationAsync(
                command.Email!,
                new EmailConfirmationEmail(userId, urlSafeToken),
                EmailConfirmationIdempotencyKey.For(userId, urlSafeToken),
                cancellationToken);

            return Result.Success(new RegisterOutcome(Session: null));
        }

        // Legacy instant-login (flag OFF): mint a session and return it (Api renders 200 + sessionId).
        // Activation (#481 2b-3b): "Håll mig inloggad" checked → a rotating Persistent session;
        // unchecked/absent → a short session-scoped Session (the safe default). See LoginCommandHandler
        // for the full flip rationale.
        var lifetime = command.RememberMe ? SessionLifetime.Persistent : SessionLifetime.Session;
        var session = await sessionStore.CreateAsync(userId, lifetime, cancellationToken);

        auditLogger.LoginSucceeded(userId, session.Id.ToString());

        return Result.Success(new RegisterOutcome(new SessionDto(session.Id.Reveal())));
    }
}

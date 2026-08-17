using Jobbliggaren.Application.Auth.Dtos;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.Auth.Commands.Register;

public sealed partial class RegisterCommandHandler(
    IAppDbContext db,
    IUserAccountService userAccountService,
    ISessionStore sessionStore,
    IAuthAuditLogger auditLogger,
    IEmailSender emailSender,
    ICooldownGate cooldown,
    IOptions<AuthOptions> authOptions,
    IOptions<AuthEmailCooldownOptions> cooldownOptions,
    IDateTimeProvider clock,
    ILogger<RegisterCommandHandler> logger)
    : ICommandHandler<RegisterCommand, Result<RegisterOutcome>>
{
    public async ValueTask<Result<RegisterOutcome>> Handle(
        RegisterCommand command, CancellationToken cancellationToken)
    {
        // ADR 0083 Amendment 2026-08-03 — public-registration kill-switch. FIRST statement, before
        // CreateUserAsync: a refused registration must leave NOTHING behind, not an Identity user for
        // the #508 orphan-sweep to collect, not a JobSeeker, not an audit row. It also never reads
        // command.Email, so the response cannot vary with the submitted address — the anti-enumeration
        // property holds by construction here rather than by keeping two branches identical (#714).
        // Default is CLOSED (fail-safe default): absent config must not open the gate.
        if (!authOptions.Value.RegistrationsOpen)
        {
            return Result.Failure<RegisterOutcome>(DomainError.Validation(
                AuthErrorCodes.RegistrationsClosed, AuthErrorCodes.RegistrationsClosedMessage));
        }

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
                // authenticated actor on this path). This cooldown is the WHOLE anti-email-bomb control,
                // full stop (ADR 0103, ADR 0124). It used to be described as the provider-independent
                // half of a pair whose other half was a provider idempotency-key — that key is gone
                // with Resend, and it was never armed anyway (Email:Provider has never been set in any
                // committed config), so nothing here got weaker. No provider since has had an
                // equivalent to inherit — neither SES v2 nor Scaleway offers one (#183).
                if (await cooldown.TryBeginAsync(
                        CooldownScopes.AccountExists,
                        command.Email!,
                        TimeSpan.FromSeconds(cooldownOptions.Value.AccountExistsNoticeWindowSeconds),
                        cancellationToken))
                {
                    // #1349 — swallowed symmetrically with the confirmation send below, and the
                    // symmetry is load-bearing: one-armed, a transport outage answers 202 for a fresh
                    // address and 500 for a taken one — the status oracle #714 exists to close.
                    try
                    {
                        await emailSender.SendAccountExistsNoticeAsync(
                            command.Email!,
                            cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        LogAccountExistsNoticeSendFailed(logger, ex);
                    }
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
            // link, then return a 202 outcome (Session = null).
            //
            // #1349 — the send is GUARDED, reversing CTO-bind D (senior-cto-advisor 2026-08-17).
            // Unguarded it rolled the not-yet-committed JobSeeker back on a transport fault and left an
            // orphaned Identity user; the bind's written ground was parity with the legacy session-mint
            // below, which AuthOptionsValidator makes unreachable outside Dev/Test. No cross-context
            // transaction is introduced, so the separate prohibition at AccountHardDeleter.cs:74-78 is
            // untouched. Parity with ResendEmailConfirmationCommandHandler, which swallows identically.
            var tokenResult = await userAccountService.GenerateEmailConfirmationTokenAsync(
                userId, cancellationToken);
            if (tokenResult.IsFailure)
                return Result.Failure<RegisterOutcome>(tokenResult.Error);

            var urlSafeToken = tokenResult.Value;
            try
            {
                await emailSender.SendEmailConfirmationAsync(
                    command.Email!,
                    new EmailConfirmationEmail(userId, urlSafeToken),
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogConfirmationSendFailed(logger, ex);
            }

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

    // Logging the exception OBJECT is safe here by the adapter's own contract: IEmailSender
    // implementations wrap every provider fault in EmailDeliveryException, which carries the email kind
    // and the underlying TYPE NAME only and deliberately holds no InnerException, precisely so that
    // exception formatting cannot walk back to a message naming the recipient (ADR 0124). Parity with
    // ResendEmailConfirmationCommandHandler.LogResendFailed.
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "RegisterCommand: confirmation send failed — the account stands, the activation link "
            + "was not delivered; the user can resend it (#1349)")]
    private static partial void LogConfirmationSendFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "RegisterCommand: account-exists notice send failed — the uniform 202 is returned "
            + "regardless, so the duplicate branch stays indistinguishable from a fresh one (#1349)")]
    private static partial void LogAccountExistsNoticeSendFailed(ILogger logger, Exception ex);
}

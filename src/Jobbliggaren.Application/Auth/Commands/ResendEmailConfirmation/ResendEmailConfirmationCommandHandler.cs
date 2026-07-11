using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.Auth.Commands.ResendEmailConfirmation;

public sealed partial class ResendEmailConfirmationCommandHandler(
    ICooldownGate cooldown,
    IOptions<ResendCooldownOptions> cooldownOptions,
    IUserAccountService userAccountService,
    IEmailSender emailSender,
    IAuthAuditLogger auditLogger,
    ILogger<ResendEmailConfirmationCommandHandler> logger)
    : ICommandHandler<ResendEmailConfirmationCommand, Result>
{
    private readonly TimeSpan _window = TimeSpan.FromSeconds(cooldownOptions.Value.WindowSeconds);

    public async ValueTask<Result> Handle(
        ResendEmailConfirmationCommand command, CancellationToken cancellationToken)
    {
        // Cooldown is check-and-set UNIFORMLY for every non-cooled request, existence-independently
        // (CTO-bind FORK 1): a within-window repeat is the SAME uniform success (silent no-op), never a
        // 429 — mirroring the register swallow so a resend reveals nothing about the target. Runs BEFORE
        // any eligibility work so cooldown state never correlates with account existence. Generalised gate
        // (#703): per-target scope, window from the #733-owned ResendCooldownOptions (unchanged behaviour).
        if (!await cooldown.TryBeginAsync(CooldownScopes.ResendConfirm, command.Email!, _window, cancellationToken))
            return Result.Success();

        try
        {
            // Eligibility + token mint are sealed in Infrastructure and FLAG-GATED: flag-OFF (the prod-safe
            // default) => null => uniform no-op, so the endpoint never mails a user whose instant-login
            // works (preserves #714's prod-safe-OFF guarantee). null is also returned for a non-existent OR
            // already-confirmed address — all indistinguishable to the handler.
            var delivery = await userAccountService.TryPrepareEmailConfirmationResendAsync(
                command.Email!, cancellationToken);
            if (delivery is null)
                return Result.Success();

            // Inline mint+send in the SAME Api process as /verify-email's validation (one Data-Protection
            // keyring) so the resent link actually resolves — mirrors RegisterCommandHandler. The out-of-
            // band Hangfire path was reverted (CTO 2026-07-10, ADR 0102): it needed a cross-process shared
            // DP keyring whose blast radius exceeded the non-exploitable FORK-2 timing oracle it closed
            // (the 60s cooldown rate-caps that oracle to one measurement/address, and it is inert flag-OFF).
            await emailSender.SendEmailConfirmationAsync(
                delivery.Email,
                new EmailConfirmationEmail(delivery.UserId, delivery.UrlSafeToken),
                EmailConfirmationIdempotencyKey.For(delivery.UserId, delivery.UrlSafeToken),
                cancellationToken);

            // Audit ONLY after a link was actually sent (a truthful "resent" event; CTO-bind ii).
            auditLogger.EmailConfirmationResent(delivery.UserId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Uniform 202 regardless of mint/send outcome (anti-enumeration): a transport fault for an
            // eligible account must NOT surface as a differential 500 that a non-existent address (a clean
            // 202) does not — that would re-open the existence oracle. Log server-side (no PII beyond the
            // exception); the user can retry after the cooldown. No audit-log line is written (nothing was sent).
            LogResendFailed(logger, ex);
        }

        return Result.Success();
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "ResendEmailConfirmationCommand: mint/send failed — uniform 202 returned, no email sent")]
    private static partial void LogResendFailed(ILogger logger, Exception ex);
}

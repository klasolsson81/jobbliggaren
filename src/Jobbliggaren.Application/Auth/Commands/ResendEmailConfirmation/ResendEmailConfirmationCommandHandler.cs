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
            // band Hangfire path was reverted (CTO 2026-07-10): it needed a cross-process shared DP keyring
            // whose blast radius exceeded the FORK-2 timing oracle it closed. ⚠ Two corrections to that
            // reasoning, measured 2026-08-10. First, "the 60s cooldown rate-caps that oracle to one
            // measurement/address" is FALSE (security-auditor): a per-address window caps repeated
            // sampling of one address, and enumeration needs one measurement per candidate. The oracle is
            // capped only by AuthWrite, per-IP and parallelisable — it is inert flag-OFF, which is what
            // still makes it non-exploitable in the default configuration, and nothing else does.
            // Second, the revert is cited here and in five other files as "ADR 0102"; no such document
            // exists (docs/decisions jumps 0101 → 0103, and 0103 is used twice). The decision is real and
            // is recorded in these comments; the ADR pointer is not. #1171 solved the same problem on the
            // reset route by moving the send off the request path — this route has not been reworked.
            await emailSender.SendEmailConfirmationAsync(
                delivery.Email,
                new EmailConfirmationEmail(delivery.UserId, delivery.UrlSafeToken),
                cancellationToken);

            // Audit ONLY after a link was actually sent (a truthful "resent" event; CTO-bind ii).
            //
            // #1087 — "actually sent" needs the capability, not just the absence of an exception. A
            // non-delivering sender returns Task.CompletedTask, so the line below used to stamp
            // User.EmailConfirmationResent for a link that reached nobody, and the comment above
            // asserted the very truthfulness it lost. ChangeEmailCommandHandler discharges the same
            // AC ("no audit row for a request that cannot complete") via AuditBehavior, which stamps
            // only on Result.Success — that reasoning does NOT reach here, because this handler
            // returns Success by contract and stamps manually.
            //
            // The RESPONSE is deliberately unchanged: still the uniform Result.Success / 202 for every
            // address, cooled or not, existing or not (FORK 1, ADR 0102). Refusing visibly here would
            // split 503 from 202 on an UNAUTHENTICATED surface after an existence-dependent lookup
            // (TryPrepareEmailConfirmationResendAsync returns null for absent AND already-confirmed),
            // which would be a live enumeration oracle. Only the audit record changes, and it changes
            // from false to absent.
            if (emailSender.CanDeliver)
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

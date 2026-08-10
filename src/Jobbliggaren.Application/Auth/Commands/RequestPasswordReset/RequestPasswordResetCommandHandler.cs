using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.Auth.Commands.RequestPasswordReset;

public sealed partial class RequestPasswordResetCommandHandler(
    IEmailSender emailSender,
    ICooldownGate cooldown,
    IOptions<AuthEmailCooldownOptions> cooldownOptions,
    IUserAccountService userAccountService,
    IAuthAuditLogger auditLogger,
    ILogger<RequestPasswordResetCommandHandler> logger)
    : ICommandHandler<RequestPasswordResetCommand, Result>
{
    private readonly TimeSpan _window =
        TimeSpan.FromSeconds(cooldownOptions.Value.PasswordResetWindowSeconds);

    public async ValueTask<Result> Handle(
        RequestPasswordResetCommand command, CancellationToken cancellationToken)
    {
        // ── The order of the three steps below IS the contract. Do not reorder them. ──

        // 1. CAPABILITY, and it MUST be the first statement.
        //
        // #1171 is delivery-dependent in the strictest sense on this port: the password changes only
        // when the emailed link is opened, so a dropped send leaves someone who has already lost access
        // with a "check your inbox" message and no link. NullEmailSender is the live default outside
        // Development/Test, so this is the ordinary configuration, not an edge case.
        //
        // Why FIRST rather than merely present. This surface is unauthenticated and answers a uniform
        // 202 for known and unknown addresses alike. Checked here the gate reads NO input, so the
        // 503/202 partition is a property of the server's configuration and can carry no information
        // about any account. Checked after the lookup it would be reachable only when an account exists,
        // and the 503 would become precisely the existence oracle the uniform 202 exists to prevent —
        // which is why ResendEmailConfirmationCommandHandler, whose check sits after its lookup, must
        // never return 503 at all and settles for suppressing its audit line. Same property, and the
        // same wording, as AuthErrorCodes.RegistrationsClosed: the gate never reads the submitted
        // address, so the response cannot vary with it.
        if (!emailSender.CanDeliver)
            return Result.Failure(DomainError.Validation(
                AuthErrorCodes.EmailDeliveryUnavailable,
                AuthErrorCodes.EmailDeliveryUnavailableMessage));

        // 2. COOLDOWN — check-and-set uniformly for every non-cooled well-formed request, before any
        // eligibility work, so cooldown state in Redis never correlates with account existence. A cooled
        // repeat returns the SAME uniform success, never a 409 or 429: a visible throttle on an
        // unauthenticated surface would answer differently for an address someone had recently requested,
        // which is an enumeration oracle assembled out of the anti-abuse control (CooldownScopes.PasswordReset).
        //
        // It sits AFTER the capability check because a static server refusal must not burn the
        // requester's window for a request the server could never have fulfilled — the same relative
        // order, for the same reason, as ChangeEmailCommandHandler's.
        if (!await cooldown.TryBeginAsync(
                CooldownScopes.PasswordReset, command.Email!, _window, cancellationToken))
            return Result.Success();

        try
        {
            // 3. Eligibility + token mint, sealed in Infrastructure. null for a non-existent address,
            // and indistinguishable here from any other ineligible case. Minted in the SAME Api process
            // that validates it at /reset-password (one Data-Protection keyring, ADR 0102 — the Worker
            // registers no token providers).
            var delivery = await userAccountService.TryPreparePasswordResetAsync(
                command.Email!, cancellationToken);
            if (delivery is null)
                return Result.Success();

            await emailSender.SendPasswordResetAsync(
                delivery.Email,
                new PasswordResetEmail(delivery.UserId, delivery.UrlSafeToken),
                cancellationToken);

            // Audit only on the branch where a link actually went out, so the log carries no
            // account-existence signal. No CanDeliver re-check is needed here (unlike the resend
            // handler's): step 1 already refused, so reaching this line implies capability.
            auditLogger.PasswordResetRequested(delivery.UserId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Uniform 202 regardless of mint/send outcome: a transport fault for an existing account must
            // not surface as a differential 500 that an unknown address (a clean 202) never produces —
            // that would re-open the existence oracle from the other side. Logged server-side without the
            // address or the token; the user can retry after the cooldown. No audit line (nothing was sent).
            LogPasswordResetSendFailed(logger, ex);
        }

        return Result.Success();
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "RequestPasswordResetCommand: mint/send failed — uniform 202 returned, no email sent")]
    private static partial void LogPasswordResetSendFailed(ILogger logger, Exception ex);
}

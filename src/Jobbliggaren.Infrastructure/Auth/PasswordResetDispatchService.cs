using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Common.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Infrastructure.Auth;

/// <summary>
/// #1171 — the consumer for <see cref="PasswordResetDispatchChannel"/>. Does the account lookup, the
/// token mint and the send, all OFF the request path and all inside the Api process.
/// <para>
/// <b>In the Api process, not the Worker, and that is a constraint rather than a preference.</b>
/// <c>DataProtectorTokenProvider</c> needs <c>IDataProtectionProvider</c>, and
/// <c>AddCoreIdentityForWorker</c> deliberately registers no token providers because a shared
/// cross-process Data-Protection keyring was rejected (CTO 2026-07-10, recorded on
/// <c>ResendEmailConfirmationCommandHandler</c>). Minting here keeps that decision intact and keeps the
/// token in memory for its whole life.
/// </para>
/// <para>
/// Everything existence-dependent lives here: whether the address resolves, whether a token is minted,
/// and whether a provider round trip happens. The request path sees none of it, which is what makes
/// its response time independent of account existence.
/// </para>
/// </summary>
internal sealed partial class PasswordResetDispatchService(
    PasswordResetDispatchChannel queue,
    IServiceScopeFactory scopeFactory,
    ILogger<PasswordResetDispatchService> logger)
    : BoundedDispatchService<PasswordResetDispatch>(queue, scopeFactory)
{
    protected override async Task HandleAsync(
        PasswordResetDispatch dispatch, IServiceProvider services, CancellationToken ct)
    {
        var accounts = services.GetRequiredService<IUserAccountService>();
        var emailSender = services.GetRequiredService<IEmailSender>();
        var audit = services.GetRequiredService<IAuthAuditLogger>();

        // null for a non-existent address, and indistinguishable here from any other ineligible
        // case. Nothing downstream of this point can reach the caller, so there is no uniformity to
        // preserve any more — that guarantee now lives entirely in the request path.
        var delivery = await accounts.TryPreparePasswordResetAsync(dispatch.Email, ct);
        if (delivery is null)
            return;

        await emailSender.SendPasswordResetAsync(
            delivery.Email,
            new PasswordResetEmail(delivery.UserId, delivery.UrlSafeToken),
            ct);

        // The audit line's IP and User-Agent are CARRIED from the request path rather than read
        // here: AuthAuditLogger reads IHttpContextAccessor, which is null in a background scope, so
        // reading them here would silently degrade both fields to "unknown" on the one auth event
        // most closely tied to account takeover (ADR 0024 D7 ratified them as defence in depth).
        // Both values were already anonymised/truncated before they entered the queue.
        audit.PasswordResetRequested(delivery.UserId, dispatch.IpAddress, dispatch.UserAgent);
    }

    protected override void OnDispatchFailed(string errorType) => LogDispatchFailed(logger, errorType);

    [LoggerMessage(1008, LogLevel.Warning,
        "Password-reset dispatch failed ({ErrorType}) — no email sent; the caller already received the "
        + "uniform 202 and cannot be told")]
    private static partial void LogDispatchFailed(ILogger logger, string errorType);
}

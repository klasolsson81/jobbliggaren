using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Common.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // CancellationToken.None, deliberately, and NOT the stopping token — the drain depends on it.
        //
        // The order is: our StopAsync completes the writer, THEN base.StopAsync cancels its own token
        // source, and only then does it await this task. So the cancellation lands while this loop is
        // still draining, not before it starts. Passing that token down would abort the drain on the
        // first awaited send: ScalewayEmailSender passes the token to HttpClient, and both catch
        // filters here and there let a caller-requested cancellation through, so the OCE would unwind
        // straight out of this loop and take the rest of the queue with it.
        //
        // Worse, it would fail ONLY in the configuration that matters. NullEmailSender and
        // ConsoleEmailSender ignore the token, so a drain looks healthy in Development and in
        // Testcontainers while Provider=Scaleway drops everything queued (dotnet-architect 2026-08-10).
        //
        // What ends this loop is the writer being completed, which is exactly what StopAsync does
        // first. What BOUNDS it is base.StopAsync's own await on the host's shutdown token.
        await foreach (var dispatch in queue.Reader.ReadAllAsync(CancellationToken.None))
        {
            await DispatchOneAsync(dispatch, CancellationToken.None);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Complete the writer FIRST so the loop above sees the end of the stream and drains. That is
        // the ONLY thing that ends the loop — see ExecuteAsync for why it must not observe the stopping
        // token. The bound is base.StopAsync's own await below, which honours HostOptions.ShutdownTimeout.
        // That timeout is NOT unset: the framework carries a default, and this app does not pin it. The
        // number is left out on purpose — it belongs to the runtime, so writing it here would put a value
        // a version bump can change into a comment nothing re-measures.
        queue.Complete();
        await base.StopAsync(cancellationToken);
    }

    private async Task DispatchOneAsync(PasswordResetDispatch dispatch, CancellationToken ct)
    {
        // A scope per item: IUserAccountService and IEmailSender are scoped, and a BackgroundService is
        // a singleton. One scope for the whole loop would leak a DbContext across unrelated requests.
        await using var scope = scopeFactory.CreateAsyncScope();

        try
        {
            // Resolution lives INSIDE the try (security-auditor 2026-08-10). A failure to resolve is a
            // configuration fault, and outside the try it would fault ExecuteAsync itself - killing the
            // consumer for the lifetime of the process and dropping every later request silently,
            // rather than logging one item's failure and continuing.
            var accounts = scope.ServiceProvider.GetRequiredService<IUserAccountService>();
            var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
            var audit = scope.ServiceProvider.GetRequiredService<IAuthAuditLogger>();

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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Ordinary failure containment, NOT anti-enumeration — the caller was answered long ago and
            // there is no response left to hold uniform. Saying otherwise would be the false-comment
            // class this PR keeps correcting.
            //
            // Type name only, never the exception object: the send leg is contained
            // (EmailDeliveryException carries a kind and a type name and deliberately no
            // InnerException), but TryPreparePasswordResetAsync's database and Data-Protection
            // exceptions are not, and can carry the address or connection detail in their message.
            LogDispatchFailed(logger, ex.GetType().Name);
        }
    }

    [LoggerMessage(1008, LogLevel.Warning,
        "Password-reset dispatch failed ({ErrorType}) — no email sent; the caller already received the "
        + "uniform 202 and cannot be told")]
    private static partial void LogDispatchFailed(ILogger logger, string errorType);
}

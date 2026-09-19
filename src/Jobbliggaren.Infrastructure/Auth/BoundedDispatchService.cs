using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jobbliggaren.Infrastructure.Auth;

/// <summary>
/// The drain of a <see cref="BoundedDispatchChannel{T}"/>: one scope per item, failures contained per item,
/// and a shutdown that delivers what is already queued (#1171; ADR 0142 D2). Shared rather than copied
/// because the drain's defect class — a cancelled send that unwinds the whole loop — failed only under the
/// real mail provider, and a copy would not be covered by the test that pins it.
/// </summary>
internal abstract class BoundedDispatchService<T>(
    BoundedDispatchChannel<T> queue,
    IServiceScopeFactory scopeFactory)
    : BackgroundService
{
    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
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
        await foreach (var item in queue.Reader.ReadAllAsync(CancellationToken.None))
        {
            await DispatchOneAsync(item);
        }
    }

    public sealed override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Complete the writer FIRST so the loop above sees the end of the stream and drains. That is
        // the ONLY thing that ends the loop — see ExecuteAsync for why it must not observe the stopping
        // token. The bound is base.StopAsync's own await below, which honours HostOptions.ShutdownTimeout.
        queue.Complete();
        await base.StopAsync(cancellationToken);
    }

    private async Task DispatchOneAsync(T item)
    {
        // A scope per item: the handlers' services are scoped, and a BackgroundService is a singleton.
        // One scope for the whole loop would leak a DbContext across unrelated requests.
        await using var scope = scopeFactory.CreateAsyncScope();

        try
        {
            // Resolution happens inside HandleAsync, so INSIDE this try (security-auditor 2026-08-10). A
            // failure to resolve is a configuration fault, and outside the try it would fault
            // ExecuteAsync itself - killing the consumer for the lifetime of the process and dropping
            // every later item silently, rather than logging one item's failure and continuing.
            await HandleAsync(item, scope.ServiceProvider, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Ordinary failure containment, NOT anti-enumeration — the caller was answered long ago.
            // Type name only, never the exception object: database and Data-Protection exceptions can
            // carry the address or connection detail in their message.
            OnDispatchFailed(ex.GetType().Name);
        }
    }

    /// <summary>One item's work. Resolve every scoped service from <paramref name="services"/>.</summary>
    protected abstract Task HandleAsync(T item, IServiceProvider services, CancellationToken ct);

    /// <summary>The per-item failure line: the exception's type name and nothing else.</summary>
    protected abstract void OnDispatchFailed(string errorType);
}

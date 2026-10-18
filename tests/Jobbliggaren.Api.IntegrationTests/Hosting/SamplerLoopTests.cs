using Jobbliggaren.Api.Hosting;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Hosting;

/// <summary>
/// The loop every sampler hosted in the API runs on (ADR 0159 D2, ADR 0174). Its one job beyond calling the tick is
/// that nothing a tick or its failure report throws may leave it, except the shutdown token's cancellation. Each
/// hosted service's own tests prove it uses this loop for its own sampler; these prove the loop.
/// </summary>
public sealed class SamplerLoopTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Guard_ASampleThatThrows_IsReportedAndDoesNotEscape()
    {
        Exception? reported = null;

        Should.NotThrow(() => SamplerLoop.Guard(
            () => throw new InvalidOperationException("boom"), exception => reported = exception, Ct));

        reported.ShouldBeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task GuardAsync_ASampleThatThrowsSynchronously_IsReportedAndDoesNotEscape()
    {
        Exception? reported = null;

        await Should.NotThrowAsync(() => SamplerLoop.GuardAsync(
            _ => throw new InvalidOperationException("boom"), exception => reported = exception, Ct));

        reported.ShouldBeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task GuardAsync_AFaultedSample_IsReportedAndDoesNotEscape()
    {
        Exception? reported = null;

        await Should.NotThrowAsync(() => SamplerLoop.GuardAsync(
            _ => Task.FromException(new InvalidOperationException("boom")), exception => reported = exception, Ct));

        reported.ShouldBeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task GuardAsync_AReportThatAlsoThrows_StillDoesNotEscape()
    {
        // The realistic way to reach the last handler is a failing log sink, which throws there too.
        await Should.NotThrowAsync(() => SamplerLoop.GuardAsync(
            _ => Task.FromException(new InvalidOperationException("boom")),
            _ => throw new IOException("the log sink is down"),
            Ct));
    }

    [Fact]
    public void Guard_AReportThatAlsoThrows_StillDoesNotEscape() =>
        Should.NotThrow(() => SamplerLoop.Guard(
            () => throw new InvalidOperationException("boom"), _ => throw new IOException("the log sink is down"), Ct));

    [Fact]
    public async Task GuardAsync_ACancellationDuringShutdown_IsPassedOnAndNotReported()
    {
        using var shutdown = new CancellationTokenSource();
        var reported = false;

        await Should.ThrowAsync<OperationCanceledException>(() => SamplerLoop.GuardAsync(
            _ =>
            {
                shutdown.Cancel();
                return Task.FromException(new OperationCanceledException(shutdown.Token));
            },
            _ => reported = true,
            shutdown.Token));

        reported.ShouldBeFalse();
    }

    [Fact]
    public async Task GuardAsync_AStrayCancellationThatIsNotShutdown_IsReportedAndDoesNotEscape()
    {
        // Without the filter the loop would read it as a graceful shutdown and the sampler would be dead for the rest
        // of the process with nothing logged.
        Exception? reported = null;

        await Should.NotThrowAsync(() => SamplerLoop.GuardAsync(
            _ => Task.FromException(new OperationCanceledException()), exception => reported = exception,
            CancellationToken.None));

        reported.ShouldBeOfType<OperationCanceledException>();
    }

    [Fact]
    public async Task RunAsync_TheFirstTickIsNotDelayedByTheInterval()
    {
        // An interval far longer than the test's own wait, so only the immediate first tick can satisfy it.
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var ticks = 0;

        var loop = SamplerLoop.RunAsync(TimeSpan.FromHours(1), _ =>
        {
            Interlocked.Increment(ref ticks);
            return Task.CompletedTask;
        }, stop.Token);

        await Eventually(() => Volatile.Read(ref ticks) == 1, "the first tick waited for the timer");
        await stop.CancelAsync();
        await loop;
    }

    [Fact]
    public async Task RunAsync_ThenTicksOnEveryInterval_AndEndsWithoutAFaultOnShutdown()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var ticks = 0;

        var loop = SamplerLoop.RunAsync(TimeSpan.FromSeconds(1), _ =>
        {
            Interlocked.Increment(ref ticks);
            return Task.CompletedTask;
        }, stop.Token);

        await Eventually(() => Volatile.Read(ref ticks) >= 2, "the timer ticks after the first one");
        await stop.CancelAsync();
        await Should.NotThrowAsync(() => loop);
        loop.IsFaulted.ShouldBeFalse("a faulted ExecuteAsync stops the whole API host");
    }

    private static async Task Eventually(Func<bool> condition, string because)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException(because);
            }

            await Task.Delay(50, Ct);
        }
    }
}

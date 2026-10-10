using Jobbliggaren.Api.Hosting;
using Jobbliggaren.Application.Admin.HostObservations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Admin;

/// <summary>
/// The hosted service's one job beyond calling the sampler (#1982): it must never be able to stop the API.
/// <c>BackgroundServiceExceptionBehavior</c> defaults to <c>StopHost</c>, so these tests hand the loop a sampler
/// and a log sink that throw. They need no web host: the service is built by hand with a one-second interval
/// (the options are not validated here; the shipped range starts at five seconds).
/// </summary>
public sealed class HostObservationServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HostObservationService Service(
        IHostObservationSampler sampler, ILogger<HostObservationService>? logger = null) =>
        new(sampler, Options.Create(new HostObservationOptions { SampleIntervalSeconds = 1 }),
            logger ?? Substitute.For<ILogger<HostObservationService>>());

    [Fact]
    public async Task Start_SamplesAtOnceAndThenOnEveryInterval()
    {
        var sampler = Substitute.For<IHostObservationSampler>();
        using var service = Service(sampler);

        await service.StartAsync(Ct);
        await Eventually(() => sampler.ReceivedCalls().Any(), "the first sample is not delayed by an interval");
        var afterFirst = sampler.ReceivedCalls().Count();
        await Eventually(() => sampler.ReceivedCalls().Count() > afterFirst, "and the timer then ticks");
        await service.StopAsync(Ct);
    }

    [Fact]
    public async Task ASamplerThatThrowsOnEveryTick_NeverFaultsTheService()
    {
        var sampler = Substitute.For<IHostObservationSampler>();
        sampler.When(s => s.Sample()).Do(_ => throw new InvalidOperationException("boom"));
        using var service = Service(sampler);

        await service.StartAsync(Ct);
        await Eventually(() => sampler.ReceivedCalls().Count() >= 3, "it keeps ticking through the failures");

        service.ExecuteTask.ShouldNotBeNull().IsFaulted.ShouldBeFalse("a faulted ExecuteAsync stops the whole API host");
        service.ExecuteTask.IsCompleted.ShouldBeFalse();
        await service.StopAsync(Ct);
        service.ExecuteTask.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public void Tick_AFailureAndALogSinkThatAlsoThrows_StillDoesNotEscape()
    {
        // The realistic way to reach the last handler is a failing log sink, which throws there too.
        var sampler = Substitute.For<IHostObservationSampler>();
        sampler.When(s => s.Sample()).Do(_ => throw new InvalidOperationException("boom"));
        using var service = Service(sampler, new ThrowingLogger());

        Should.NotThrow(() => service.Tick(Ct));
    }

    [Fact]
    public void Tick_ALoggedFailure_IsOneWarningWithoutThePayload()
    {
        var sampler = Substitute.For<IHostObservationSampler>();
        sampler.When(s => s.Sample()).Do(_ => throw new InvalidOperationException("boom"));
        var logger = new Jobbliggaren.TestSupport.RecordingLogger<HostObservationService>();
        using var service = Service(sampler, logger);

        service.Tick(Ct);

        var line = logger.Records.ShouldHaveSingleItem();
        (line.Level, line.EventId.Id).ShouldBe((LogLevel.Warning, 6224));
        line.Message.ShouldNotContain("boom");
    }

    [Fact]
    public void Tick_ACancellationDuringShutdown_IsPassedOnSoTheLoopEnds()
    {
        using var shutdown = new CancellationTokenSource();
        var sampler = Substitute.For<IHostObservationSampler>();
        sampler.When(s => s.Sample()).Do(_ =>
        {
            shutdown.Cancel();
            throw new OperationCanceledException(shutdown.Token);
        });
        using var service = Service(sampler);

        Should.Throw<OperationCanceledException>(() => service.Tick(shutdown.Token));
    }

    [Fact]
    public void Tick_AStrayCancellationThatIsNotShutdown_IsAFailureAndDoesNotEndTheLoop()
    {
        // Without the filter the outer catch would read it as a graceful shutdown and the sampler would be
        // dead for the rest of the process with nothing logged.
        var sampler = Substitute.For<IHostObservationSampler>();
        sampler.When(s => s.Sample()).Do(_ => throw new OperationCanceledException());
        var logger = new Jobbliggaren.TestSupport.RecordingLogger<HostObservationService>();
        using var service = Service(sampler, logger);

        Should.NotThrow(() => service.Tick(CancellationToken.None));

        logger.Records.ShouldHaveSingleItem().EventId.Id.ShouldBe(6224);
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

    private sealed class ThrowingLogger : ILogger<HostObservationService>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => throw new IOException("the log sink is down");
    }
}

using System.ComponentModel.DataAnnotations;
using Jobbliggaren.Api.Hosting;
using Jobbliggaren.Application.Admin.ServiceObservations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Admin;

/// <summary>
/// The hosted service's one job beyond calling the sampler (#1982): it must never be able to stop the API.
/// <c>BackgroundServiceExceptionBehavior</c> defaults to <c>StopHost</c>, so these tests hand the loop a sampler and a
/// log sink that throw. They need no web host: the service is built by hand with a one-second interval (the options
/// are not validated here; the shipped range starts at five seconds).
/// </summary>
public sealed class ServiceObservationServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ServiceObservationService Service(
        IServiceObservationSampler sampler, ILogger<ServiceObservationService>? logger = null,
        ServiceObservationOptions? options = null) =>
        new(sampler, Options.Create(options ?? new ServiceObservationOptions { SampleIntervalSeconds = 1 }),
            logger ?? Substitute.For<ILogger<ServiceObservationService>>());

    [Fact]
    public async Task Start_TheFirstSampleIsNotDelayedByAnInterval()
    {
        // An interval no test deadline can wait out, in the range the shipped validation accepts: only the
        // immediate first tick can satisfy this.
        var sampler = Substitute.For<IServiceObservationSampler>();
        var options = new ServiceObservationOptions { SampleIntervalSeconds = 300, StaleAfterSeconds = 3600 };
        Validator.TryValidateObject(options, new ValidationContext(options), [], validateAllProperties: true)
            .ShouldBeTrue("the pair is one the shipped validation accepts");
        using var service = Service(sampler, options: options);

        await service.StartAsync(Ct);
        await Eventually(() => sampler.ReceivedCalls().Any(), "the first sample waited for the timer");
        await service.StopAsync(Ct);
    }

    [Fact]
    public async Task Start_ThenSamplesOnEveryInterval()
    {
        var sampler = Substitute.For<IServiceObservationSampler>();
        using var service = Service(sampler);

        await service.StartAsync(Ct);
        await Eventually(() => sampler.ReceivedCalls().Any(), "the first sample is not delayed by an interval");
        var afterFirst = sampler.ReceivedCalls().Count();
        await Eventually(() => sampler.ReceivedCalls().Count() > afterFirst, "and the timer then ticks");
        await service.StopAsync(Ct);
    }

    [Fact]
    public async Task Service_ASamplerThatThrowsOnEveryTick_NeverFaultsTheService()
    {
        var sampler = Substitute.For<IServiceObservationSampler>();
        sampler.SampleAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromException(new InvalidOperationException("boom")));
        using var service = Service(sampler);

        await service.StartAsync(Ct);
        await Eventually(() => sampler.ReceivedCalls().Count() >= 3, "it keeps ticking through the failures");

        service.ExecuteTask.ShouldNotBeNull().IsFaulted.ShouldBeFalse("a faulted ExecuteAsync stops the whole API host");
        service.ExecuteTask.IsCompleted.ShouldBeFalse();
        await service.StopAsync(Ct);
        service.ExecuteTask.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Tick_ASamplerThatThrowsSynchronously_IsAlsoContained()
    {
        var sampler = Substitute.For<IServiceObservationSampler>();
        sampler.SampleAsync(Arg.Any<CancellationToken>()).Returns<Task>(_ => throw new InvalidOperationException("boom"));
        using var service = Service(sampler);

        await Should.NotThrowAsync(async () => await service.TickAsync(Ct));
    }

    [Fact]
    public async Task Tick_AFailureAndALogSinkThatAlsoThrows_StillDoesNotEscape()
    {
        // The realistic way to reach the last handler is a failing log sink, which throws there too.
        var sampler = Substitute.For<IServiceObservationSampler>();
        sampler.SampleAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromException(new InvalidOperationException("boom")));
        using var service = Service(sampler, new ThrowingLogger());

        await Should.NotThrowAsync(async () => await service.TickAsync(Ct));
    }

    [Fact]
    public async Task Tick_ALoggedFailure_IsOneWarningCarryingTheTypeNameAndNotTheMessage()
    {
        var sampler = Substitute.For<IServiceObservationSampler>();
        sampler.SampleAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("Host=db;Password=hunter2")));
        var logger = new Jobbliggaren.TestSupport.RecordingLogger<ServiceObservationService>();
        using var service = Service(sampler, logger);

        await service.TickAsync(Ct);

        var line = logger.Records.ShouldHaveSingleItem();
        (line.Level, line.EventId.Id).ShouldBe((LogLevel.Warning, 6232));
        line.Message.ShouldContain("InvalidOperationException");
        line.Message.ShouldNotContain("hunter2");
    }

    [Fact]
    public async Task Tick_ACancellationDuringShutdown_IsPassedOnSoTheLoopEnds()
    {
        using var shutdown = new CancellationTokenSource();
        var sampler = Substitute.For<IServiceObservationSampler>();
        sampler.SampleAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            shutdown.Cancel();
            return Task.FromException(new OperationCanceledException(shutdown.Token));
        });
        using var service = Service(sampler);

        await Should.ThrowAsync<OperationCanceledException>(async () => await service.TickAsync(shutdown.Token));
    }

    [Fact]
    public async Task Tick_AStrayCancellationThatIsNotShutdown_IsAFailureAndDoesNotEndTheLoop()
    {
        // Without the filter the outer catch would read it as a graceful shutdown and the sampler would be dead for
        // the rest of the process with nothing logged.
        var sampler = Substitute.For<IServiceObservationSampler>();
        sampler.SampleAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromException(new OperationCanceledException()));
        var logger = new Jobbliggaren.TestSupport.RecordingLogger<ServiceObservationService>();
        using var service = Service(sampler, logger);

        await Should.NotThrowAsync(async () => await service.TickAsync(CancellationToken.None));

        logger.Records.ShouldHaveSingleItem().EventId.Id.ShouldBe(6232);
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

    private sealed class ThrowingLogger : ILogger<ServiceObservationService>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => throw new IOException("the log sink is down");
    }
}

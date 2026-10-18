using Jobbliggaren.Api.Hosting;
using Jobbliggaren.Application.JobAds.Listing;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Hosting;

/// <summary>
/// The listed-ad snapshot's hosted service (ADR 0174) on the shared loop: its first read does not wait for the
/// five-minute interval, a failing read cannot stop the API, and its log line names the failure's type and nothing it
/// carried. The loop's own guarantees are <c>SamplerLoopTests</c>'.
/// </summary>
public sealed class ListedJobAdSnapshotServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Start_TheFirstReadIsNotDelayedByTheInterval()
    {
        // The shipped interval is five minutes, longer than the test's own wait, so only the immediate tick satisfies it.
        var sampler = Substitute.For<IListedJobAdSnapshotSampler>();
        using var service = new ListedJobAdSnapshotService(sampler, Substitute.For<ILogger<ListedJobAdSnapshotService>>());

        await service.StartAsync(Ct);
        await Eventually(() => sampler.ReceivedCalls().Any(), "the first read waited for the timer");
        await service.StopAsync(Ct);
    }

    [Fact]
    public async Task Service_AReadThatThrows_NeverFaultsTheService()
    {
        var sampler = Substitute.For<IListedJobAdSnapshotSampler>();
        sampler.SampleAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromException(new InvalidOperationException("boom")));
        using var service = new ListedJobAdSnapshotService(sampler, Substitute.For<ILogger<ListedJobAdSnapshotService>>());

        await service.StartAsync(Ct);
        await Eventually(() => sampler.ReceivedCalls().Any(), "the first read ran");

        service.ExecuteTask.ShouldNotBeNull().IsFaulted.ShouldBeFalse("a faulted ExecuteAsync stops the whole API host");
        await service.StopAsync(Ct);
        service.ExecuteTask.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Tick_ALoggedFailure_IsOneWarningCarryingTheTypeNameAndNotTheMessage()
    {
        var sampler = Substitute.For<IListedJobAdSnapshotSampler>();
        sampler.SampleAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("Host=db;Password=hunter2")));
        var logger = new Jobbliggaren.TestSupport.RecordingLogger<ListedJobAdSnapshotService>();
        using var service = new ListedJobAdSnapshotService(sampler, logger);

        await service.TickAsync(Ct);

        var line = logger.Records.ShouldHaveSingleItem();
        (line.Level, line.EventId.Id).ShouldBe((LogLevel.Warning, 6233));
        line.Message.ShouldContain("InvalidOperationException");
        line.Message.ShouldNotContain("hunter2");
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

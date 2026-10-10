using System.Text.Json;
using Jobbliggaren.Application.Admin.HostObservations;
using Jobbliggaren.Application.Admin.HostObservations.Queries.GetHostObservation;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Authorization;
using Jobbliggaren.Application.Common.Behaviors;
using Jobbliggaren.Application.Common.Exceptions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.HostObservations;

/// <summary>
/// The query turns the newest snapshot into what the page may claim, against the API clock (#1982). It does
/// no I/O, so every case is a snapshot and a clock.
/// </summary>
public class GetHostObservationQueryHandlerTests
{
    private static readonly DateTimeOffset Sampled = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly MutableClock _clock = new(Sampled.AddSeconds(10));
    private readonly Reader _reader = new(HealthySnapshot());

    private GetHostObservationQueryHandler Handler() =>
        new(_reader, _clock, Options.Create(new HostObservationOptions()));

    private async Task<HostObservationDto> ReadAsync() =>
        await Handler().Handle(new GetHostObservationQuery(), Ct);

    private static HostObservationSnapshot HealthySnapshot() => new(
        Sampled,
        HostMetric.Available(Sampled, new CpuValue(2.5, 30)),
        HostMetric.Available(Sampled, new MemoryValue(29.4, 2_449_854_464, 8_331_255_808)),
        HostMetric.Available(Sampled, new DiskValue(6.1, 242_287_181_824, 258_154_033_152)));

    [Fact]
    public async Task Handle_FreshSnapshot_PassesEveryReadingThroughAndStampsTheReadTimeSeparately()
    {
        var dto = await ReadAsync();

        dto.ReadAt.ShouldBe(Sampled.AddSeconds(10));
        dto.StaleAfterSeconds.ShouldBe(120);
        dto.Cpu.State.ShouldBe(HostMetricState.Available);
        dto.Cpu.SampledAt.ShouldBe(Sampled, "the reading keeps the time the host was sampled, not the time of the read");
        dto.Memory.Value.ShouldBe(new MemoryValue(29.4, 2_449_854_464, 8_331_255_808));
        dto.Disk.State.ShouldBe(HostMetricState.Available);
    }

    // The actor: the sampler stopping (the hosted service died, or the thread pool starved it).
    [Fact]
    public async Task Handle_ASamplerThatHasStopped_TurnsValuesIntoStaleKeepingTheirOriginalTime()
    {
        _clock.UtcNow = Sampled.AddSeconds(121);

        var dto = await ReadAsync();

        dto.Cpu.State.ShouldBe(HostMetricState.Stale);
        dto.Cpu.SampledAt.ShouldBe(Sampled);
        dto.Cpu.Value.ShouldBe(new CpuValue(2.5, 30));
    }

    [Fact]
    public async Task Handle_ExactlyAtTheStaleLimit_IsStillAvailable()
    {
        _clock.UtcNow = Sampled.AddSeconds(120);

        (await ReadAsync()).Memory.State.ShouldBe(HostMetricState.Available);
    }

    [Fact]
    public async Task Handle_AReadingWithoutAValueFromAStoppedSampler_IsFailedNotCollectingForever()
    {
        _reader.Current = HostObservationSnapshot.NotSampled(Sampled);
        _clock.UtcNow = Sampled.AddSeconds(121);

        var dto = await ReadAsync();

        dto.Cpu.State.ShouldBe(HostMetricState.Failed);
        dto.Memory.State.ShouldBe(HostMetricState.Failed);
        dto.Disk.State.ShouldBe(HostMetricState.Failed);
        dto.Disk.Value.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ARunningSamplerStillCollecting_StaysCollecting()
    {
        _reader.Current = HostObservationSnapshot.NotSampled(Sampled);
        _clock.UtcNow = Sampled.AddSeconds(20);

        (await ReadAsync()).Cpu.State.ShouldBe(HostMetricState.Collecting);
    }

    [Fact]
    public async Task Handle_ANotObservableReading_IsNotAgedIntoAFault()
    {
        _reader.Current = HostObservationSnapshot.NotSampled(Sampled) with
        {
            Cpu = HostMetric.Without<CpuValue>(HostMetricReason.PlatformUnsupported),
        };
        _clock.UtcNow = Sampled.AddHours(3);

        (await ReadAsync()).Cpu.State.ShouldBe(HostMetricState.NotObservable);
    }

    // The actor: the wall clock. A sample stamped before the clock stepped backwards is dated in the future.
    [Fact]
    public async Task Handle_ASampleDatedInTheFuture_IsNeverPresentedAsFresh()
    {
        _clock.UtcNow = Sampled.AddSeconds(-6);

        var dto = await ReadAsync();

        dto.Cpu.State.ShouldBe(HostMetricState.Failed);
        dto.Cpu.SampledAt.ShouldBeNull();
        dto.Memory.Value.ShouldBeNull();
    }

    [Fact]
    public async Task Handle_ASampleAFewSecondsAhead_IsWithinTheClockTolerance()
    {
        _clock.UtcNow = Sampled.AddSeconds(-4);

        (await ReadAsync()).Cpu.State.ShouldBe(HostMetricState.Available);
    }

    [Fact]
    public async Task Handle_OneFailedReading_LeavesTheOthersAlone()
    {
        _reader.Current = HealthySnapshot() with { Memory = HostMetric.Without<MemoryValue>(HostMetricReason.ReadFailed) };

        var dto = await ReadAsync();

        dto.Memory.State.ShouldBe(HostMetricState.Failed);
        dto.Memory.Value.ShouldBeNull();
        dto.Cpu.State.ShouldBe(HostMetricState.Available);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(121)]
    [InlineData(100_000)]
    public async Task Handle_EveryStateObeysTheInvariantsTheBrowserRelies_AtAnyAge(int ageSeconds)
    {
        var snapshots = new[]
        {
            HealthySnapshot(),
            HostObservationSnapshot.NotSampled(Sampled),
            HealthySnapshot() with { Cpu = HostMetric.Without<CpuValue>(HostMetricReason.CounterReset) },
            HealthySnapshot() with { Memory = HostMetric.Without<MemoryValue>(HostMetricReason.HostViewUnverified) },
            HealthySnapshot() with { Disk = HostMetric.Without<DiskValue>(HostMetricReason.PlatformUnsupported) },
        };
        _clock.UtcNow = Sampled.AddSeconds(ageSeconds);

        foreach (var snapshot in snapshots)
        {
            _reader.Current = snapshot;
            var dto = await ReadAsync();
            AssertInvariant(dto.Cpu);
            AssertInvariant(dto.Memory);
            AssertInvariant(dto.Disk);
        }
    }

    private static void AssertInvariant<T>(HostMetricDto<T> metric)
        where T : class
    {
        if (metric.State is HostMetricState.Available or HostMetricState.Stale)
        {
            metric.Value.ShouldNotBeNull();
            metric.SampledAt.ShouldNotBeNull();
        }
        else
        {
            metric.Value.ShouldBeNull("a number that is not known is null, never zero");
            metric.SampledAt.ShouldBeNull();
        }
    }

    [Fact]
    public async Task Json_CarriesOnlyTheAgreedFields_WithStringStatesAndExplicitNulls()
    {
        _reader.Current = HealthySnapshot() with { Disk = HostMetric.Without<DiskValue>(HostMetricReason.ReadFailed) };

        var json = JsonSerializer.Serialize(await ReadAsync(), JsonSerializerOptions.Web);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Names(root).ShouldBe(["readAt", "staleAfterSeconds", "cpu", "memory", "disk"]);
        Names(root.GetProperty("cpu")).ShouldBe(["state", "sampledAt", "value"]);
        Names(root.GetProperty("cpu").GetProperty("value")).ShouldBe(["percent", "windowSeconds"]);
        Names(root.GetProperty("memory").GetProperty("value")).ShouldBe(["percent", "usedBytes", "totalBytes"]);
        Names(root.GetProperty("disk")).ShouldBe(["state", "sampledAt", "value"]);
        root.GetProperty("cpu").GetProperty("state").GetString().ShouldBe("Available");
        root.GetProperty("disk").GetProperty("state").GetString().ShouldBe("Failed");
        root.GetProperty("disk").GetProperty("sampledAt").ValueKind.ShouldBe(JsonValueKind.Null);
        root.GetProperty("disk").GetProperty("value").ValueKind.ShouldBe(JsonValueKind.Null);
        // Nothing that names the machine, its layout or the failure reaches the page.
        json.ShouldNotContain("reason");
        json.ShouldNotContain("ReadFailed");
        json.ShouldNotContain("vda");
    }

    [Fact]
    public async Task Json_TheDiskValueCarriesFreeAndTotalAndThePercentOfThatTotal()
    {
        var json = JsonSerializer.Serialize(await ReadAsync(), JsonSerializerOptions.Web);

        using var document = JsonDocument.Parse(json);
        var disk = document.RootElement.GetProperty("disk").GetProperty("value");
        Names(disk).ShouldBe(["percent", "freeBytes", "totalBytes"]);
        var (percent, free, total) = (disk.GetProperty("percent").GetDouble(),
            disk.GetProperty("freeBytes").GetInt64(), disk.GetProperty("totalBytes").GetInt64());
        Math.Round(100.0 * (total - free) / total, 1).ShouldBe(percent, "the card never pairs the meter with another denominator");
    }

    private static string[] Names(JsonElement element) => element.EnumerateObject().Select(p => p.Name).ToArray();

    [Fact]
    public void Query_IsAnAdminRequest()
    {
        typeof(IAdminRequest).IsAssignableFrom(typeof(GetHostObservationQuery)).ShouldBeTrue();
    }

    [Fact]
    public async Task Authorization_ShouldRefuseAnOrdinaryCaller_BeforeTheReaderIsAsked()
    {
        var user = Substitute.For<ICurrentUser>();
        user.IsAuthenticated.Returns(true);
        user.IsInRole(Roles.Admin).Returns(false);
        var reader = Substitute.For<IHostObservationReader>();
        var handler = new GetHostObservationQueryHandler(reader, _clock, Options.Create(new HostObservationOptions()));
        var behavior = new AdminAuthorizationBehavior<GetHostObservationQuery, HostObservationDto>(user);

        await Should.ThrowAsync<ForbiddenException>(async () =>
            await behavior.Handle(new GetHostObservationQuery(), handler.Handle, Ct));

        _ = reader.DidNotReceive().Current;
    }

    private sealed class Reader(HostObservationSnapshot snapshot) : IHostObservationReader
    {
        public HostObservationSnapshot Current { get; set; } = snapshot;
    }
}

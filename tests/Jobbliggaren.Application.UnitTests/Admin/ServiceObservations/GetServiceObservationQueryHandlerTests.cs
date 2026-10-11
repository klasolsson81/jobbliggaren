using System.Text.Json;
using Jobbliggaren.Application.Admin.ServiceObservations;
using Jobbliggaren.Application.Admin.ServiceObservations.Queries.GetServiceObservation;
using Jobbliggaren.Application.Common.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.ServiceObservations;

/// <summary>
/// The handler is a function of the reader, the clock and the options (#1982, ADR 0159), and what leaves it is made of
/// states, instants, one count and three limits. The wire tests serialise the DTO the way the Api does
/// (<c>JsonSerializerDefaults.Web</c>) and pin the key set per state, so a field that is added to a row by accident
/// fails here before it reaches a browser.
/// </summary>
public sealed class GetServiceObservationQueryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private sealed class FixedReader(ServiceObservationSnapshot snapshot) : IServiceObservationReader
    {
        public ServiceObservationSnapshot Current { get; } = snapshot;
    }

    private static ServiceObservationSnapshot Healthy() => new(
        SourceSample.Of(Now.AddSeconds(-4), new WorkerObservation(true, Now.AddSeconds(-9), Now.AddHours(-1))),
        SourceSample.Of(Now.AddSeconds(-4), true),
        SourceSample.Of(Now.AddSeconds(-4), new ImportObservation(new StreamImportRun(Now.AddMinutes(-6), 1))));

    private static async Task<JsonElement> WireAsync(ServiceObservationSnapshot snapshot)
    {
        var handler = new GetServiceObservationQueryHandler(
            new FixedReader(snapshot), new MutableClock(Now), Options.Create(new ServiceObservationOptions()));
        var dto = await handler.Handle(new GetServiceObservationQuery(), TestContext.Current.CancellationToken);
        return JsonSerializer.SerializeToElement(dto, Web);
    }

    private static string[] Names(JsonElement element) => element.EnumerateObject().Select(p => p.Name).ToArray();

    [Fact]
    public void Query_IsAnAdminRequest_SoTheMediatorPipelineRefusesEveryoneElse()
    {
        typeof(IAdminRequest).IsAssignableFrom(typeof(GetServiceObservationQuery)).ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_JudgesTheNewestSnapshotAgainstTheInjectedClock()
    {
        var handler = new GetServiceObservationQueryHandler(
            new FixedReader(Healthy()), new MutableClock(Now), Options.Create(new ServiceObservationOptions()));

        var dto = await handler.Handle(new GetServiceObservationQuery(), TestContext.Current.CancellationToken);

        dto.ReadAt.ShouldBe(Now);
        dto.BackgroundJobs.State.ShouldBe(WorkerProcessState.Alive);
        dto.VolatileStore.State.ShouldBe(VolatileStoreState.Reachable);
        dto.JobImport.State.ShouldBe(StreamImportState.Recorded);
    }

    [Fact]
    public async Task Handle_TheStaleLimitIsTheConfiguredOneAndIsSentSoThePageAgesWhatItKeepsByTheSameRule()
    {
        var handler = new GetServiceObservationQueryHandler(
            new FixedReader(Healthy()), new MutableClock(Now),
            Options.Create(new ServiceObservationOptions { SampleIntervalSeconds = 10, StaleAfterSeconds = 45 }));

        var dto = await handler.Handle(new GetServiceObservationQuery(), TestContext.Current.CancellationToken);

        dto.StaleAfterSeconds.ShouldBe(45);
    }

    [Fact]
    public async Task Wire_TheBodyCarriesOnlyTheAgreedKeys_InTheirOrder()
    {
        var body = await WireAsync(Healthy());

        Names(body).ShouldBe([
            "staleAfterSeconds", "workerAliveWithinSeconds", "importOverdueAfterSeconds",
            "backgroundJobs", "volatileStore", "jobImport"]);
        Names(body.GetProperty("backgroundJobs")).ShouldBe(["state", "sampledAt", "heartbeatAt"]);
        Names(body.GetProperty("volatileStore")).ShouldBe(["state", "sampledAt"]);
        Names(body.GetProperty("jobImport")).ShouldBe(["state", "sampledAt", "lastRunAt", "errors"]);
    }

    [Fact]
    public async Task Wire_StatesAreStrings_AndNumbersAreNumbers()
    {
        var body = await WireAsync(Healthy());

        body.GetProperty("backgroundJobs").GetProperty("state").GetString().ShouldBe("Alive");
        body.GetProperty("volatileStore").GetProperty("state").GetString().ShouldBe("Reachable");
        body.GetProperty("jobImport").GetProperty("state").GetString().ShouldBe("Recorded");
        body.GetProperty("jobImport").GetProperty("errors").GetInt32().ShouldBe(1);
        body.GetProperty("staleAfterSeconds").GetInt32().ShouldBe(120);
        body.GetProperty("workerAliveWithinSeconds").GetInt32().ShouldBe(120);
        body.GetProperty("importOverdueAfterSeconds").GetInt32().ShouldBe(1500);
    }

    [Fact]
    public async Task Wire_AnUnknownRow_CarriesNoInstantAndNoCount()
    {
        var body = await WireAsync(ServiceObservationSnapshot.NotSampled());

        foreach (var row in new[] { "backgroundJobs", "volatileStore", "jobImport" })
        {
            var element = body.GetProperty(row);
            element.GetProperty("state").GetString().ShouldBe("Unknown");
            foreach (var property in element.EnumerateObject().Where(p => p.Name != "state"))
            {
                property.Value.ValueKind.ShouldBe(JsonValueKind.Null, $"{row}.{property.Name} must be null when the row is unknown");
            }
        }
    }

    [Fact]
    public async Task Wire_TheDtoHasNoPlaceForAReasonAnAddressAnAuditPayloadOrFreeText()
    {
        var text = (await WireAsync(Healthy())).GetRawText();

        foreach (var forbidden in new[]
                 {
                     "reason", "message", "description", "server", "queue", "workers", "latency", "ping", "payload",
                     "source", "fetched", "aggregate", "exception", "Platsbanken", "JobTech", "redis", "hangfire",
                 })
        {
            text.ShouldNotContain(forbidden, Case.Insensitive);
        }
    }

    [Fact]
    public void Dto_ReadAtIsNotPartOfTheBody_ItTravelsInTheObservationHeader()
    {
        var dto = ServiceStatusEvaluator.Evaluate(Healthy(), Now, new ServiceObservationOptions());

        JsonSerializer.SerializeToElement(dto, Web).TryGetProperty("readAt", out _).ShouldBeFalse();
    }
}

using System.Net;
using System.Text.Json;
using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobAds;
using JobbPilot.Infrastructure.JobSources.Platsbanken;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly;
using Shouldly;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace JobbPilot.Api.IntegrationTests.JobAds;

/// <summary>
/// Resilience-tester för JobTech-stream-pipelinen. Stubbar JobTech-API via
/// WireMock och verifierar att retry-pipelinen tolererar transient 503 +
/// att polymorft event-schema (upsert/removal) parsas korrekt av
/// <see cref="PlatsbankenJobSource"/>.
/// </summary>
/// <remarks>
/// Använder lokal DI-container snarare än ApiFactory eftersom resilience-
/// pipelinen registreras separat från Identity/Postgres-stacken. Rate-limiter
/// är process-statisk, så testerna är medvetet i SAMMA Collection och kör
/// sekventiellt (parallelizeTestCollections=false). 503-retry-testet använder
/// stateful WireMock-stub som blir healthy efter två failures.
/// </remarks>
public class JobTechStreamResilienceTests
{
    [Fact]
    public async Task FetchSnapshotAsync_TolerantesTransient503ViaRetryPipeline()
    {
        var ct = TestContext.Current.CancellationToken;
        using var server = WireMockServer.Start();
        // v2-shape: webpage_url på top-level (web-verifierat 2026-05-13).
        var snapshotJson = """[{"id":"hit-1","headline":"Dev","description":{"text":"d"},"employer":{"name":"X"},"webpage_url":"https://e/1","publication_date":"2026-05-12T10:00:00Z"}]""";

        // Stateful stub: 2× 503, sedan 200. Polly retry (3 attempts) ska nå 200.
        server
            .Given(Request.Create().WithPath("/v2/snapshot").UsingGet())
            .InScenario("transient-503")
            .WillSetStateTo("after-first-503")
            .RespondWith(Response.Create().WithStatusCode(503));

        server
            .Given(Request.Create().WithPath("/v2/snapshot").UsingGet())
            .InScenario("transient-503")
            .WhenStateIs("after-first-503")
            .WillSetStateTo("after-second-503")
            .RespondWith(Response.Create().WithStatusCode(503));

        server
            .Given(Request.Create().WithPath("/v2/snapshot").UsingGet())
            .InScenario("transient-503")
            .WhenStateIs("after-second-503")
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(snapshotJson));

        var jobSource = BuildJobSource(server.Url!);

        var snapshot = await jobSource.FetchSnapshotAsync(ct);

        snapshot.Items.Count.ShouldBe(1);
        snapshot.Items[0].ExternalId.ShouldBe("hit-1");
    }

    [Fact]
    public async Task StreamChangesAsync_ParsesPolymorphicUpsertAndRemovalEvents()
    {
        var ct = TestContext.Current.CancellationToken;
        using var server = WireMockServer.Start();
        // v2-shape: webpage_url på top-level + removal-event utan extra fields.
        var streamJson = """
        [
            {
                "id": "upsert-1",
                "headline": "New Job",
                "description": { "text": "desc" },
                "employer": { "name": "Acme" },
                "webpage_url": "https://e/1",
                "publication_date": "2026-05-12T10:00:00Z"
            },
            {
                "id": "removal-1",
                "removed": true,
                "removed_date": "2026-05-12T11:00:00Z"
            }
        ]
        """;

        server
            .Given(Request.Create().WithPath("/v2/stream").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(streamJson));

        var jobSource = BuildJobSource(server.Url!);
        var since = new DateTimeOffset(2026, 5, 12, 9, 0, 0, TimeSpan.Zero);

        var changes = new List<JobAdChange>();
        await foreach (var change in jobSource.StreamChangesAsync(since, ct))
            changes.Add(change);

        changes.Count.ShouldBe(2);
        changes.OfType<JobAdUpsert>().Count().ShouldBe(1);
        changes.OfType<JobAdRemoval>().Count().ShouldBe(1);
        changes.OfType<JobAdRemoval>().Single().ExternalId.ShouldBe("removal-1");
    }

    private static IJobSource BuildJobSource(string baseUrl)
    {
        // Bygger en isolerad DI-container för testet. Använder inte den
        // process-statiska rate-limitern (passar JobStream production) eftersom
        // vi testar resilience-pipelinen, inte rate-limit-semantiken. Stream-
        // klienten + Polly-retry/CB via AddResilienceHandler.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOptions<JobTechOptions>>(
            Options.Create(new JobTechOptions
            {
                JobSearchBaseUrl = baseUrl,
                JobStreamBaseUrl = baseUrl,
                ApiKey = string.Empty,
                RawPayloadRetentionDays = 30,
            }));

        services.AddSingleton<IDateTimeProvider, FixedClock>();

        services.AddHttpClient<IJobTechStreamClient, JobTechStreamClient>(client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        })
        .AddResilienceHandler("test-jobstream", builder =>
        {
            builder.AddRetry(new Microsoft.Extensions.Http.Resilience.HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Constant,
                Delay = TimeSpan.FromMilliseconds(10),
            });
        });

        services.AddScoped<IJobSource, PlatsbankenJobSource>();

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IJobSource>();
    }

    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } =
            new(2026, 5, 12, 12, 0, 0, TimeSpan.Zero);
    }
}

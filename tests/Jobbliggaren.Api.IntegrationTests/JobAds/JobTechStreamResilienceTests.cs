using System.Net;
using System.Text.Json;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.JobSources.Platsbanken;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Refit;
using Shouldly;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

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

        // FetchSnapshotAsync är nu IAsyncEnumerable (root-cause-fix 2026-05-16).
        // Resilience-beteendet (Polly retry över 2× 503 → 200) verifieras genom
        // att strömmen kan konsumeras fullständigt och ger förväntat item.
        var items = new List<JobAdImportItem>();
        await foreach (var item in jobSource.FetchSnapshotAsync(new SnapshotOutcomeRecorder(), ct))
            items.Add(item);

        items.Count.ShouldBe(1);
        items[0].ExternalId.ShouldBe("hit-1");
    }

    [Fact]
    public async Task FetchSnapshotAsync_WhenResponseTruncatedMidStream_DoesNotThrowUncaught_YieldsParsedPrefix()
    {
        // REGRESSIONSTEST för rotorsaken (Batch 0 2026-05-16, CloudWatch-evidens):
        // /v2/snapshot >364 MB singel-GET termineras icke-deterministiskt
        // mid-stream → System.Text.Json "reached end of data" kastades OFÅNGAT
        // vid enumeration (PlatsbankenJobSource saknade try/catch runt
        // await foreach) → propagerade till SyncPlatsbankenSnapshotJob (vars
        // try/catch bara omslöt per-item-upsert) → Hangfire.AutomaticRetry-storm
        // (60 starts / 0 completes). Efter fix: enumeration-boundary-catch i
        // PlatsbankenJobSource ska hantera trunkering gracefully — yielda
        // parsad prefix (idempotent persisterad via UNIQUE-index), avsluta
        // strömmen, ALDRIG kasta ofångat. CTO MA 3.1 Variant A 2026-05-16.
        var ct = TestContext.Current.CancellationToken;
        using var server = WireMockServer.Start();

        // Giltig JSON-array-prefix (hit-1 komplett) som sedan kapas mitt i
        // hit-2 — exakt trunkerings-shape som JobTech-droppen producerar.
        var truncatedBody =
            """[{"id":"hit-1","headline":"Dev","description":{"text":"d"},"employer":{"name":"X"},"webpage_url":"https://e/1","publication_date":"2026-05-12T10:00:00Z"},{"id":"hit-2","headline":"Dev2","desc""";

        server
            .Given(Request.Create().WithPath("/v2/snapshot").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(truncatedBody));

        var jobSource = BuildJobSource(server.Url!);

        var items = new List<JobAdImportItem>();
        // Får INTE kasta ofångat (rotorsaken). Bounded retry uttömd → graceful
        // end. WireMock returnerar samma trunkerade svar varje försök.
        var enumeration = async () =>
        {
            await foreach (var item in jobSource.FetchSnapshotAsync(new SnapshotOutcomeRecorder(), ct))
                items.Add(item);
        };

        await enumeration.ShouldNotThrowAsync();
        // Prefixen som hann parsas före trunkeringen ska ha yieldats
        // (persisteras idempotent av konsumenten via UNIQUE-index).
        items.ShouldContain(i => i.ExternalId == "hit-1");
    }

    [Fact]
    public async Task FetchSnapshotAsync_MalformedElementMidArray_SkipsPoisonAndYieldsTheTail()
    {
        // #509 — DETERMINISTIC POISON PILL. A data-level JsonException (schema drift:
        // a malformed publication_date here) is NOT truncation: refetching cannot fix
        // it, so all 3 snapshot attempts used to break at the same element — the whole
        // tail was never ingested and TruncatedAndExhausted=true suppressed
        // miss-tracking EVERY night until the poison left the feed. Post-fix the
        // client parses per element (JsonElement + per-element Deserialize) and skips
        // the poison element; transport errors keep the truncation-retry (see the
        // WhenResponseTruncatedMidStream test, which must stay green).
        var ct = TestContext.Current.CancellationToken;
        using var server = WireMockServer.Start();

        var poisonedBody =
            """[{"id":"hit-1","headline":"Dev","description":{"text":"d"},"employer":{"name":"X"},"webpage_url":"https://e/1","publication_date":"2026-05-12T10:00:00Z"},{"id":"poison-2","headline":"Dev2","description":{"text":"d"},"employer":{"name":"X"},"webpage_url":"https://e/2","publication_date":"inte-ett-datum"},{"id":"hit-3","headline":"Dev3","description":{"text":"d"},"employer":{"name":"X"},"webpage_url":"https://e/3","publication_date":"2026-05-12T10:00:00Z"}]""";

        server
            .Given(Request.Create().WithPath("/v2/snapshot").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(poisonedBody));

        var jobSource = BuildJobSource(server.Url!);
        var recorder = new SnapshotOutcomeRecorder();

        var items = new List<JobAdImportItem>();
        await foreach (var item in jobSource.FetchSnapshotAsync(recorder, ct))
            items.Add(item);

        // The TAIL after the poison element must be ingested (the whole point of #509).
        items.Select(i => i.ExternalId).ShouldBe(["hit-1", "hit-3"],
            "the poison element is skipped, both valid elements around it are yielded");

        // A data error is NOT truncation → no bounded-retry exhaustion, miss-tracking
        // must NOT be suppressed.
        recorder.Outcome.ShouldNotBeNull();
        recorder.Outcome.TruncatedAndExhausted.ShouldBeFalse(
            "a deterministic data error must not masquerade as truncation");
        recorder.Outcome.ParsedTotal.ShouldBe(2);

        // ...and no full-refetch retry: refetching cannot fix a data error. ONE GET.
        server.FindLogEntries(Request.Create().WithPath("/v2/snapshot").UsingGet())
            .Count.ShouldBe(1, "a data-level JsonException must not trigger the truncation refetch");
    }

    [Fact]
    public async Task FetchSnapshotAsync_WithoutPoison_YieldsEveryElement_AndLogsNoSkipEvents()
    {
        // Counterfactual for the poison-skip test: the same shape WITHOUT the poison
        // element yields ALL elements — proves the skip is targeted at the malformed
        // element, not silently dropping a tail. The capturing logger additionally
        // pins that a CLEAN run emits neither per-element (5011) nor summary (5012)
        // events — an unconditional summary would be log noise every night.
        var ct = TestContext.Current.CancellationToken;
        using var server = WireMockServer.Start();

        var cleanBody =
            """[{"id":"hit-1","headline":"Dev","description":{"text":"d"},"employer":{"name":"X"},"webpage_url":"https://e/1","publication_date":"2026-05-12T10:00:00Z"},{"id":"hit-2","headline":"Dev2","description":{"text":"d"},"employer":{"name":"X"},"webpage_url":"https://e/2","publication_date":"2026-05-12T10:00:00Z"},{"id":"hit-3","headline":"Dev3","description":{"text":"d"},"employer":{"name":"X"},"webpage_url":"https://e/3","publication_date":"2026-05-12T10:00:00Z"}]""";

        server
            .Given(Request.Create().WithPath("/v2/snapshot").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(cleanBody));

        var capture = new CapturingLoggerProvider();
        var jobSource = BuildJobSource(server.Url!, capture);

        var items = new List<JobAdImportItem>();
        await foreach (var item in jobSource.FetchSnapshotAsync(new SnapshotOutcomeRecorder(), ct))
            items.Add(item);

        items.Select(i => i.ExternalId).ShouldBe(["hit-1", "hit-2", "hit-3"]);
        capture.Entries.ShouldNotContain(e => e.EventId.Id == 5011 || e.EventId.Id == 5012);
    }

    [Fact]
    public async Task FetchSnapshotAsync_NullElementInArray_IsSilentlySkipped_NotCountedAsMalformed()
    {
        // A literal JSON null element is a DISTINCT skip class from a malformed
        // element: Deserialize<JobTechHit> returns null without throwing → silent
        // skip — not counted in ParsedTotal, not logged as 5011/5012 (a null is
        // not schema drift worth an operator Warning).
        var ct = TestContext.Current.CancellationToken;
        using var server = WireMockServer.Start();

        var bodyWithNull =
            """[{"id":"hit-1","headline":"Dev","description":{"text":"d"},"employer":{"name":"X"},"webpage_url":"https://e/1","publication_date":"2026-05-12T10:00:00Z"},null,{"id":"hit-3","headline":"Dev3","description":{"text":"d"},"employer":{"name":"X"},"webpage_url":"https://e/3","publication_date":"2026-05-12T10:00:00Z"}]""";

        server
            .Given(Request.Create().WithPath("/v2/snapshot").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(bodyWithNull));

        var capture = new CapturingLoggerProvider();
        var jobSource = BuildJobSource(server.Url!, capture);
        var recorder = new SnapshotOutcomeRecorder();

        var items = new List<JobAdImportItem>();
        await foreach (var item in jobSource.FetchSnapshotAsync(recorder, ct))
            items.Add(item);

        items.Select(i => i.ExternalId).ShouldBe(["hit-1", "hit-3"]);
        recorder.Outcome.ShouldNotBeNull();
        recorder.Outcome.ParsedTotal.ShouldBe(2);
        capture.Entries.ShouldNotContain(e => e.EventId.Id == 5011 || e.EventId.Id == 5012);
    }

    [Fact]
    public async Task FetchSnapshotAsync_NonObjectElement_IsMalformedSkipped_WithNullIdInTheLog()
    {
        // A bare number in the array IS a malformed element (Deserialize throws) and
        // TryReadElementId must take its non-object branch → the 5011 log line
        // renders id=(null) instead of crashing or reading a bogus key.
        var ct = TestContext.Current.CancellationToken;
        using var server = WireMockServer.Start();

        var bodyWithNumber =
            """[{"id":"hit-1","headline":"Dev","description":{"text":"d"},"employer":{"name":"X"},"webpage_url":"https://e/1","publication_date":"2026-05-12T10:00:00Z"},42,{"id":"hit-3","headline":"Dev3","description":{"text":"d"},"employer":{"name":"X"},"webpage_url":"https://e/3","publication_date":"2026-05-12T10:00:00Z"}]""";

        server
            .Given(Request.Create().WithPath("/v2/snapshot").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(bodyWithNumber));

        var capture = new CapturingLoggerProvider();
        var jobSource = BuildJobSource(server.Url!, capture);

        var items = new List<JobAdImportItem>();
        await foreach (var item in jobSource.FetchSnapshotAsync(new SnapshotOutcomeRecorder(), ct))
            items.Add(item);

        items.Select(i => i.ExternalId).ShouldBe(["hit-1", "hit-3"]);
        var skipEntry = capture.Entries.Where(e => e.EventId.Id == 5011).ShouldHaveSingleItem();
        skipEntry.Message.ShouldContain("id=(null)");
    }

    [Fact]
    public async Task FetchSnapshotAsync_MalformedThenTruncated_LogsPerElementSkipsButNeverASummary()
    {
        // The summary (5012) is only reached on CLEAN enumeration end. An attempt
        // that hits a malformed element and THEN truncates must log the per-element
        // 5011 (once per attempt × 3 retries) but never a summary — if the summary
        // ever moved into a finally, truncated attempts would emit misleading
        // per-attempt totals three times a night.
        var ct = TestContext.Current.CancellationToken;
        using var server = WireMockServer.Start();

        var malformedThenTruncated =
            """[{"id":"poison-1","headline":"Bad","description":{"text":"d"},"employer":{"name":"X"},"webpage_url":"https://e/1","publication_date":"inte-ett-datum"},{"id":"hit-2","headline":"Dev2","desc""";

        server
            .Given(Request.Create().WithPath("/v2/snapshot").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(malformedThenTruncated));

        var capture = new CapturingLoggerProvider();
        var jobSource = BuildJobSource(server.Url!, capture);
        var recorder = new SnapshotOutcomeRecorder();

        var items = new List<JobAdImportItem>();
        await foreach (var item in jobSource.FetchSnapshotAsync(recorder, ct))
            items.Add(item);

        recorder.Outcome.ShouldNotBeNull();
        recorder.Outcome.TruncatedAndExhausted.ShouldBeTrue(
            "the mid-element cut is still transport truncation");
        capture.Entries.Count(e => e.EventId.Id == 5011).ShouldBe(3,
            "one malformed-skip Warning per attempt, three attempts");
        capture.Entries.ShouldNotContain(e => e.EventId.Id == 5012);
    }

    [Fact]
    public async Task StreamChangesAsync_PoisonBeforeUpsert_AfterRemoval_YieldsBothRealEvents()
    {
        // Ordering symmetry for the stream path: the poison element sits AFTER the
        // removal and BEFORE the upsert — both real events must still be delivered
        // (no cross-element state in the tolerant parse).
        var ct = TestContext.Current.CancellationToken;
        using var server = WireMockServer.Start();

        var streamJson =
            """[{"id":"removal-1","removed":true,"removed_date":"2026-05-12T11:00:00Z"},{"id":"poison-2","headline":"Bad","description":{"text":"d"},"employer":{"name":"X"},"webpage_url":"https://e/2","publication_date":"inte-ett-datum"},{"id":"upsert-3","headline":"New Job","description":{"text":"desc"},"employer":{"name":"Acme"},"webpage_url":"https://e/3","publication_date":"2026-05-12T10:00:00Z"}]""";

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
        changes.OfType<JobAdRemoval>().Single().ExternalId.ShouldBe("removal-1");
        changes.OfType<JobAdUpsert>().Single().ExternalId.ShouldBe("upsert-3");
    }

    [Fact]
    public async Task FetchSnapshotAsync_WhenResponseTruncatedMidStream_StillRetriesThreeTimes()
    {
        // #509 structural separation, the other direction: a SYNTAX-level JsonException
        // (body cut mid-element) is thrown by the array enumerator itself — OUTSIDE the
        // per-element tolerant parse — and must KEEP the bounded truncation-retry
        // (MaxSnapshotAttempts=3 fresh GETs). If the per-element catch is ever widened
        // to swallow the enumerator boundary, this pin goes red.
        var ct = TestContext.Current.CancellationToken;
        using var server = WireMockServer.Start();

        var truncatedBody =
            """[{"id":"hit-1","headline":"Dev","description":{"text":"d"},"employer":{"name":"X"},"webpage_url":"https://e/1","publication_date":"2026-05-12T10:00:00Z"},{"id":"hit-2","headline":"Dev2","desc""";

        server
            .Given(Request.Create().WithPath("/v2/snapshot").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(truncatedBody));

        var jobSource = BuildJobSource(server.Url!);
        var recorder = new SnapshotOutcomeRecorder();

        var items = new List<JobAdImportItem>();
        await foreach (var item in jobSource.FetchSnapshotAsync(recorder, ct))
            items.Add(item);

        recorder.Outcome.ShouldNotBeNull();
        recorder.Outcome.TruncatedAndExhausted.ShouldBeTrue(
            "a mid-element cut is transport truncation, not a data error");
        server.FindLogEntries(Request.Create().WithPath("/v2/snapshot").UsingGet())
            .Count.ShouldBe(3, "truncation keeps the bounded full-refetch retry (3 attempts)");
    }

    [Fact]
    public async Task FetchSnapshotAsync_ManyMalformedElements_CapsPerElementWarnings_AndLogsSummary()
    {
        // #509 observability (CTO F2a): per-element skip Warnings are capped (first 10)
        // so whole-corpus schema drift cannot flood the log with ~50k lines, and an
        // end-of-stream summary Warning carries the authoritative total.
        var ct = TestContext.Current.CancellationToken;
        using var server = WireMockServer.Start();

        var poisonElements = string.Join(",", Enumerable.Range(1, 12).Select(i =>
            $$"""{"id":"poison-{{i:D2}}","headline":"Dev","description":{"text":"d"},"employer":{"name":"X"},"webpage_url":"https://e/p","publication_date":"inte-ett-datum"}"""));
        var body =
            $$"""[{{poisonElements}},{"id":"hit-ok","headline":"Dev","description":{"text":"d"},"employer":{"name":"X"},"webpage_url":"https://e/ok","publication_date":"2026-05-12T10:00:00Z"}]""";

        server
            .Given(Request.Create().WithPath("/v2/snapshot").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(body));

        var capture = new CapturingLoggerProvider();
        var jobSource = BuildJobSource(server.Url!, capture);

        var items = new List<JobAdImportItem>();
        await foreach (var item in jobSource.FetchSnapshotAsync(new SnapshotOutcomeRecorder(), ct))
            items.Add(item);

        items.ShouldHaveSingleItem().ExternalId.ShouldBe("hit-ok");

        // EventId 5011 = per-element skip (capped at 10), 5012 = summary with total.
        capture.Entries.Count(e => e.EventId.Id == 5011).ShouldBe(10,
            "per-element warnings are capped so mass drift cannot flood the log");
        var summary = capture.Entries.Where(e => e.EventId.Id == 5012).ShouldHaveSingleItem();
        // The summary carries the authoritative full skip count (12), not the cap (10).
        summary.Message.ShouldContain("12");
    }

    [Fact]
    public async Task StreamChangesAsync_MalformedElementMidArray_SkipsPoisonAndYieldsRemainingEvents()
    {
        // #509 (CTO F1b) — the stream path shares the same wire shape and the same
        // poison dynamic: a deterministic data JsonException used to escape to
        // Hangfire AutomaticRetry against the same element every 10 minutes. The
        // shared tolerant parse skips it; events on BOTH sides are delivered.
        // (#483-Low — the stream path's missing TRANSPORT catch — is untouched.)
        var ct = TestContext.Current.CancellationToken;
        using var server = WireMockServer.Start();

        var streamJson =
            """[{"id":"upsert-1","headline":"New Job","description":{"text":"desc"},"employer":{"name":"Acme"},"webpage_url":"https://e/1","publication_date":"2026-05-12T10:00:00Z"},{"id":"poison-2","headline":"Bad","description":{"text":"d"},"employer":{"name":"X"},"webpage_url":"https://e/2","publication_date":"inte-ett-datum"},{"id":"removal-1","removed":true,"removed_date":"2026-05-12T11:00:00Z"}]""";

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
        changes.OfType<JobAdUpsert>().Single().ExternalId.ShouldBe("upsert-1");
        changes.OfType<JobAdRemoval>().Single().ExternalId.ShouldBe("removal-1",
            "the event AFTER the poison element must still be delivered");
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

    private static IJobSource BuildJobSource(string baseUrl, ILoggerProvider? loggerProvider = null)
    {
        // Bygger en isolerad DI-container för testet. Använder inte den
        // process-statiska rate-limitern (passar JobStream production) eftersom
        // vi testar resilience-pipelinen, inte rate-limit-semantiken. Stream-
        // klienten + Polly-retry/CB via AddResilienceHandler.
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            if (loggerProvider is not null)
                builder.AddProvider(loggerProvider);
        });
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

        // STEG 6 (2026-05-24) — PlatsbankenJobSource har nu IJobTechSearchClient
        // som constructor-dep för RefetchByExternalIdAsync. Snapshot/stream-tester
        // anropar inte refetch-vägen, men DI måste fortfarande resolvera porten.
        services.AddRefitClient<IJobTechSearchClient>()
            .ConfigureHttpClient(c => c.BaseAddress = new Uri(baseUrl));

        services.AddScoped<IJobSource, PlatsbankenJobSource>();

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IJobSource>();
    }

    private sealed class FixedClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } =
            new(2026, 5, 12, 12, 0, 0, TimeSpan.Zero);
    }

    /// <summary>
    /// #509 — captures log entries from the tolerant wire-parse so the tests can
    /// assert the capped per-element Warnings + the summary Warning (the client is
    /// HttpClient-registered infra — LoggingBehavior never wraps it, so its own
    /// log signal is the only observability and must be pinned).
    /// </summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<(EventId EventId, LogLevel Level, string Message)> _entries = [];

        public IReadOnlyList<(EventId EventId, LogLevel Level, string Message)> Entries
        {
            get { lock (_entries) return [.. _entries]; }
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose() { }

        private sealed class CapturingLogger(CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner._entries)
                    owner._entries.Add((eventId, logLevel, formatter(state, exception)));
            }
        }
    }
}

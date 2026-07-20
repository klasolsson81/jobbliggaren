using System.Runtime.CompilerServices;
using System.Text.Json;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Infrastructure.JobSources.Platsbanken;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.JobSources;

/// <summary>
/// #483 Low — stream-path enumeration-boundary catch (CTO F2 P-ACL + B-GRACE).
///
/// The snapshot path (<see cref="PlatsbankenJobSource.FetchSnapshotAsync"/>) drives its
/// enumerator manually and catches <c>JsonException/IOException/HttpRequestException</c> at
/// the <c>MoveNextAsync</c> boundary — a mid-stream transport truncation is treated as
/// truncation, not an escape (ADR 0032 §5: an uncaught enumeration throw WAS the whole
/// storm mechanism, 60 starts / 0 completes). The stream path
/// (<see cref="PlatsbankenJobSource.StreamChangesAsync"/>) previously had NO such catch, so a
/// mid-array wire error in <c>/v2/stream</c> escaped through <c>SyncPlatsbankenStreamJob</c>
/// to Hangfire AutomaticRetry.
///
/// The stream is INCREMENTAL and self-healing (the 10-min cron's 15-min overlap window +
/// the nightly snapshot + idempotent UNIQUE-index upserts already provide catch-up — "Tappade
/// kör tolereras", Fowler 2002 Idempotent Receiver), so — unlike the authoritative snapshot —
/// it does NOT retry: on transport truncation it logs a warning and completes gracefully
/// (yields the parsed prefix, no throw). Genuine cancellation still propagates.
/// </summary>
public class PlatsbankenStreamTruncationTests
{
    private static readonly DateTimeOffset FakeNow = new(2026, 7, 20, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Published = new(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);

    public enum TransportFault { Json, Io, Http }

    private static JobTechHit ValidHit(string id) => new()
    {
        Id = id,
        Headline = "Backend-utvecklare",
        Description = new JobTechDescription { Text = "Beskrivning av tjänsten." },
        Employer = new JobTechEmployer { Name = "Test Company AB" },
        WebpageUrl = "https://arbetsformedlingen.se/platsbanken/annonser/" + id,
        PublicationDate = Published,
    };

    private static PlatsbankenJobSource Sut(IJobTechStreamClient client) =>
        Sut(client, NullLogger<PlatsbankenJobSource>.Instance);

    private static PlatsbankenJobSource Sut(
        IJobTechStreamClient client, ILogger<PlatsbankenJobSource> logger) => new(
        client, new EmptySearchClient(), new FakeDateTimeProvider(FakeNow), logger);

    private const int TruncationEventId = 5010;

    private static Exception MakeFault(TransportFault fault) => fault switch
    {
        TransportFault.Json => new JsonException("mid-array malformed syntax (simulated truncation)"),
        TransportFault.Io => new IOException("mid-stream truncation (simulated)"),
        TransportFault.Http => new HttpRequestException("connection reset mid-stream (simulated)"),
        _ => throw new ArgumentOutOfRangeException(nameof(fault)),
    };

    [Fact]
    public async Task StreamChanges_CleanCompletion_YieldsEveryChange()
    {
        // Baseline: the manual-enumerator refactor must not alter the happy path.
        var ct = TestContext.Current.CancellationToken;
        var client = new ScriptedStreamClient([ValidHit("a"), ValidHit("b"), ValidHit("c")], fault: null);
        var sut = Sut(client);

        var yielded = new List<string>();
        await foreach (var change in sut.StreamChangesAsync(FakeNow, ct))
            yielded.Add(change.ExternalId);

        yielded.ShouldBe(["a", "b", "c"]);
    }

    [Theory]
    [InlineData(TransportFault.Json)]
    [InlineData(TransportFault.Io)]
    [InlineData(TransportFault.Http)]
    public async Task StreamChanges_TransportTruncationMidStream_YieldsParsedPrefixThenCompletesGracefully(
        TransportFault fault)
    {
        // THE #483 PIN: the wire yields 2 changes then breaks mid-stream with a transport
        // error. The ACL must yield the parsed prefix and then COMPLETE (no throw) — never
        // let the enumeration throw escape to Hangfire. Remove the catch and this goes red.
        var ct = TestContext.Current.CancellationToken;
        var client = new ScriptedStreamClient([ValidHit("a"), ValidHit("b")], MakeFault(fault));
        var sut = Sut(client);

        var yielded = new List<string>();
        await Should.NotThrowAsync(async () =>
        {
            await foreach (var change in sut.StreamChangesAsync(FakeNow, ct))
                yielded.Add(change.ExternalId);
        });

        yielded.ShouldBe(["a", "b"]);
    }

    [Fact]
    public async Task StreamChanges_Cancellation_Propagates_NotSwallowedAsTruncation()
    {
        // The OperationCanceledException guard must sit BEFORE the transport triad: a genuine
        // cancellation is not a truncation and must propagate, never be swallowed as a
        // graceful stop.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        // Two hits: the fake cancels the source right after yielding the first, so the
        // second iteration's ThrowIfCancellationRequested observes it before yielding.
        var client = new ScriptedStreamClient([ValidHit("a"), ValidHit("b")], fault: null, cts);
        var sut = Sut(client);

        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in sut.StreamChangesAsync(FakeNow, cts.Token))
            {
                // The fake cancels the source after the first yield; the next MoveNextAsync
                // observes cancellation.
            }
        });
    }

    [Fact]
    public async Task StreamChanges_TransportTruncation_EmitsTheTruncationWarning()
    {
        // Unlike the snapshot path, the stream has NO outcome-recorder — the EventId 5010 warning
        // is the ONLY observable that distinguishes a swallowed truncation from a clean stream end
        // (both are a silent yield break). Pin it, or a mutation that drops the log call but keeps
        // the graceful stop stays green. (Sibling: JobTechStreamResilienceTests pins 5011/5012 for
        // the same "the only observability must be pinned" reason.)
        var ct = TestContext.Current.CancellationToken;
        var logger = new CapturingLogger<PlatsbankenJobSource>();
        var client = new ScriptedStreamClient([ValidHit("a"), ValidHit("b")], new IOException("cut"));
        var sut = Sut(client, logger);

        await foreach (var _ in sut.StreamChangesAsync(FakeNow, ct)) { }

        logger.EventIds.ShouldContain(TruncationEventId);
    }

    [Fact]
    public async Task StreamChanges_CleanCompletion_DoesNotEmitTheTruncationWarning()
    {
        // Counterfactual: 5010 must fire ONLY on truncation, never on a clean stream end — else the
        // pin above would pass even if the warning leaked onto the happy path.
        var ct = TestContext.Current.CancellationToken;
        var logger = new CapturingLogger<PlatsbankenJobSource>();
        var client = new ScriptedStreamClient([ValidHit("a"), ValidHit("b")], fault: null);
        var sut = Sut(client, logger);

        await foreach (var _ in sut.StreamChangesAsync(FakeNow, ct)) { }

        logger.EventIds.ShouldNotContain(TruncationEventId);
    }

    /// <summary>Minimal capturing logger — records the EventId of every log call.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<int> EventIds { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => EventIds.Add(eventId.Id);
    }

    /// <summary>
    /// Stream-only fake: yields the scripted hits then, if <c>fault</c> is set, throws it at
    /// the enumeration boundary (simulating a mid-array wire error). When a
    /// <see cref="CancellationTokenSource"/> is supplied it is cancelled after the first
    /// yield, so the following <c>MoveNextAsync</c> observes cancellation.
    /// </summary>
    private sealed class ScriptedStreamClient(
        JobTechHit[] hits,
        Exception? fault,
        CancellationTokenSource? cancelAfterFirst = null) : IJobTechStreamClient
    {
        public IAsyncEnumerable<JobTechHit> FetchSnapshotAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException("Stream-only fake.");

        public async IAsyncEnumerable<JobTechHit> StreamChangesAsync(
            DateTimeOffset since,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var index = 0;
            foreach (var hit in hits)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return hit;
                index++;
                if (index == 1)
                    cancelAfterFirst?.Cancel();
            }

            await Task.CompletedTask;
            if (fault is not null)
                throw fault;
        }
    }

    private sealed class EmptySearchClient : IJobTechSearchClient
    {
        public Task<JobTechHit?> GetAdByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult<JobTechHit?>(null);

        public Task<JobTechSearchListResponse> SearchRemoteAsync(
            int offset, int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult(new JobTechSearchListResponse
            {
                Total = new JobTechSearchTotal { Value = 0 },
                Hits = [],
            });
    }
}

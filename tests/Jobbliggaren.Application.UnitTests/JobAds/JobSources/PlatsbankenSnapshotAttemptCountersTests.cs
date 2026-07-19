using System.Runtime.CompilerServices;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Infrastructure.JobSources.Platsbanken;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.JobSources;

/// <summary>
/// #510 — retry counter inflation. <c>converted</c>/<c>total</c> used to be
/// initialised OUTSIDE the bounded-retry attempt loop while every retry
/// re-streams from element 0, so a truncate-then-succeed run reported
/// <c>ParsedTotal</c> as the CROSS-ATTEMPT SUM (attempt 1's partial + attempt
/// 2's full). That inflated value poisoned the 7-day
/// <c>MAX(ParsedTotal)</c>-baseline: the next healthy run compared its true
/// count against 0.80 × the inflated max, tripped the relative floor, and
/// miss-tracking (stale-ad archiving) was suppressed for up to 7 days.
/// The scripted fake differentiates attempts — WireMock cannot (it returns the
/// same body every GET), which is why this pin lives here and not in the
/// resilience tests.
/// </summary>
public class PlatsbankenSnapshotAttemptCountersTests
{
    private static readonly DateTimeOffset FakeNow = new(2026, 7, 19, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Published = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

    private static JobTechHit ValidHit(string id) => new()
    {
        Id = id,
        Headline = "Backend-utvecklare",
        Description = new JobTechDescription { Text = "Beskrivning av tjänsten." },
        Employer = new JobTechEmployer { Name = "Test Company AB" },
        WebpageUrl = "https://arbetsformedlingen.se/platsbanken/annonser/" + id,
        PublicationDate = Published,
    };

    [Fact]
    public async Task FetchSnapshot_TruncatedThenSuccessfulAttempt_ReportsOnlyTheFinalAttemptsCounts()
    {
        var ct = TestContext.Current.CancellationToken;
        // Attempt 1: streams 2 hits then breaks mid-stream (transport IOException).
        // Attempt 2: streams all 3 hits and completes.
        var client = new ScriptedStreamClient(
            [new AttemptScript([ValidHit("a"), ValidHit("b")], ThrowAtEnd: true),
             new AttemptScript([ValidHit("a"), ValidHit("b"), ValidHit("c")], ThrowAtEnd: false)]);
        var sut = new PlatsbankenJobSource(
            client, new EmptySearchClient(), new FakeDateTimeProvider(FakeNow),
            NullLogger<PlatsbankenJobSource>.Instance);
        var recorder = new SnapshotOutcomeRecorder();

        var yielded = new List<string>();
        await foreach (var item in sut.FetchSnapshotAsync(recorder, ct))
            yielded.Add(item.ExternalId);

        recorder.Outcome.ShouldNotBeNull();
        recorder.Outcome.TruncatedAndExhausted.ShouldBeFalse();
        recorder.Outcome.Attempts.ShouldBe(2);

        // THE #510 PIN: ParsedTotal is the FINAL attempt's element count (3), never
        // the cross-attempt sum (5) that inflated the 7-day baseline.
        recorder.Outcome.ParsedTotal.ShouldBe(3);

        // Yield semantics are UNCHANGED by the reset: already-yielded items from the
        // truncated attempt are re-yielded (idempotent duplicates via UNIQUE index).
        yielded.ShouldBe(["a", "b", "a", "b", "c"]);
    }

    [Fact]
    public async Task FetchSnapshot_AllAttemptsTruncated_ReportsOnlyTheLastAttemptsPartialCount()
    {
        var ct = TestContext.Current.CancellationToken;
        // All 3 attempts truncate: 2, 2 and then 1 parsed element. The recorded
        // ParsedTotal must be the LAST attempt's partial (1), not the sum (5) —
        // post-reset a truncated run can only DEFLATE the baseline (MAX
        // self-corrects to the largest healthy run), never inflate it ~3×.
        var client = new ScriptedStreamClient(
            [new AttemptScript([ValidHit("a"), ValidHit("b")], ThrowAtEnd: true),
             new AttemptScript([ValidHit("a"), ValidHit("b")], ThrowAtEnd: true),
             new AttemptScript([ValidHit("a")], ThrowAtEnd: true)]);
        var sut = new PlatsbankenJobSource(
            client, new EmptySearchClient(), new FakeDateTimeProvider(FakeNow),
            NullLogger<PlatsbankenJobSource>.Instance);
        var recorder = new SnapshotOutcomeRecorder();

        await foreach (var _ in sut.FetchSnapshotAsync(recorder, ct))
        {
            // Consume the stream; yields are pinned in the test above.
        }

        recorder.Outcome.ShouldNotBeNull();
        recorder.Outcome.TruncatedAndExhausted.ShouldBeTrue();
        recorder.Outcome.Attempts.ShouldBe(3);
        recorder.Outcome.ParsedTotal.ShouldBe(1);
    }

    private sealed record AttemptScript(JobTechHit[] Hits, bool ThrowAtEnd);

    /// <summary>
    /// Attempt-aware fake: each <see cref="FetchSnapshotAsync"/> call consumes the
    /// next script; <c>ThrowAtEnd</c> simulates mid-stream transport truncation
    /// (IOException at the enumeration boundary, the class the bounded retry owns).
    /// </summary>
    private sealed class ScriptedStreamClient(IReadOnlyList<AttemptScript> scripts) : IJobTechStreamClient
    {
        private int _calls;

        public IAsyncEnumerable<JobTechHit> FetchSnapshotAsync(CancellationToken cancellationToken)
        {
            var script = scripts[Math.Min(_calls, scripts.Count - 1)];
            _calls++;
            return Play(script, cancellationToken);
        }

        public IAsyncEnumerable<JobTechHit> StreamChangesAsync(
            DateTimeOffset since, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Snapshot-only fake.");

        private static async IAsyncEnumerable<JobTechHit> Play(
            AttemptScript script,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var hit in script.Hits)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return hit;
            }

            await Task.CompletedTask;
            if (script.ThrowAtEnd)
                throw new IOException("mid-stream truncation (simulated)");
        }
    }

    private sealed class EmptySearchClient : IJobTechSearchClient
    {
        public Task<JobTechHit?> GetAdByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult<JobTechHit?>(null);

        // Empty harvest → PlatsbankenJobSource treats it as anomaly → null set →
        // Remote=null on every item. Irrelevant to the counter pins here.
        public Task<JobTechSearchListResponse> SearchRemoteAsync(
            int offset, int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult(new JobTechSearchListResponse
            {
                Total = new JobTechSearchTotal { Value = 0 },
                Hits = [],
            });
    }
}

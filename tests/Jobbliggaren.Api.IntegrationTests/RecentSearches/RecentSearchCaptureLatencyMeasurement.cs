using System.Diagnostics;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.RecentJobSearches;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.RecentSearches;

/// <summary>
/// #2285 (dotnet-architect, #2285 plan read, point 8) — the store half of the capture command against ADR 0045 class (c),
/// command/write: <b>p95 400 ms</b>. The count half scales with the corpus and is measured on 40 000 ads in
/// <see cref="JobAds.CaptureCountLatencyMeasurement"/>; this half touches only the user's own rows, at most
/// <see cref="RecentJobSearch.MaxPerSeeker"/>, so it is measured on its three paths through the real capturer.
/// <para>
/// ⚠ <b>OBSERVE-ONLY</b> (ADR 0045): durations are written to test output and read into the PR body; what is asserted is
/// that each path ran as named.
/// </para>
/// </summary>
[Collection(SharedPostgresFixtureGroup.Name)]
[Trait("Category", "SmokeTest")]
public sealed class RecentSearchCaptureLatencyMeasurement(SharedPostgresFixture postgres, ITestOutputHelper output)
    : IAsyncLifetime
{
    private const int Iterations = 40;
    private readonly RecentSearchQuickLinkHarness _h = new();

    public ValueTask InitializeAsync() => _h.InitializeAsync(postgres);

    public ValueTask DisposeAsync() => _h.DisposeAsync(postgres);

    [Fact]
    public async Task TheStore_OnItsThreePaths_AgainstAdr0045ClassC()
    {
        // One untimed call first, so the first timed one pays no cold start. Then a new row while the list has room, a
        // new row against a full list (it evicts the oldest), and a bump.
        await _h.CaptureAsync("varm");
        var insert = await TimeAsync(RecentJobSearch.MaxPerSeeker - 2, i => $"ny{i:D3}");
        (await _h.RowsAsync()).Count.ShouldBe(RecentJobSearch.MaxPerSeeker - 1);

        await _h.CaptureAsync("fyller");
        var evict = await TimeAsync(Iterations, i => $"evict{i:D3}");
        (await _h.RowsAsync()).Count.ShouldBe(RecentJobSearch.MaxPerSeeker, "every insert against a full list evicted one row");

        var bump = await TimeAsync(Iterations, _ => "evict000");
        (await _h.RowsAsync()).Count.ShouldBe(RecentJobSearch.MaxPerSeeker, "a bump adds no row");

        output.WriteLine($"ADR 0045 class (c): p95 400 ms. The capturer alone, one scope per call, {Iterations} timed calls.");
        output.WriteLine($"insert, list has room ({insert.Count} calls)   p50 {P(insert, 0.50),6:F1} ms  p95 {P(insert, 0.95),6:F1} ms");
        output.WriteLine($"insert against a full list (evicts)  p50 {P(evict, 0.50),6:F1} ms  p95 {P(evict, 0.95),6:F1} ms");
        output.WriteLine($"bump of an existing row              p50 {P(bump, 0.50),6:F1} ms  p95 {P(bump, 0.95),6:F1} ms");
    }

    private async Task<List<double>> TimeAsync(int calls, Func<int, string> q)
    {
        var samples = new List<double>(calls);
        for (var i = 0; i < calls; i++)
        {
            _h.Clock.UtcNow = _h.Clock.UtcNow.AddMinutes(1);
            var sw = Stopwatch.StartNew();
            await _h.CaptureAsync(q(i));
            sw.Stop();
            samples.Add(sw.Elapsed.TotalMilliseconds);
        }

        samples.Sort();
        return samples;
    }

    private static double P(List<double> sorted, double p) => LatencyMeasurementTools.Percentile(sorted, p);
}

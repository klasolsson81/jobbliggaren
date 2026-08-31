using System.Diagnostics;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree;
using Jobbliggaren.Application.JobAds.Queries.SuggestJobAdTerms;
using Jobbliggaren.Infrastructure.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #1546 — the before/after latency measurement the CTO bind (F2) and <c>security-auditor</c>'s Major 1
/// both require for the employer branch, against ADR 0045's budget.
///
/// <para>
/// <b>The budget is Klas-locked, not this class's to choose.</b> ADR 0045 Beslut 1 class (b)
/// typeahead/suggest = <b>p95 150 ms</b> (p99 300 ms, observe-only), locked at the Accepted-flip
/// 2026-05-17 because it carries product, UX and cost consequence. The measurement point is
/// server-side HANDLER latency — exactly what <c>LoggingBehavior</c> instruments in production — not
/// edge-to-edge. If the branch does not fit, the remedy is the query or the branch, never the number.
/// </para>
///
/// <para>
/// <b>How before/after is isolated.</b> "After" is the real port. "Before" is the SAME handler with a
/// port that returns nothing without touching the database — i.e. the pre-#1546 shape: taxonomy from
/// the in-process snapshot plus one title query. Same term, same title selectivity, same taxonomy, so
/// the difference between the two is the employer round-trip and nothing else. Comparing a 2-character
/// term against a 3-character one would have moved title selectivity at the same time and measured a
/// blend.
/// </para>
///
/// <para>
/// <b>Why the dedicated container (#1013), not <c>ApiFactory</c>'s.</b> A timing number needs a table
/// whose size and statistics the measurement owns. The shared <c>[Collection("Api")]</c> Postgres is
/// seeded by dozens of classes in execution-dependent order and never truncated, so any number taken
/// there describes the run order, not the query.
/// </para>
///
/// <para>
/// ⚠ <b>OBSERVE-ONLY: this class asserts BEHAVIOUR, never a duration.</b> ADR 0045 keeps fitness
/// functions observe-only until an explicit Klas ratchet, and the sibling fixture's own docblock
/// carries the reason in the ADR's words — <i>"a flaky perf-gate is worse than no perf-gate"</i>. A
/// wall-clock assertion inside a container on a shared developer machine is exactly that. The
/// durations are written to test output and read by a human into the PR body; what IS asserted is that
/// the employer branch actually ran, so the numbers can never describe a code path that did not
/// execute.
/// </para>
///
/// <para>
/// <b>Result, measured 2026-08-31 against this class at 50 000 ads.</b> A dated reading of a finished
/// run, not a live number: re-run this class to regenerate it.
/// <code>
/// term                 before p95    after p95    delta p95
/// "nordiska"  (8 ch)      1.4 ms      53.4 ms      +52.0 ms   every seeded employer matches
/// "nordiska bygg 417"     1.1 ms      10.0 ms       +8.9 ms   one entity matches
/// "no"        (2 ch)      1.0 ms       1.1 ms        ~0 ms    gate closed
/// </code>
/// The first row is deliberately pathological — the seed puts the term in EVERY company name, so all
/// 50 000 rows reach the GROUP BY. Even there the composed handler sits at roughly a third of the
/// 150 ms budget, and the ordinary case costs about 9 ms. The two-character row is the gate's claim
/// discharged: within noise of the branch not existing.
/// </para>
/// <para>
/// <b>Scale, stated precisely rather than flatteringly.</b> 50 000 rows is this fixture's established
/// regime and is near the box's ~58 800 ads; the dev database carries ~106 000. This measurement
/// therefore does NOT cover dev scale directly. The cost of the branch grows with the number of
/// matching rows, and the worst case above already matches 100 % of them, so doubling the corpus
/// stays inside the budget on this evidence — but that is an inference, not a reading, and it is
/// written as one.
/// </para>
/// </summary>
[Collection("JobAdBrowsePlan")]
public class SuggestUnionLatencyMeasurement(JobAdBrowsePlanFixture fixture, ITestOutputHelper output)
{
    private readonly JobAdBrowsePlanFixture _fixture = fixture;
    private readonly ITestOutputHelper _output = output;
    private void Say(string line) => _output.WriteLine(line);

    private const int TotalRows = 50_000;
    private const int Iterations = 50;

    /// <summary>Matches every seeded employer — the "ab" / "kommun" worst case the budget exists for.</summary>
    private const string HighCardinalityTerm = "nordiska";

    /// <summary>Matches one entity — the ordinary case a user actually types.</summary>
    private const string SelectiveTerm = "nordiska bygg 417";

    [Fact]
    public async Task EmployerBranch_LatencyDelta_AgainstAdr0045ClassB()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = _fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await SeedProductionRegimeAsync(db, ct);

        var withEmployers = new SuggestJobAdTermsQueryHandler(
            db, EmptyTaxonomy(), new EmployerDisambiguationQuery(db));
        var withoutEmployers = new SuggestJobAdTermsQueryHandler(
            db, EmptyTaxonomy(), NoEmployers());

        // Non-vacuity FIRST: if the branch returns nothing, every duration below describes a code path
        // that did not run, and the whole measurement is a number about nothing.
        var probe = await withEmployers.Handle(
            new SuggestJobAdTermsQuery(HighCardinalityTerm, 10), ct);
        probe.Count(s => s.Kind == SuggestionKind.Employer).ShouldBe(3,
            "the employer branch must actually execute and fill its budget, or the timings below "
            + "measure the absence of the thing under measurement.");

        Say($"ADR 0045 class (b) budget: p95 150 ms (Klas-locked 2026-05-17). "
            + $"Corpus {TotalRows:N0} ads, {Iterations} warmed iterations each.");
        Say("");

        foreach (var term in new[] { HighCardinalityTerm, SelectiveTerm })
        {
            var before = await MeasureAsync(withoutEmployers, term, ct);
            var after = await MeasureAsync(withEmployers, term, ct);

            Say($"term \"{term}\" ({term.Length} chars)");
            Say($"  before (no employer branch): p50 {before.P50:F1} ms  p95 {before.P95:F1} ms");
            Say($"  after  (employer branch on): p50 {after.P50:F1} ms  p95 {after.P95:F1} ms");
            Say($"  delta                      : p50 {after.P50 - before.P50:F1} ms  p95 {after.P95 - before.P95:F1} ms");
            Say("");
        }

        // The >= 3 gate's whole claim: below it the branch costs nothing at all. Measured on the same
        // handler, so a regression that fires the branch early would show up here as a non-zero delta.
        var shortBefore = await MeasureAsync(withoutEmployers, "no", ct);
        var shortAfter = await MeasureAsync(withEmployers, "no", ct);
        Say("term \"no\" (2 chars — the >= 3 gate is CLOSED)");
        Say($"  before: p50 {shortBefore.P50:F1} ms  p95 {shortBefore.P95:F1} ms");
        Say($"  after : p50 {shortAfter.P50:F1} ms  p95 {shortAfter.P95:F1} ms");

        (await withEmployers.Handle(new SuggestJobAdTermsQuery("no", 10), ct))
            .ShouldAllBe(s => s.Kind != SuggestionKind.Employer,
                "below the trigram-servable length the employer branch must not run at all — that is "
                + "the gate's entire claim, and it is what makes its delta above meaningful.");
    }

    private static async Task<(double P50, double P95)> MeasureAsync(
        SuggestJobAdTermsQueryHandler handler, string term, CancellationToken ct)
    {
        // Warm-up: first-call costs (plan cache, connection, EF model) are not what production pays on
        // a keystroke, and including them would make the two arms differ by their warm-ups.
        for (var i = 0; i < 5; i++)
            await handler.Handle(new SuggestJobAdTermsQuery(term, 10), ct);

        var samples = new List<double>(Iterations);
        for (var i = 0; i < Iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            await handler.Handle(new SuggestJobAdTermsQuery(term, 10), ct);
            sw.Stop();
            samples.Add(sw.Elapsed.TotalMilliseconds);
        }

        samples.Sort();
        return (Percentile(samples, 0.50), Percentile(samples, 0.95));
    }

    private static double Percentile(List<double> sorted, double p)
    {
        var index = (int)Math.Ceiling(p * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }

#pragma warning disable CA2012 // ValueTask-stub konsumeras av NSubstitute (jfr SuggestJobAdTermsUnionTests)
    private static ITaxonomyReadModel EmptyTaxonomy()
    {
        var taxonomy = Substitute.For<ITaxonomyReadModel>();
        taxonomy.SuggestByPrefixAsync(
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<TaxonomySuggestionDto>>(
                (IReadOnlyList<TaxonomySuggestionDto>)[]));
        return taxonomy;
    }

    /// <summary>
    /// The "before" arm: the port contract, answering without touching the database. Not a null object
    /// standing in for production behaviour — it stands in for the ABSENCE of this branch, which is
    /// precisely the state before #1546.
    /// </summary>
    private static IEmployerDisambiguationQuery NoEmployers()
    {
        var port = Substitute.For<IEmployerDisambiguationQuery>();
        port.SuggestActiveEmployersAsync(
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<EmployerAdGroup>>(
                (IReadOnlyList<EmployerAdGroup>)[]));
        return port;
    }
#pragma warning restore CA2012

    /// <summary>
    /// TRUNCATE-and-own, bulk-seed a production-scale regime, then ANALYZE. The ANALYZE is MANDATORY:
    /// TRUNCATE wipes the statistics, and without them the planner falls back on default selectivity
    /// constants — which would make the timing describe a plan production never runs.
    /// </summary>
    private static async Task SeedProductionRegimeAsync(AppDbContext db, CancellationToken ct)
    {
        db.Database.SetCommandTimeout(300);
        await db.Database.ExecuteSqlRawAsync("TRUNCATE job_ads;", ct);

        // Every company name carries the high-cardinality token, so the worst case is real rather than
        // hypothetical: one fragment, ~50 000 rows to group. Titles carry a DIFFERENT token so the
        // title branch's selectivity does not move between the two arms.
        var bulk =
            "INSERT INTO job_ads "
            + "(id, title, description, url, published_at, created_at, status, source, company_name, "
            + "organization_number, remote) "
            + "SELECT gen_random_uuid(), "
            + "'Systemutvecklare ' || i, "
            + "'Beskrivning ' || i, "
            + "'https://example.com/jobb/' || i, "
            + "now() - (((i * 7919) % " + TotalRows + ") || ' minutes')::interval, "
            + "now(), "
            + "CASE WHEN (i * 7919) % 5 = 0 THEN 'Archived' ELSE 'Active' END, "
            + "'Platsbanken', "
            + "'Nordiska Bygg ' || i || ' AB', "
            + "'55' || lpad(((i * 7919) % 100000000)::text, 8, '0'), "
            + "false "
            + "FROM generate_series(0, " + (TotalRows - 1) + ") AS i;";
        await db.Database.ExecuteSqlRawAsync(bulk, ct);

        await db.Database.ExecuteSqlRawAsync("ANALYZE job_ads;", ct);
    }
}

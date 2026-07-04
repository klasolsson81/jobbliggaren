using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Infrastructure.CompanyRegister.Scb;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyRegister;

/// <summary>
/// #560 (ADR 0091) — unit tests for the count-then-slice partition planner. The load-bearing invariant
/// is "every yielded leaf is ≤ the SCB fetch cap" (SCB has no pagination), pinned here with a fake
/// count function — no HTTP, no cert, no DB.
/// </summary>
public class ScbPartitionPlannerTests
{
    private const string Kommun = "SätesKommun";
    private const string Form = "Juridisk form";
    private const string Bransch = "Bransch";
    private const int Cap = 2000;

    // Canonical signature of a query's constraints (BranschNiva ignored — only category+codes matter
    // to the fake distribution).
    private static string Sig(ScbQuery q) => string.Join("|",
        q.Filters.OrderBy(f => f.Category, StringComparer.Ordinal)
            .Select(f => $"{f.Category}={string.Join(",", f.Codes)}"));

    private static Func<ScbQuery, CancellationToken, Task<int>> Counts(Dictionary<string, int> table) =>
        (q, _) => Task.FromResult(table.GetValueOrDefault(Sig(q), 0));

    private static async Task<List<ScbLeaf>> CollectAsync(
        IReadOnlyList<ScbQuery> seeds, IReadOnlyList<ScbFacet> ladder,
        Func<ScbQuery, CancellationToken, Task<int>> countAsync, ScbSyncOutcome outcome)
    {
        var leaves = new List<ScbLeaf>();
        await foreach (var leaf in ScbPartitionPlanner.PlanAsync(
            seeds, ladder, Cap, countAsync, outcome, TestContext.Current.CancellationToken))
        {
            leaves.Add(leaf);
        }
        return leaves;
    }

    [Fact]
    public async Task PlanAsync_YieldsSeedDirectly_WhenUnderCap()
    {
        var seed = new ScbQuery([new ScbCategoryFilter(Kommun, ["2403"])]);
        var outcome = new ScbSyncOutcome();

        var leaves = await CollectAsync([seed], [], Counts(new() { [Sig(seed)] = 133 }), outcome);

        var leaf = leaves.ShouldHaveSingleItem();
        leaf.Count.ShouldBe(133);
        outcome.PartitionsCounted.ShouldBe(1);
        outcome.TruncatedOrErrored.ShouldBeFalse();
    }

    [Fact]
    public async Task PlanAsync_VisitsEverySeed_YieldsLeafPerNonZeroSeed()
    {
        // Production seeds ~290 municipalities; pin that every seed is visited and a non-zero seed
        // yields a leaf (the reverse-push order is cosmetic, but coverage must not rest on a single seed).
        var s1 = new ScbQuery([new ScbCategoryFilter(Kommun, ["0180"])]);
        var s2 = new ScbQuery([new ScbCategoryFilter(Kommun, ["1480"])]);
        var s3 = new ScbQuery([new ScbCategoryFilter(Kommun, ["9999"])]); // 0 → skipped
        var outcome = new ScbSyncOutcome();

        var leaves = await CollectAsync(
            [s1, s2, s3], [], Counts(new() { [Sig(s1)] = 100, [Sig(s2)] = 200 }), outcome);

        leaves.Select(l => l.Count).OrderBy(c => c).ShouldBe([100, 200]);
        outcome.PartitionsCounted.ShouldBe(3); // all three seeds counted
    }

    [Fact]
    public async Task PlanAsync_SkipsZeroCountPartitions_NoFetch()
    {
        var seed = new ScbQuery([new ScbCategoryFilter(Kommun, ["9999"])]);
        var outcome = new ScbSyncOutcome();

        var leaves = await CollectAsync([seed], [], Counts([]), outcome); // empty table → count 0

        leaves.ShouldBeEmpty();
        outcome.PartitionsCounted.ShouldBe(1);
    }

    [Fact]
    public async Task PlanAsync_SplitsOverCapPartitionDownTheLadder_EveryLeafUnderCap()
    {
        // Seed (Stockholm × all legal forms) is over cap; the Juridisk-form rung brings 51 under cap
        // but 49 is still over, so the Bransch rung splits 49 by SNI section — every leaf ends ≤ cap.
        var seed = new ScbQuery([
            new ScbCategoryFilter(Kommun, ["0180"]),
            new ScbCategoryFilter(Form, ["49", "51"]),
        ]);
        var ladder = new ScbFacet[]
        {
            new(Form, ["49", "51"]),
            new(Bransch, ["A", "B"], BranschNiva: 1),
        };
        var table = new Dictionary<string, int>
        {
            [$"{Form}=49,51|{Kommun}=0180"] = 5000,   // seed → over cap
            [$"{Form}=49|{Kommun}=0180"] = 3000,      // form 49 → still over cap
            [$"{Form}=51|{Kommun}=0180"] = 1500,      // form 51 → leaf
            [$"{Bransch}=A|{Form}=49|{Kommun}=0180"] = 1200, // 49 × A → leaf
            [$"{Bransch}=B|{Form}=49|{Kommun}=0180"] = 800,  // 49 × B → leaf
        };
        var outcome = new ScbSyncOutcome();

        var leaves = await CollectAsync([seed], ladder, Counts(table), outcome);

        leaves.Count.ShouldBe(3);
        leaves.ShouldAllBe(l => l.Count <= Cap);          // THE invariant
        outcome.TruncatedOrErrored.ShouldBeFalse();
        outcome.PartitionsCounted.ShouldBe(5);            // seed + 2 forms + 2 bransch
    }

    [Fact]
    public async Task PlanAsync_MarksTruncated_WhenLadderExhaustedAndStillOverCap()
    {
        // A monster partition that stays over cap even after the last rung — the planner fetches it
        // (client caps the rows) but latches truncated so the caller SKIPS the deregister sweep.
        var seed = new ScbQuery([
            new ScbCategoryFilter(Kommun, ["0180"]),
            new ScbCategoryFilter(Form, ["49"]),
        ]);
        var ladder = new ScbFacet[] { new(Bransch, ["A"], BranschNiva: 1) };
        var table = new Dictionary<string, int>
        {
            [$"{Form}=49|{Kommun}=0180"] = 5000,
            [$"{Bransch}=A|{Form}=49|{Kommun}=0180"] = 3000, // still over cap, ladder exhausted
        };
        var outcome = new ScbSyncOutcome();

        var leaves = await CollectAsync([seed], ladder, Counts(table), outcome);

        leaves.ShouldContain(l => l.Count == 3000);
        outcome.TruncatedOrErrored.ShouldBeTrue();
    }
}

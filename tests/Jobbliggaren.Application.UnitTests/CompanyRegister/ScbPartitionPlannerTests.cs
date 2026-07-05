using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Infrastructure.CompanyRegister.Scb;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyRegister;

/// <summary>
/// #560 / #628 (ADR 0091) — unit tests for the count-then-slice partition planner and its rungs. The
/// load-bearing invariant is "every yielded leaf is ≤ the SCB fetch cap" (SCB has no pagination), pinned
/// here with a fake count function — no HTTP, no cert, no DB. #628 deepens the ladder to a dynamic SNI
/// drill-down (Juridisk form → 2-digit division → 5-digit Bransch fanned by the parent's 2-digit prefix).
/// </summary>
public class ScbPartitionPlannerTests
{
    private const string Kommun = "SätesKommun";
    private const string Form = "Juridisk form";
    private const string TwoDigit = "2-siffrig bransch 1";
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
        IReadOnlyList<ScbQuery> seeds, IReadOnlyList<IScbRung> ladder,
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
    public async Task PlanAsync_DrillsFullSniLadderToUnderCap_EveryLeafUnderCap_NotTruncated()
    {
        // The #628 win: a metro partition over cap after the legal-form rung is split by 2-digit SNI
        // division, then by 5-digit Bransch (fanned by the 2-digit prefix), until every leaf is ≤ cap —
        // and the run is NOT marked truncated (so the deregister sweep can apply on a clean run).
        var seed = new ScbQuery([
            new ScbCategoryFilter(Kommun, ["0180"]),
            new ScbCategoryFilter(Form, ["49"]),
        ]);
        var ladder = new IScbRung[]
        {
            new ScbStaticRung(Form, ["49"]),
            new ScbStaticRung(TwoDigit, ["70"]),
            new ScbPrefixRung(TwoDigit, Bransch, ChildBranschNiva: 3,
                new Dictionary<string, IReadOnlyList<string>> { ["70"] = ["70100", "70200"] }),
        };
        var table = new Dictionary<string, int>
        {
            [$"{Form}=49|{Kommun}=0180"] = 5000,                     // seed + form-split → over cap
            [$"{TwoDigit}=70|{Form}=49|{Kommun}=0180"] = 3000,       // 2-digit 70 → still over cap
            [$"{Bransch}=70100|{Form}=49|{Kommun}=0180"] = 1500,     // 5-digit 70100 → leaf (2-digit stripped)
            [$"{Bransch}=70200|{Form}=49|{Kommun}=0180"] = 900,      // 5-digit 70200 → leaf
        };
        var outcome = new ScbSyncOutcome();

        var leaves = await CollectAsync([seed], ladder, Counts(table), outcome);

        leaves.Select(l => l.Count).OrderBy(c => c).ShouldBe([900, 1500]);
        leaves.ShouldAllBe(l => l.Count <= Cap);              // THE invariant
        outcome.TruncatedOrErrored.ShouldBeFalse();           // clean run → sweep may apply
        outcome.PartitionsCounted.ShouldBe(5);                // seed + form + 2-digit + 2×5-digit
    }

    [Fact]
    public async Task PlanAsync_MarksTruncated_WhenDeepestSniLeafStillOverCap()
    {
        // The dense-metro tail (e.g. Stockholm 0180 × form 49 × SNI 70100 = 2809 > 2000): even the
        // 5-digit rung leaves it over cap and there is no finer SNI level, so the planner fetches what
        // it can (client caps the rows) but latches truncated → the caller SKIPS the deregister sweep.
        var seed = new ScbQuery([
            new ScbCategoryFilter(Kommun, ["0180"]),
            new ScbCategoryFilter(Form, ["49"]),
        ]);
        var ladder = new IScbRung[]
        {
            new ScbStaticRung(Form, ["49"]),
            new ScbStaticRung(TwoDigit, ["70"]),
            new ScbPrefixRung(TwoDigit, Bransch, ChildBranschNiva: 3,
                new Dictionary<string, IReadOnlyList<string>> { ["70"] = ["70100"] }),
        };
        var table = new Dictionary<string, int>
        {
            [$"{Form}=49|{Kommun}=0180"] = 5000,
            [$"{TwoDigit}=70|{Form}=49|{Kommun}=0180"] = 3000,
            [$"{Bransch}=70100|{Form}=49|{Kommun}=0180"] = 2809, // deepest rung, still over cap
        };
        var outcome = new ScbSyncOutcome();

        var leaves = await CollectAsync([seed], ladder, Counts(table), outcome);

        leaves.ShouldContain(l => l.Count == 2809);
        outcome.TruncatedOrErrored.ShouldBeTrue();
    }

    [Fact]
    public async Task PlanAsync_MarksTruncated_WhenPrefixRungHasNoChildrenForParentCode()
    {
        // Defensive: a prefix rung whose map has no entry for the parent's 2-digit code cannot split →
        // Expand returns empty → same safe semantics as an exhausted ladder (fetch what we can, latch
        // truncated). Shouldn't happen once the map is derived from the same niva-3 table, but the
        // over-cap partition must never vanish silently.
        var seed = new ScbQuery([
            new ScbCategoryFilter(Kommun, ["0180"]),
            new ScbCategoryFilter(TwoDigit, ["99"]),
        ]);
        var ladder = new IScbRung[]
        {
            new ScbPrefixRung(TwoDigit, Bransch, ChildBranschNiva: 3,
                new Dictionary<string, IReadOnlyList<string>> { ["70"] = ["70100"] }),
        };
        var table = new Dictionary<string, int>
        {
            [$"{TwoDigit}=99|{Kommun}=0180"] = 4000, // over cap, but no children for "99"
        };
        var outcome = new ScbSyncOutcome();

        var leaves = await CollectAsync([seed], ladder, Counts(table), outcome);

        leaves.ShouldContain(l => l.Count == 4000);
        outcome.TruncatedOrErrored.ShouldBeTrue();
    }

    [Fact]
    public void ScbPrefixRung_Expand_FansOnlyPrefixMatchingChildren_DropsTwoDigit_CarriesNiva()
    {
        // The dynamic child facet: for a parent on 2-digit "70", fan ONLY the 5-digit codes under 70
        // (never 41000), drop the now-subsumed 2-digit constraint, and carry BranschNiva 3.
        var rung = new ScbPrefixRung(TwoDigit, Bransch, ChildBranschNiva: 3,
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["70"] = ["70100", "70200"],
                ["41"] = ["41000"],
            });
        var parent = new ScbQuery([
            new ScbCategoryFilter(Kommun, ["0180"]),
            new ScbCategoryFilter(Form, ["49"]),
            new ScbCategoryFilter(TwoDigit, ["70"]),
        ]);

        var children = rung.Expand(parent);

        children.Count.ShouldBe(2);
        foreach (var child in children)
        {
            child.Filters.ShouldNotContain(f => f.Category == TwoDigit);            // 2-digit stripped
            child.Filters.ShouldContain(f => f.Category == Kommun && f.Codes[0] == "0180");
            child.Filters.ShouldContain(f => f.Category == Form && f.Codes[0] == "49");
            var bransch = child.Filters.Single(f => f.Category == Bransch);
            bransch.BranschNiva.ShouldBe(3);
            bransch.Codes.Count.ShouldBe(1);
        }
        children.SelectMany(c => c.Filters.Where(f => f.Category == Bransch).SelectMany(f => f.Codes))
            .OrderBy(c => c, StringComparer.Ordinal)
            .ShouldBe(["70100", "70200"]);                                          // never 41000
    }

    [Theory]
    [InlineData(0)] // parent has no 2-digit constraint at all
    [InlineData(2)] // parent has two 2-digit codes (never happens in a leaf-bound partition)
    public void ScbPrefixRung_Expand_ReturnsEmpty_WhenParentTwoDigitIsNotASingleCode(int codeCount)
    {
        var rung = new ScbPrefixRung(TwoDigit, Bransch, ChildBranschNiva: 3,
            new Dictionary<string, IReadOnlyList<string>> { ["70"] = ["70100"] });
        var filters = new List<ScbCategoryFilter> { new(Kommun, ["0180"]) };
        if (codeCount > 0)
            filters.Add(new ScbCategoryFilter(TwoDigit,
                [.. Enumerable.Range(70, codeCount).Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture))]));

        rung.Expand(new ScbQuery(filters)).ShouldBeEmpty();
    }
}

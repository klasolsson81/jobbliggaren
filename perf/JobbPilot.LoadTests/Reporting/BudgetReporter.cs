// Budget-rapport mot ADR 0045 Beslut 1-budgetar.
//
// Observe-only Fas 1 (ADR 0045 Beslut 5):
//   - p95 över budget → GitHub ::warning::-annotation
//   - exit-kod ALDRIG non-zero (det styrs av Program.cs sista `return 0;`)
//   - JSON-trend-artefakt skrivs till artifacts/perf/ (CI laddar upp)
//
// Flip till blockerande gate = medveten Klas-GO-ratchet (ADR 0045 Beslut 6),
// aldrig från denna fil.

using System.Text.Json;
using NBomber.Contracts.Stats;

namespace JobbPilot.LoadTests.Reporting;

internal sealed record BudgetCheck(
    string Scenario,
    int BudgetMs,
    double MeasuredP95Ms,
    double MeasuredP99Ms,
    double MeasuredMeanMs,
    long OkCount,
    long FailCount,
    bool Breached);

internal static class BudgetReporter
{
    private static readonly JsonSerializerOptions TrendJsonOptions = new()
    {
        WriteIndented = true,
    };


    /// <summary>
    /// Jämför uppmätta scenarier mot deras Klas-låsta budget och emitterar
    /// observe-only GitHub-annotation + JSON-trend-fil.
    /// </summary>
    /// <param name="stats">NBomber-stats från en run.</param>
    /// <param name="scenarioBudgets">Scenario-namn → budget i ms (klass (a)/(b)/(c)).</param>
    /// <param name="trendOutputPath">Absolut väg där trend-JSON skrivs.</param>
    public static void Report(
        NodeStats stats,
        IReadOnlyDictionary<string, int> scenarioBudgets,
        string trendOutputPath)
    {
        var checks = new List<BudgetCheck>(stats.ScenarioStats.Length);

        foreach (var scenarioStats in stats.ScenarioStats)
        {
            if (!scenarioBudgets.TryGetValue(scenarioStats.ScenarioName, out var budgetMs))
            {
                // Scenario utan budget (t.ex. baseline) → loggas men jämförs ej.
                continue;
            }

            // NBomber rapporterar latens i ms (double). Ok.Latency = enbart
            // framgångsrika requests; failed (inkl. 429) räknas i Fail.Request.
            var p95 = scenarioStats.Ok.Latency.Percent95;
            var p99 = scenarioStats.Ok.Latency.Percent99;
            var mean = scenarioStats.Ok.Latency.MeanMs;
            var okCount = scenarioStats.Ok.Request.Count;
            var failCount = scenarioStats.Fail.Request.Count;

            var breached = p95 > budgetMs;

            checks.Add(new BudgetCheck(
                Scenario: scenarioStats.ScenarioName,
                BudgetMs: budgetMs,
                MeasuredP95Ms: p95,
                MeasuredP99Ms: p99,
                MeasuredMeanMs: mean,
                OkCount: okCount,
                FailCount: failCount,
                Breached: breached));

            if (breached)
            {
                // GitHub Actions ::warning::-format. Observe-only → fitness
                // function-trend, ej blockerande exit.
                Console.WriteLine(
                    $"::warning title=Perf budget breach ({scenarioStats.ScenarioName})::" +
                    $"p95={p95:F1} ms överskrider ADR 0045-budget {budgetMs} ms " +
                    $"(p99={p99:F1} ms observe-only, ok={okCount}, fail={failCount}). " +
                    $"Observe-only Fas 1 — blockerar ej CI. Flip→gate = Klas-GO-ratchet (Beslut 6).");
            }
            else
            {
                Console.WriteLine(
                    $"::notice title=Perf budget OK ({scenarioStats.ScenarioName})::" +
                    $"p95={p95:F1} ms ≤ ADR 0045-budget {budgetMs} ms " +
                    $"(p99={p99:F1} ms observe-only, ok={okCount}, fail={failCount}).");
            }

            // Sanity-check på fail-count. 429 från rate-limit räknas som fail
            // och betyder att scenariots last-form måste re-kalibreras mot
            // LandingPublicRead.PermitLimit. Flaggas separat (egen warning)
            // så code-reviewer ser signalen, men blockerar inte.
            if (failCount > 0)
            {
                Console.WriteLine(
                    $"::warning title=Perf scenario non-zero failures ({scenarioStats.ScenarioName})::" +
                    $"fail={failCount} (ok={okCount}). Sannolikt 429 från rate-limit — " +
                    $"re-kalibrera lastformen mot policy:n eller utöka loadtest-miljöns PermitLimit.");
            }
        }

        // JSON-trend-artefakt. Format: NDJSON-vänligt enskilt run-objekt.
        // CI-jobbet laddar upp hela artifacts/perf/-mappen → multipla runs
        // ackumuleras över tid och ger trend-data för Beslut 6-ratchet.
        var trend = new
        {
            timestamp = DateTimeOffset.UtcNow.ToString("O"),
            adr = "0045",
            phase = "observe-only-fas-1",
            checks,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(trendOutputPath)!);

        File.WriteAllText(
            trendOutputPath,
            JsonSerializer.Serialize(trend, TrendJsonOptions));

        Console.WriteLine($"::notice::Perf trend-artefakt skriven: {trendOutputPath}");
    }
}

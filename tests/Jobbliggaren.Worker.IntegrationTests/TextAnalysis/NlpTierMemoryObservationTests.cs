using System.Diagnostics;
using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;
using Jobbliggaren.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.TextAnalysis;

// Fas 4 STEG 2 (F4-2) — OBSERVE-ONLY NLP-tier-minnesobservation (ADR 0045
// 512 MiB Worker-budget). This is NOT a hard gate. It resolves the tier via
// AddTextAnalysis(), forces first use (triggers the lazy DSSO WordList load —
// the only resident post of note), and writes the GC delta + working set to
// test output so a human / perf-test-writer can read the trend.
//
// OWNERSHIP: perf-test-writer owns the FINAL mechanism and threshold for the
// ADR 0045 Worker-memory budget (measured at F4-9 when the WordList gets its
// first real consumer). This test's ceiling is deliberately generous and
// observe-only — it must NOT become a flaky hard gate. Do not tighten the
// bound here; raise it with perf-test-writer at F4-9.
//
// RED until AddTextAnalysis + the three impls + DSSO files ship.
//
// Naming: Method_Scenario_Expected.
public class NlpTierMemoryObservationTests
{
    private readonly ITestOutputHelper _output;

    public NlpTierMemoryObservationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void NlpTier_FirstUse_ResidentMemoryWithinGenerousObserveOnlyCeiling()
    {
        // Baseline AFTER a full collection so the delta isolates the tier.
        var before = GC.GetTotalMemory(forceFullCollection: true);

        var services = new ServiceCollection();
        services.AddTextAnalysis();
        using var provider = services.BuildServiceProvider();

        var analyzer = provider.GetRequiredService<ITextAnalyzer>();
        var stemmer = provider.GetRequiredService<IStemmer>();
        var spellChecker = provider.GetRequiredService<ISpellChecker>();

        // Force first real use — triggers the lazy DSSO WordList load.
        stemmer.Stem("utvecklare", TextLanguage.Swedish).ShouldNotBeNull();
        analyzer.ToLexemes("erfaren lärare söks", TextLanguage.Swedish).ShouldNotBeNull();
        spellChecker.Check("arbete", TextLanguage.Swedish); // lazy DSSO load fires here

        var afterUse = GC.GetTotalMemory(forceFullCollection: true);
        var deltaBytes = afterUse - before;
        var deltaMiB = deltaBytes / (1024.0 * 1024.0);
        var workingSetMiB =
            Process.GetCurrentProcess().WorkingSet64 / (1024.0 * 1024.0);

        _output.WriteLine(
            $"GC-delta efter NLP-tier (DSSO laddad): {deltaMiB:F1} MiB | " +
            $"WorkingSet64: {workingSetMiB:F1} MiB");

        // GENEROUS observe-only ceiling. The DSSO WordList is "tiotals MB" per
        // the architect review; 256 MiB is a deliberately loose bound that only
        // fails on a gross regression (e.g. a Catalyst model sneaking in — which
        // is explicitly OUT of this tier, ADR 0074 OQ1). The REAL budget verdict
        // is perf-test-writer's at F4-9 against the ADR 0045 512 MiB Worker cap.
        const double GenerousCeilingMiB = 256.0;
        deltaMiB.ShouldBeLessThan(GenerousCeilingMiB,
            $"NLP-tierns GC-delta ({deltaMiB:F1} MiB) överskred den generösa " +
            "observe-only-taket — sannolikt en grov regression (Catalyst-modell?). " +
            "Slutligt budgetverdikt ägs av perf-test-writer vid F4-9 (ADR 0045).");
    }
}

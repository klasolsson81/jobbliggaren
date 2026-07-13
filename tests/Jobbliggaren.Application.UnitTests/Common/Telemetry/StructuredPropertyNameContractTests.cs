using Jobbliggaren.Application.Common.Telemetry;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Jobs.Common;
using Jobbliggaren.Application.UnitTests.Common;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Telemetry;

/// <summary>
/// Pins the STRUCTURED property names the #754 instruments emit, because the runbook's Seq
/// queries look them up by name and a structured sink is case-sensitive.
///
/// <para>
/// <b>Why this test exists — it is the guard for a defect that actually shipped to review.</b>
/// MEL derives a property's name from the placeholder TOKEN, not from the literal prose around
/// it:
/// </para>
/// <code>
/// "WorkerMemoryTrend: workingSetBytes={WorkingSetBytes}, ..."
///                     ^^^^^^^^^^^^^^^ prose      ^^^^^^^^^^^^^^^^ THE PROPERTY NAME
/// </code>
/// <para>
/// <c>docs/runbooks/performance-measurement.md</c> §B/§C were written against the prose
/// (<c>@Properties['workingSetBytes']</c>). Seq would have matched the rows on
/// <c>@MessageTemplate</c> and then returned **every selected column as NULL** — the memory
/// instrument's primary readout would have been a column of blanks, and the throughput series
/// likewise. Both dotnet-architect and code-reviewer found it independently; no test could,
/// because the existing assertions only ever inspected the RENDERED message string, where the
/// two spellings are indistinguishable.
/// </para>
///
/// <para>
/// A renamed placeholder is therefore a **breaking change to the runbook**, not a cosmetic
/// edit. If this test goes red, fix the runbook query in the same commit — or put the
/// placeholder back.
/// </para>
///
/// <para>
/// Naming: <c>&lt;ClassUnderTest&gt;_&lt;Scenario&gt;_&lt;Expected&gt;</c>.
/// </para>
/// </summary>
public class StructuredPropertyNameContractTests
{
    [Fact]
    public void WorkerMemoryTrendSampler_TrendEvent_EmitsThePropertyNamesTheRunbookQueries()
    {
        var logger = new RecordingLogger<WorkerMemoryTrendSampler>();
        var sampler = new WorkerMemoryTrendSampler(
            new ScriptedProcessMemoryProbe([new ProcessMemorySample(
                WorkingSetBytes: 100L * 1024 * 1024, GcHeapBytes: 42, Gen2Collections: 7)]),
            Options.Create(new WorkerMemoryTrendOptions()),
            logger);

        sampler.Sample();

        // docs/runbooks/performance-measurement.md §B queries exactly these three.
        logger.Latest.Properties.Select(p => p.Key)
            .ShouldContain("WorkingSetBytes", RunbookContract("§B", "WorkingSetBytes"));
        logger.Latest.Properties.Select(p => p.Key)
            .ShouldContain("GcHeapBytes", RunbookContract("§B", "GcHeapBytes"));
        logger.Latest.Properties.Select(p => p.Key)
            .ShouldContain("Gen2Collections", RunbookContract("§B", "Gen2Collections"));
    }

    [Fact]
    public void IngestionThroughputReporter_ThroughputEvent_EmitsThePropertyNamesTheRunbookQueries()
    {
        var logger = new RecordingLogger<IngestionThroughputReporter>();
        var reporter = new IngestionThroughputReporter(
            Options.Create(new IngestionThroughputOptions()), logger);

        // A qualifying run (fetched >= MinItemsForVerdict, durationSec > 0) — the only kind
        // that emits at all.
        reporter.Report("platsbanken", "snapshot", fetched: 47_000, durationSec: 2400);

        var names = logger.Records[0].Properties.Select(p => p.Key).ToList();

        // docs/runbooks/performance-measurement.md §C queries exactly these five.
        names.ShouldContain("Source", RunbookContract("§C", "Source"));
        names.ShouldContain("JobType", RunbookContract("§C", "JobType"));
        names.ShouldContain("Fetched", RunbookContract("§C", "Fetched"));
        names.ShouldContain("DurationSec", RunbookContract("§C", "DurationSec"));
        names.ShouldContain("ItemsPerMinute", RunbookContract("§C", "ItemsPerMinute"));
    }

    private static string RunbookContract(string section, string property) =>
        $"docs/runbooks/performance-measurement.md {section} queries @Properties['{property}'], " +
        "and a structured sink is case-sensitive. If the placeholder was renamed, the runbook " +
        "query now returns a column of NULLs — fix the runbook in the same commit, or restore " +
        "the placeholder. (Note: MEL takes the property name from the {Placeholder} token, not " +
        "from the prose next to it.)";
}

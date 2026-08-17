using Shouldly;

namespace Jobbliggaren.Migrate.UnitTests;

/// <summary>
/// Pins that the deploy stack's job-ad ingestion gate defaults to OFF.
///
/// <para>
/// Same shape and same reason as <see cref="DeployComposeRegistrationGateTests"/>, one gate
/// over, and the reason it is a separate class is that this one is not the registration gate —
/// a shared class would need a doc comment that is false about half its rows.
/// </para>
///
/// <para>
/// What this pin sees that nothing else does: <c>JobSourceIngestOptions</c>' code default is
/// <see langword="true"/>, and <c>JobSourceIngestGateConfigurationTests</c> pins that the
/// shipped Production overlay carries <c>false</c>. Neither of them sees the value the box
/// actually feeds the container. Flip <c>${JOBTECH_INGEST_ENABLED:-false}</c> to <c>:-true</c>
/// and nothing goes red: the overlay's <c>false</c> is overridden by the environment, which
/// wins in the configuration order, and the box starts writing recruiter contact records with
/// no operator having set anything. The two jobs would not even log their refusal, because
/// they only log when the gate is off.
/// </para>
///
/// <para>
/// The gate is a DECISION and not a derivable state — Klas's explicit written GO, recorded
/// with its adjudicator, date and place in <c>release-checklist.md</c> §2.6 point 3.5. This
/// pin does not encode the decision and cannot: it pins that an ABSENT variable means off, so
/// that turning ingestion on stays an act someone performs rather than a default someone
/// inherits.
/// </para>
///
/// <para>
/// Naming: <c>&lt;ClassUnderTest&gt;_&lt;Scenario&gt;_&lt;Expected&gt;</c>.
/// </para>
/// </summary>
public class DeployComposeIngestGateTests
{
    private static string ComposeText =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "deploy", "docker-compose.yml"));

    private static string LineContaining(string key) =>
        ComposeText.Split('\n').SingleOrDefault(l => l.Contains(key, StringComparison.Ordinal))
        ?? throw new InvalidOperationException(
            $"deploy/docker-compose.yml has no single line containing '{key}'. If the file was " +
            "restructured, this pin must be rewritten rather than deleted.");

    [Fact]
    public void IngestGate_DefaultsToOff_WhenTheBoxSetsNothing()
    {
        // The whole line, so a renamed variable fails the same way an opened default does: a
        // rename that leaves deploy/.env.example behind reads as configured and ingests anyway.
        LineContaining("JobTech__IngestEnabled:").Trim()
            .ShouldBe("JobTech__IngestEnabled: ${JOBTECH_INGEST_ENABLED:-false}",
                customMessage:
                "The ingestion gate must default OFF. An open default lands recruiter contact " +
                "records within ten minutes of the first `up -d`, and nothing refuses it: the " +
                "code default is true, so an absent key does not fail, it ingests.");
    }

    /// <summary>
    /// The vacuity guard. If the compose file stopped being copied into this project's output,
    /// <see cref="LineContaining"/> would throw rather than pass — but a future restructure
    /// could leave the file present and the worker service gone, and then the assertion above
    /// would fail for a reason nobody would read correctly. This pins that the gate sits on the
    /// service that runs the jobs.
    /// </summary>
    [Fact]
    public void IngestGate_SitsOnTheWorkerService_WhichIsWhereTheJobsRun()
    {
        var lines = ComposeText.Split('\n');
        var gateIndex = Array.FindIndex(lines, l =>
            l.Contains("JobTech__IngestEnabled:", StringComparison.Ordinal));

        gateIndex.ShouldBeGreaterThan(-1, "the gate line is gone entirely");

        // Walk back to the nearest two-space service key. The api service consumes no JobTech
        // options at all, so the gate landing there would be inert AND silent.
        var owner = lines.Take(gateIndex)
            .LastOrDefault(l => l.Length > 2 && l.StartsWith("  ", StringComparison.Ordinal)
                                && !l.StartsWith("   ", StringComparison.Ordinal)
                                && l.TrimEnd().EndsWith(':'));

        owner?.Trim().ShouldBe("worker:",
            customMessage:
            "The Platsbanken jobs are registered by the Worker host. On any other service this " +
            "environment key binds nothing, fails nothing, and logs nothing — the gate would " +
            "read as present while the Worker inherits the code default, which is true.");
    }
}

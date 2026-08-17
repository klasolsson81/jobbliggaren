using Shouldly;

namespace Jobbliggaren.Migrate.UnitTests;

/// <summary>
/// Pins that the deploy stack's job-ad ingestion gate defaults to OFF, and that it sits on the
/// service that runs the jobs.
///
/// <para>
/// Both tests use <see cref="DeployComposeRegistrationGateTests"/>' forward-scanning form:
/// anchor on the service key, assert the anchor unconditionally, then assert position and count
/// inside that service's block. A backward scan from the environment key was tried first and
/// measured to fail OPEN — see the second test's own remarks.
/// </para>
///
/// <para>
/// What this pin sees that nothing else does: <c>JobSourceIngestOptions</c>' code default is
/// <see langword="true"/>, and <c>JobSourceIngestGateConfigurationTests</c> pins that the
/// shipped Production overlay carries <c>false</c>. Neither of them sees the value the box
/// actually feeds the container. Flip <c>${JOBTECH_INGEST_ENABLED:-false}</c> to <c>:-true</c>
/// and nothing goes red: environment wins over JSON in the configuration order, so the
/// overlay's <c>false</c> is overridden and the box starts writing recruiter contact records
/// with no operator having set anything. The two cron jobs would not even log their refusal,
/// because they only log when the gate is off.
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
    private const string GateKey = "JobTech__IngestEnabled:";

    private static string[] ComposeLines =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "deploy", "docker-compose.yml"))
            .Split('\n');

    /// <summary>
    /// Two spaces of indent, ending in a colon. That matches every service key — and also the
    /// volume names, the network name and one anchor child, which is why it is only ever used to
    /// find where the worker's block ENDS, scanning forward from the worker key itself. It is a
    /// SHAPE test, not a service test, and must not be used as one.
    ///
    /// <para>
    /// The over-match is bounded rather than merely harmless, and the network name is what
    /// bounds it: were <c>worker</c> ever the last service, the forward scan would stop at the
    /// network name rather than running to the end of the file. The range would gain a block
    /// header that cannot carry an environment key, and the assertion it feeds has an upper
    /// bound — a range too wide only weakens, it never fails the wrong way.
    /// </para>
    /// </summary>
    private static bool IsTwoSpaceKey(string line) =>
        line.Length > 2
        && line.StartsWith("  ", StringComparison.Ordinal)
        && line[2] != ' '
        && line.TrimEnd().EndsWith(':');

    [Fact]
    public void IngestGate_DefaultsToOff_WhenTheBoxSetsNothing()
    {
        var matches = ComposeLines.Where(l => l.Contains(GateKey, StringComparison.Ordinal)).ToList();

        matches.Count.ShouldBe(1,
            "deploy/docker-compose.yml must carry exactly one ingestion-gate line. If the file " +
            "was restructured, this pin must be rewritten rather than deleted.");

        // The whole line, so a renamed variable fails the same way an opened default does: a
        // rename that leaves deploy/.env.example behind is the same defect as an open default,
        // just slower to find.
        matches[0].Trim().ShouldBe($"{GateKey} ${{JOBTECH_INGEST_ENABLED:-false}}",
            customMessage:
            "The ingestion gate must default OFF. An open default lands recruiter contact " +
            "records within ten minutes of the first `up -d`, and nothing refuses it: the " +
            "code default is true, so an absent key does not fail, it ingests.");
    }

    /// <summary>
    /// The gate must sit inside the <c>worker</c> service's own block.
    ///
    /// <para>
    /// This scans FORWARD from the service key rather than backward from the gate, and the
    /// difference was measured rather than assumed. A backward scan returns
    /// <see langword="null"/> when the gate is hoisted OUT of <c>services:</c> — into one of the
    /// file's seven top-level <c>x-*</c> anchors, which is this file's own established refactor
    /// idiom — and both agents measured that arrangement passing GREEN while the worker carried
    /// no gate key at all. Forward-scanning cannot: the anchor assertion below fires first.
    /// </para>
    ///
    /// <para>
    /// Why the worker and not the api: <b>both</b> hosts bind the options AND register the three
    /// consumers, because <c>AddJobSources</c> is the one Infrastructure module both pass. What
    /// differs is EXECUTION — only the Worker registers the Hangfire wrappers and the recurring
    /// jobs, and only the Worker runs a Hangfire server. So a gate on <c>api</c> would read as
    /// present, bind successfully, and gate nothing that ever runs.
    /// (The third consumer is not a job at all but a shared runner three backfill jobs take a
    /// dependency on — admin-triggered, never on a cron. A fourth backfill job in the same
    /// namespace deliberately does not use it, and says so in its own docstring.)
    /// </para>
    /// </summary>
    [Fact]
    public void IngestGate_SitsInsideTheWorkerServiceBlock_WhereTheJobsAreExecuted()
    {
        var lines = ComposeLines;

        var workerStart = Array.FindIndex(lines, l => l.StartsWith("  worker:", StringComparison.Ordinal));
        workerStart.ShouldBeGreaterThan(-1, "the compose file no longer declares a `worker` service");

        var workerEnd = Array.FindIndex(lines, workerStart + 1, IsTwoSpaceKey);
        if (workerEnd < 0) workerEnd = lines.Length;

        var gateIndex = Array.FindIndex(lines, l => l.Contains(GateKey, StringComparison.Ordinal));
        gateIndex.ShouldBeGreaterThan(-1, "the ingestion-gate line is gone entirely");

        gateIndex.ShouldBeInRange(workerStart + 1, workerEnd - 1,
            "the ingestion gate must sit inside the `worker` service's own block. Hoisted into " +
            "a shared anchor or moved to another service it still parses, still binds, and " +
            "still reads as configured - while the Worker inherits JobSourceIngestOptions' code " +
            "default, which is true.");
    }
}

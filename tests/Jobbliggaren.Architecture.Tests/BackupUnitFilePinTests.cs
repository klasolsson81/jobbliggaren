using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #197 / ADR 0050 gate M-4 — the systemd units that carry the nightly backup.
///
/// <para>
/// <b>Nothing in this repository pinned a systemd unit file before this class.</b> Four units and
/// two timers ship in <c>deploy/systemd/</c> and every one of their properties was, until now,
/// held only by a comment. The bash fixture suites cover the scripts; the units are the half
/// that decides whether a script ever runs, and a unit that stops being triggered fails no test
/// and appears on no list — a timer is not "failed" when it is disabled.
/// </para>
///
/// <para>
/// <b>Why source text and not a runtime assertion.</b> These files are consumed by systemd on a
/// host this test suite cannot reach. The properties worth holding are textual — a timer that is
/// <c>Persistent</c>, a service whose <c>ExecStart</c> names a script that exists, a
/// <c>Documentation=</c> line that points at a runbook that exists. Each pins a specific way the
/// pair has been observed to rot: a nightly job silently losing its catch-up semantics, a script
/// renamed without its unit, and a <c>Documentation=</c> URL surviving the deletion of the file
/// it names.
/// </para>
///
/// <para>
/// The polarity difference between the two timers is deliberate and is asserted in both
/// directions. The backup timer is <c>Persistent=true</c> because a missed night is a missing
/// artefact no later run replaces; the freshness timer is not, because "is the backup stale right
/// now" has no missed-run semantics and a catch-up firing would only duplicate the boot run. An
/// assertion on one alone would let the pair converge on whichever spelling was copied last.
/// </para>
/// </summary>
public class BackupUnitFilePinTests
{
    private const string SystemdDirectory = "deploy/systemd";
    private const string BackupScript = "jobbliggaren-backup.sh";
    private const string Runbook = "docs/runbooks/backup-restore.md";

    [Fact]
    public void BackupTimer_IsNightlyAndPersistent()
    {
        var timer = ReadUnit("jobbliggaren-backup.timer");

        DirectiveOf(timer, "OnCalendar").ShouldBe("*-*-* 02:15:00",
            "the nightly backup runs once a day at a fixed time. A change here is a change to " +
            "the window the freshness probe's 26h threshold is derived from.");

        DirectiveOf(timer, "Persistent").ShouldBe("true",
            "a box that was off at 02:15 must take its backup on boot rather than skip a night. " +
            "A missed backup is a missing artefact that no later run replaces — which is exactly " +
            "why this timer's answer differs from jobbliggaren-backup-fresh.timer's.");
    }

    [Fact]
    public void FreshnessTimer_IsNotPersistent_BecauseStalenessHasNoMissedRunSemantics()
    {
        var timer = ReadUnit("jobbliggaren-backup-fresh.timer");

        // Asserted as an ABSENCE, which is the weaker half of the pair and is stated as such:
        // it cannot fail if the directive is merely renamed. The positive sibling above carries
        // the liveness, so a rename that broke both would still be caught there.
        Directives(timer).Any(line => line.StartsWith("Persistent=", StringComparison.Ordinal))
            .ShouldBeFalse(
            "the freshness probe must not carry missed-run semantics: a catch-up firing would " +
            "only duplicate the OnBootSec run.");

        DirectiveOf(timer, "OnCalendar").ShouldBe("hourly",
            "a nightly job that stopped being triggered must surface within the hour, not the day.");
    }

    [Theory]
    [InlineData("jobbliggaren-backup.timer")]
    [InlineData("jobbliggaren-backup-fresh.timer")]
    public void Timers_AreEnabledByTimersTarget(string fileName)
    {
        var timer = ReadUnit(fileName);

        DirectiveOf(timer, "WantedBy").ShouldBe("timers.target",
            "a timer with no [Install] section cannot be enabled, so the unit it drives never " +
            "runs and nothing reports that it does not.");
    }

    [Theory]
    [InlineData("jobbliggaren-backup.service")]
    [InlineData("jobbliggaren-backup-fresh.service")]
    public void Services_HaveNoInstallSection_SoTheTimerIsTheOnlyActivationPath(string fileName)
    {
        var service = ReadUnit(fileName);

        Directives(service).Any(line => line == "[Install]").ShouldBeFalse(
            $"{fileName} is started by its timer. A WantedBy=multi-user.target here would " +
            "additionally run it at every boot as a side effect of enabling — for the backup " +
            "service that means a full dump on every reboot.");
    }

    [Theory]
    [InlineData("jobbliggaren-backup.service", BackupScript)]
    [InlineData("jobbliggaren-backup-fresh.service", BackupScript + " --check")]
    public void Services_ExecStartNamesAScriptThatExists(string fileName, string expectedTail)
    {
        var execStart = DirectiveOf(ReadUnit(fileName), "ExecStart");

        execStart.EndsWith(expectedTail, StringComparison.Ordinal).ShouldBeTrue(
            $"{fileName} must invoke {expectedTail}, but ExecStart is '{execStart}'. The two " +
            "services share one script and are told apart by that argument alone.");

        // The path is absolute and rooted at the box's checkout, so only the file name can be
        // checked against the repository — but that is the half that rots: a renamed script
        // leaves the unit pointing at nothing, and systemd reports it only when the timer fires.
        var scriptPath = Path.Combine(RepositoryRoot(), SystemdDirectory, BackupScript);
        File.Exists(scriptPath).ShouldBeTrue(
            $"{fileName} names {BackupScript}, which does not exist at {SystemdDirectory}/");

        execStart.StartsWith("/opt/jobbliggaren/" + SystemdDirectory + "/", StringComparison.Ordinal)
            .ShouldBeTrue("the unit must name the path the box actually checks the repository " +
                          $"out to, but ExecStart is '{execStart}'.");
    }

    [Theory]
    [InlineData("jobbliggaren-backup.service")]
    [InlineData("jobbliggaren-backup.timer")]
    [InlineData("jobbliggaren-backup-fresh.service")]
    [InlineData("jobbliggaren-backup-fresh.timer")]
    public void Units_DocumentationPointsAtARunbookThatExists(string fileName)
    {
        var documentation = DirectiveOf(ReadUnit(fileName), "Documentation");

        documentation.EndsWith(Runbook, StringComparison.Ordinal).ShouldBeTrue(
            $"{fileName} must point an operator at the backup runbook. `systemctl --failed` is " +
            "this box's only alarm surface (#1175), and the Documentation= line is what turns a " +
            "failed unit into an actionable one at 03:00.");

        File.Exists(Path.Combine(RepositoryRoot(), Runbook)).ShouldBeTrue(
            $"{fileName} documents {Runbook}, which does not exist. A Documentation= URL " +
            "outliving the file it names is worse than none: it reads as an instruction.");
    }

    [Fact]
    public void BackupService_OrdersItselfAfterDockerAndPullsNetworkOnlineIntoTheTransaction()
    {
        var service = ReadUnit("jobbliggaren-backup.service");

        var after = DirectiveOf(service, "After");
        after.Contains("docker.service", StringComparison.Ordinal).ShouldBeTrue(
            "the run dumps through `docker exec`; starting before dockerd fails for a reason " +
            $"that has nothing to do with backups. After= is '{after}'.");

        // After= without Wants= is inert against a target nothing else pulls in, and the failure
        // is silent: the unit starts anyway, before the network is up, and the upload fails.
        after.Contains("network-online.target", StringComparison.Ordinal).ShouldBeTrue(
            $"After= must order against network-online.target, but is '{after}'.");
        DirectiveOf(service, "Wants").Contains("network-online.target", StringComparison.Ordinal).ShouldBeTrue(
            "ordering against network-online.target does nothing unless something pulls the " +
            "target into the transaction. This unit uploads, so it needs the target itself.");
    }

    /// <summary>
    /// The vacuity guard. Every assertion above reads a file, and a test suite whose files have
    /// all been renamed or deleted would otherwise report a green run over nothing at all —
    /// <see cref="ReadUnit"/> throws, but only for the files a case happens to name.
    /// </summary>
    [Fact]
    public void AllFourUnitFilesExist()
    {
        string[] expected =
        [
            "jobbliggaren-backup.service",
            "jobbliggaren-backup.timer",
            "jobbliggaren-backup-fresh.service",
            "jobbliggaren-backup-fresh.timer",
        ];

        foreach (var fileName in expected)
        {
            File.Exists(Path.Combine(RepositoryRoot(), SystemdDirectory, fileName))
                .ShouldBeTrue($"{SystemdDirectory}/{fileName} is missing");
        }
    }

    /// <summary>
    /// The unit file's directives, with comments and blank lines removed.
    ///
    /// <para>
    /// <b>Absence assertions must run against this and never against the raw text</b>, and this
    /// class learned that the expensive way: every one of these unit files carries a comment
    /// explaining why a directive is <em>absent</em> — "NO [Install] SECTION", "Deliberately NOT
    /// Persistent=true" — so a substring search over the whole file matches the prose that
    /// documents the property and reports the opposite of the truth. Three cases failed against
    /// correct unit files before this existed.
    /// </para>
    /// </summary>
    private static List<string> Directives(string unitText) =>
        unitText
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();

    private static string ReadUnit(string fileName)
    {
        var path = Path.Combine(RepositoryRoot(), SystemdDirectory, fileName);
        File.Exists(path).ShouldBeTrue($"expected a systemd unit at {SystemdDirectory}/{fileName}");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// Reads a single directive's value. Deliberately not a <c>Contains</c> on the whole file:
    /// every one of these unit files carries long comment blocks that quote the very strings
    /// being asserted, so a substring match would pass against the prose explaining why a
    /// directive was removed. Comment lines are dropped before matching for that reason.
    /// </summary>
    private static string DirectiveOf(string unitText, string directive)
    {
        var matches = unitText
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => !line.StartsWith('#'))
            .Where(line => line.StartsWith(directive + "=", StringComparison.Ordinal))
            .Select(line => line[(directive.Length + 1)..].Trim())
            .ToList();

        matches.Count.ShouldBe(1,
            $"expected exactly one uncommented `{directive}=` line, found {matches.Count}. " +
            "Two spellings of one setting is how an assertion quietly keeps checking the " +
            "one that is no longer in effect.");

        return matches[0];
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null
               && !(File.Exists(Path.Combine(dir.FullName, "Jobbliggaren.sln"))
                    && Directory.Exists(Path.Combine(dir.FullName, "src"))))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("could not locate the repository root from " + AppContext.BaseDirectory);
        return dir.FullName;
    }
}

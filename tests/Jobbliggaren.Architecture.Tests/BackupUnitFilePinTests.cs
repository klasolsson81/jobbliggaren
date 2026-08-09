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
    /// The set of backup units is exactly these four, asserted as an EQUALITY in both directions.
    ///
    /// <para>
    /// This began as an existence check, and that version guarded nothing:
    /// <see cref="Units_DocumentationPointsAtARunbookThatExists"/> already names all four, so it
    /// could not fail without another case failing first. The gap it left is the opposite one - a
    /// fifth <c>jobbliggaren-backup-*.timer</c> added later would get no coverage from any case
    /// here and nothing would say so. An equality catches a deletion AND an unpinned addition.
    /// </para>
    /// </summary>
    [Fact]
    public void TheBackupUnitSetIsExactlyThese_SoAnAdditionCannotArriveUnpinned()
    {
        string[] expected =
        [
            "jobbliggaren-backup-fresh.service",
            "jobbliggaren-backup-fresh.timer",
            "jobbliggaren-backup.service",
            "jobbliggaren-backup.timer",
        ];

        var actual = Directory
            .GetFiles(Path.Combine(RepositoryRoot(), SystemdDirectory), "jobbliggaren-backup*")
            .Select(Path.GetFileName)
            .Where(name => name!.EndsWith(".service", StringComparison.Ordinal)
                           || name.EndsWith(".timer", StringComparison.Ordinal))
            .Order()
            .ToArray();

        actual.ShouldBe(expected,
            "the backup unit set changed. Every unit here is pinned by the cases above; a new one " +
            "must be added to them and to this list in the same change, or it ships with its " +
            "properties held by nothing but a comment.");
    }


    /// <summary>
    /// The age recipient is tracked, so it can be pinned — and pinning it is the one control that
    /// tracking buys.
    ///
    /// <para>
    /// The box refuses to run without a well-formed recipient, but that refusal happens at 02:15
    /// on a machine nobody is watching. A recipient mangled by an editor, a CRLF, a stray blank
    /// line or a paste of the <c>.example</c> file reaches the box and is discovered a night later
    /// at best. Here it is a build failure.
    /// </para>
    ///
    /// <para>
    /// It cannot check that the recipient is the RIGHT one — only Klas holds the private half, and
    /// a swapped-but-well-formed recipient is exactly what the periodic drill exists to catch
    /// (<c>docs/runbooks/backup-restore.md</c> §6). What it does check is everything shape can
    /// carry, including the one that would be catastrophic and is trivially detectable: a PRIVATE
    /// key pasted into the public file.
    /// </para>
    /// </summary>
    [Fact]
    public void AgeRecipient_IsExactlyOneWellFormedPublicRecipient()
    {
        var path = Path.Combine(RepositoryRoot(), "deploy", "backup", "age.recipient");
        File.Exists(path).ShouldBeTrue(
            "deploy/backup/age.recipient is what the box encrypts to. Without it the nightly " +
            "unit refuses, and it refuses at 02:15 rather than here.");

        var lines = File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

        lines.Count.ShouldBe(1,
            $"expected exactly one recipient line, found {lines.Count}. The script reads the whole " +
            "file and strips whitespace, so a second line silently becomes part of the value.");

        lines[0].StartsWith("AGE-SECRET-KEY-", StringComparison.OrdinalIgnoreCase).ShouldBeFalse(
            "that is a PRIVATE key. It must never exist in this repository or on the box — the " +
            "whole point of encrypting to a recipient is that the box holds no key that opens a " +
            "backup (ADR 0050 Amendment 2026-08-04 §7 requirement b).");

        System.Text.RegularExpressions.Regex.IsMatch(lines[0], "^age1[0-9a-z]+$").ShouldBeTrue(
            $"'{lines[0]}' is not a well-formed age recipient. This is the same predicate " +
            "jobbliggaren-backup.sh applies before it will run, asserted here so a malformed " +
            "value fails the build instead of the 02:15 run.");
    }

    /// <summary>
    /// The unit file's directives, with comments and blank lines removed.
    ///
    /// <para>
    /// <b>Absence assertions must run against this and never against the raw text</b>, and this
    /// class learned that the expensive way: every one of these unit files carries a comment
    /// explaining why a directive is <em>absent</em> - "NO [Install] SECTION", "Deliberately NOT
    /// Persistent=true" - so a substring search over the whole file matches the prose that
    /// documents the property and reports the opposite of the truth. Three cases failed against
    /// correct unit files before this existed.
    /// </para>
    ///
    /// <para>
    /// It is also the ONLY definition of "what is a directive line" in this class.
    /// <see cref="DirectiveOf"/> used to re-implement the same filter, which is the very fault its
    /// own failure message names: two spellings of one rule is how an assertion quietly keeps
    /// checking the one that is no longer in effect.
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
        var matches = Directives(unitText)
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

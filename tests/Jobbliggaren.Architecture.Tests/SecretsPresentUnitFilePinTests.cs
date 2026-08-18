using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #198 / ADR 0050 gate B-1 — the two absence detectors, and the one property that tells them
/// apart.
///
/// <para>
/// <b>The invariant this class exists for is a single command-line argument.</b> Since #1329 one
/// script carries two detectors: <c>--check</c> answers for the crypto secrets api and worker
/// read, <c>--check-host</c> for #197's host-only backup credential, which no container sees. The
/// two sets have different owners, different severity and different provisioning lifecycles, and
/// the ONLY thing binding each unit to its own set is the argument on its <c>ExecStart</c> line.
/// </para>
///
/// <para>
/// <b>Swap those two arguments and every suite in this repository stays green.</b> The bash
/// fixture suite proves the script has two correct entry points — including the counterfactual
/// that <c>--check</c> goes green and <c>--check-host</c> red in one and the same fixture state —
/// but it never reads a unit file, so it cannot see which entry point the box actually calls. A
/// host timer that ran <c>--check</c> hourly would report green on a box with the crypto secrets
/// injected and the rclone credential absent: a silent deletion of exactly the alarm #1329 was
/// filed to create, on a box whose only alarm surface is <c>systemctl --failed</c> (#1175, no log
/// sink).
/// </para>
///
/// <para>
/// <b>Both are pinned, never one.</b> A pin on the host unit alone cannot fail on a swap — the
/// crypto unit would then carry <c>--check-host</c> and nothing would ask. That is the same
/// polarity argument <see cref="BackupUnitFilePinTests"/> makes for its two timers, and it applies
/// verbatim to a pair told apart by one flag.
/// </para>
///
/// <para>
/// <b>Why the helpers below are a second copy, and why that is a DECLINED extraction rather than a
/// deferred one.</b> They are byte-alike with <see cref="BackupUnitFilePinTests"/>'s, whose own
/// docstring argues that two spellings of one rule is how an assertion quietly keeps checking the
/// one no longer in effect — so the duplication is a considered cost, not an oversight. It was
/// declined on the ground that extracting would refactor #197's already-pinned class, buying a
/// shared helper at the price of a new reviewable claim (did the extraction preserve behaviour?).
/// <b>And the divergence it risks fails safe:</b> if one copy's <c>Directives</c> filter were
/// tightened and the other's were not, <see cref="DirectiveOf"/>'s <c>ShouldBe</c> on the match
/// count goes RED — the drift fells itself, which is a different class from the silent one
/// <see cref="BackupUnitFilePinTests"/> warns about. This paragraph is the record of a decision,
/// not a pointer at work someone owes: no follow-up is filed and none is implied.
/// </para>
/// </summary>
public class SecretsPresentUnitFilePinTests
{
    private const string SystemdDirectory = "deploy/systemd";
    private const string InjectScript = "jobbliggaren-inject-secrets.sh";
    private const string Runbook = "docs/runbooks/master-key-ops.md";

    private const string CryptoService = "jobbliggaren-secrets-present.service";
    private const string CryptoTimer = "jobbliggaren-secrets-present.timer";
    private const string HostService = "jobbliggaren-host-secrets-present.service";
    private const string HostTimer = "jobbliggaren-host-secrets-present.timer";

    /// <summary>
    /// The whole point of #1329, held as source text.
    ///
    /// <para>
    /// <c>--check</c> is a prefix of <c>--check-host</c>, and the polarity survives it: an
    /// <c>ExecStart</c> ending in <c>--check-host</c> does not end in <c>--check</c>, so a swap in
    /// either direction fails its own case rather than passing both.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(CryptoService, InjectScript + " --check")]
    [InlineData(HostService, InjectScript + " --check-host")]
    public void Services_ExecStartNamesItsOwnDetector(string fileName, string expectedTail)
    {
        var execStart = DirectiveOf(ReadUnit(fileName), "ExecStart");

        execStart.EndsWith(expectedTail, StringComparison.Ordinal).ShouldBeTrue(
            $"{fileName} must invoke {expectedTail}, but ExecStart is '{execStart}'. The two " +
            "services share one script and are told apart by that argument alone — swap them and " +
            "the host timer reports on the crypto set while the rclone credential goes unwatched.");

        var scriptPath = Path.Combine(RepositoryRoot(), SystemdDirectory, InjectScript);
        File.Exists(scriptPath).ShouldBeTrue(
            $"{fileName} names {InjectScript}, which does not exist at {SystemdDirectory}/");

        execStart.StartsWith("/opt/jobbliggaren/" + SystemdDirectory + "/", StringComparison.Ordinal)
            .ShouldBeTrue("the unit must name the path the box actually checks the repository " +
                          $"out to, but ExecStart is '{execStart}'.");
    }

    /// <summary>
    /// The cadences diverge on purpose, and both directions are asserted for the reason
    /// <see cref="BackupUnitFilePinTests"/> gives: an assertion on one alone lets the pair converge
    /// on whichever spelling was copied last.
    /// </summary>
    [Fact]
    public void Timers_CadenceMatchesTheSeverityOfTheSetEachOneWatches()
    {
        DirectiveOf(ReadUnit(CryptoTimer), "OnCalendar").ShouldBe("*:0/10",
            "an absent crypto secret means api and worker are crash-looping and the site is down. " +
            "Ten minutes is the resolution that condition earns.");

        DirectiveOf(ReadUnit(HostTimer), "OnCalendar").ShouldBe("hourly",
            "an absent backup credential means tonight's upload will not happen — hours, not " +
            "minutes. Firing six times as often would spend the alarm surface's credibility on a " +
            "condition nobody can act on faster (#1329).");
    }

    /// <summary>
    /// The boot offset is derived from the CLEARING interval, not copied between the pair.
    ///
    /// <para>
    /// A unit that fires before the operator has finished injecting sits on
    /// <c>systemctl --failed</c> until its next run. For the crypto timer that is at most ten
    /// minutes; for an hourly timer a 2-minute offset would leave a stale failure lit for nearly
    /// an hour, and <c>jobbliggaren-heartbeat.timer</c> would page once at the transition and then
    /// leave that surface deaf to the next fault for the rest of it. <b>The cost is the deafness,
    /// not a burst of pages</b> — <c>systemctl --failed</c> latches and the expecter notifies on
    /// the transition (#1397, measured 2026-08-17). What derives 20 is not a window-length
    /// optimum: the assertion's own reason cites the house value
    /// with two hourly siblings on DIFFERENT offsets (backup-fresh 20min, logship-fresh 25min), and
    /// a window-length optimum could not make both correct. What this offset buys is room to inject
    /// before the first fire. An alarm lit for a condition that no longer exists also
    /// trains an operator to stop reading the only alarm surface there is — which is the doctrine
    /// these units are written against, so reproducing it here would be self-defeating.
    /// </para>
    /// </summary>
    [Fact]
    public void HostTimer_BootOffsetIsSizedForAnHourlyClearingInterval()
    {
        DirectiveOf(ReadUnit(HostTimer), "OnBootSec").ShouldBe("20min",
            "the house value for an hourly unit (jobbliggaren-backup-fresh.timer 20min, " +
            "jobbliggaren-logship-fresh.timer 25min). The crypto sibling's 2min is affordable " +
            "only because it self-clears within ten.");

        DirectiveOf(ReadUnit(CryptoTimer), "OnBootSec").ShouldBe("2min",
            "late enough that an operator injecting after a PLANNED reboot is not paged by their " +
            "own maintenance, early enough that an UNPLANNED one is on `systemctl --failed` long " +
            "before anyone notices the site is degraded.");
    }

    /// <summary>
    /// Asserted as an ABSENCE, and stated as the weaker half: it cannot fail if the directive is
    /// merely renamed. "Are the secrets present right now" has no missed-run semantics, and a
    /// catch-up firing would only duplicate the OnBootSec run.
    /// </summary>
    [Theory]
    [InlineData(CryptoTimer)]
    [InlineData(HostTimer)]
    public void Timers_AreNotPersistent_BecausePresenceHasNoMissedRunSemantics(string fileName)
    {
        Directives(ReadUnit(fileName))
            .Any(line => line.StartsWith("Persistent=", StringComparison.Ordinal))
            .ShouldBeFalse(
                $"{fileName} must not carry missed-run semantics — unlike jobbliggaren-backup.timer, " +
                "where a missed night is a missing artefact no later run replaces.");
    }

    /// <summary>
    /// <c>OnUnitActiveSec=</c> measures from the moment the triggered unit became ACTIVE, and a
    /// <c>Type=oneshot</c> service never is (systemd#21600). The earlier spelling delivered the
    /// boot alarm and neither of the two properties it claimed: it would not re-fire, and — worse —
    /// it would not clear, leaving the unit in <c>systemctl --failed</c> after a successful
    /// injection. That repair is what this case holds.
    /// </summary>
    [Theory]
    [InlineData(CryptoTimer)]
    [InlineData(HostTimer)]
    public void Timers_DriveAOneshotOnAWallClock_NeverOnUnitState(string fileName)
    {
        Directives(ReadUnit(fileName))
            .Any(line => line.StartsWith("OnUnitActiveSec=", StringComparison.Ordinal))
            .ShouldBeFalse(
                $"{fileName} triggers a Type=oneshot service, which never becomes active, so " +
                "OnUnitActiveSec= would neither re-fire nor clear (systemd#21600).");
    }

    [Theory]
    [InlineData(CryptoTimer)]
    [InlineData(HostTimer)]
    public void Timers_AreEnabledByTimersTarget(string fileName)
    {
        DirectiveOf(ReadUnit(fileName), "WantedBy").ShouldBe("timers.target",
            "a timer with no [Install] section cannot be enabled, so the unit it drives never " +
            "runs and nothing reports that it does not.");
    }

    [Theory]
    [InlineData(CryptoService)]
    [InlineData(HostService)]
    public void Services_HaveNoInstallSection_SoTheTimerIsTheOnlyActivationPath(string fileName)
    {
        Directives(ReadUnit(fileName)).Any(line => line == "[Install]").ShouldBeFalse(
            $"{fileName} is started by its timer. A second activation path is how one of the pair " +
            "ends up enabled without the other, which is precisely the state #1329 separated.");
    }

    [Theory]
    [InlineData(CryptoService)]
    [InlineData(CryptoTimer)]
    [InlineData(HostService)]
    [InlineData(HostTimer)]
    public void Units_DocumentationPointsAtARunbookThatExists(string fileName)
    {
        var documentation = DirectiveOf(ReadUnit(fileName), "Documentation");

        documentation.EndsWith(Runbook, StringComparison.Ordinal).ShouldBeTrue(
            $"{fileName} must point an operator at the master-key runbook: `systemctl --failed` is " +
            "this box's only alarm surface (#1175), and Documentation= is what turns a failed unit " +
            "into an actionable one.");

        File.Exists(Path.Combine(RepositoryRoot(), Runbook)).ShouldBeTrue(
            $"{fileName} documents {Runbook}, which does not exist. A Documentation= URL " +
            "outliving the file it names is worse than none: it reads as an instruction.");
    }

    /// <summary>
    /// An equality in both directions, for the reason <see cref="BackupUnitFilePinTests"/> records:
    /// an existence check cannot see a THIRD detector arriving later with its properties held by
    /// nothing but a comment — which is how this pair got to two in the first place.
    /// </summary>
    [Fact]
    public void TheAbsenceDetectorUnitSetIsExactlyThese_SoAnAdditionCannotArriveUnpinned()
    {
        string[] expected = [HostService, HostTimer, CryptoService, CryptoTimer];

        var actual = Directory
            .GetFiles(Path.Combine(RepositoryRoot(), SystemdDirectory))
            .Select(Path.GetFileName)
            .Where(name => name!.EndsWith("secrets-present.service", StringComparison.Ordinal)
                           || name.EndsWith("secrets-present.timer", StringComparison.Ordinal))
            .Order()
            .ToArray();

        actual.ShouldBe(expected.Order().ToArray(),
            "the absence-detector unit set changed. Every unit here is pinned by the cases above; " +
            "a new one must be added to them and to this list in the same change, or it ships with " +
            "its detector argument held by nothing but a comment.");
    }

    /// <summary>
    /// The unit file's directives, with comments and blank lines removed. Absence assertions must
    /// run against this and never against the raw text: every one of these files carries a comment
    /// explaining why a directive is <em>absent</em> — "NO [Install] SECTION", "Deliberately NOT
    /// Persistent=true", "OnCalendar, NOT OnUnitActiveSec" — so a substring search over the whole
    /// file matches the prose documenting the property and reports the opposite of the truth.
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

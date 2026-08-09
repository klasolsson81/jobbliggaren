using Jobbliggaren.Application.Common.Security;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #197 gate M-4 — <b>the drill and the runbook must not drift apart in silence.</b>
///
/// <para>
/// <c>BackupRestoreDrillTests</c> proves that <c>docs/runbooks/backup-restore.md</c> §5 works by
/// typing §5's own commands. That warrant lasts exactly as long as the two files agree. Edit the
/// runbook and the drill keeps proving the commands it still carries; edit the drill and it
/// stops proving the runbook. <b>Neither move fails anything</b>, which is what this class exists
/// to change: one fixed token list, asserted against both files.
/// </para>
///
/// <para>
/// <b>Why a token pin rather than executing the runbook's text</b> (senior-cto-advisor bind
/// 2026-08-09, D2). Making §5 executable would INVERT the gate: a runbook edit that weakened a
/// command would then make the drill pass, so the drill could never fail for a runbook
/// regression, which is the one thing this guard is for. A partial guard that cannot be fooled
/// beats a total guard that a documentation edit can quietly satisfy.
/// </para>
///
/// <para>
/// <b>Comments are stripped before matching, and that is load-bearing rather than tidy.</b> Two
/// of these tokens appear in §5 TWICE — once in a command and once in the prose explaining why
/// the command needs it (<c>ON_ERROR_STOP=1</c> at the "psql MUST RUN WITH" note, and the
/// ciphertext pattern at the "and not <c>&lt;&gt; ''</c>" note). A raw substring search over the
/// file would therefore pass against a runbook whose COMMAND had lost the flag while the comment
/// that explains it survived — reporting the opposite of the truth. This is
/// <c>BackupUnitFilePinTests.Directives()</c>'s lesson, in a second file and a second comment
/// syntax.
/// </para>
///
/// <para>
/// <b>What this does NOT measure, stated rather than implied:</b> token-level presence, not
/// semantic equivalence. Two files can carry every token below and still do different things —
/// a reordered pipeline, a different database, an extra flag. What it catches is the drift that
/// has actually happened to files in this repository: a flag deleted on one side of a pair.
/// </para>
/// </summary>
public class RestoreDrillRunbookParityTests
{
    private const string Runbook = "docs/runbooks/backup-restore.md";

    private const string Drill =
        "tests/Jobbliggaren.Worker.IntegrationTests/Backup/BackupRestoreDrillTests.cs";

    /// <summary>
    /// The load-bearing fragments of §5's restore procedure. Each one is a property whose loss
    /// has a named consequence:
    ///
    /// <list type="bullet">
    /// <item><c>ON_ERROR_STOP=1</c> — without it psql prints its error and exits 0, and a failed
    /// load reports success (#197 PR-1, measured).</item>
    /// <item><c>public._dek_restore</c> — SCHEMA-QUALIFIED. <c>pg_restore -f -</c> emits an empty
    /// <c>search_path</c>, so the unqualified name resolves to nothing and loads zero rows
    /// (#197 PR-1, measured).</item>
    /// <item><c>(LIKE user_data_keys)</c> — the staging table is created from the real table's
    /// shape, so a column added to <c>user_data_keys</c> travels rather than silently truncating
    /// the restore.</item>
    /// <item><c>--no-owner --no-privileges</c> — the restore target is a cluster that has never
    /// heard of <c>jobbliggaren_app</c>. Without these the restore fails with
    /// <c>role … does not exist</c>.</item>
    /// <item><c>job_seeker_id IN (SELECT id FROM job_seekers)</c> — the orphan drop. Without it
    /// the INSERT hits the FK and takes the whole load down with it.</item>
    /// </list>
    /// </summary>
    private static readonly string[] SharedTokens =
    [
        "ON_ERROR_STOP=1",
        "public._dek_restore",
        "(LIKE user_data_keys)",
        "--no-owner --no-privileges",
        "job_seeker_id IN (SELECT id FROM job_seekers)",
    ];

    [Fact]
    public void RunbookSection5_StillCarriesEveryCommandTheDrillExecutes()
    {
        var commands = CommandLines(ReadRepoFile(Runbook));

        foreach (var token in SharedTokens)
        {
            commands.ShouldContain(
                line => line.Contains(token, StringComparison.Ordinal),
                $"'{token}' is gone from backup-restore.md §5's COMMANDS. Either the runbook lost " +
                "a property the restore depends on, or it was rewritten and BackupRestoreDrillTests " +
                "no longer proves it. Both are findings; neither is a reason to delete this token.");
        }
    }

    [Fact]
    public void Drill_StillExecutesEveryCommandTheRunbookPrescribes()
    {
        var drill = ReadRepoFile(Drill);

        foreach (var token in SharedTokens)
        {
            drill.Contains(token, StringComparison.Ordinal).ShouldBeTrue(
                $"'{token}' is gone from the drill. The runbook still prescribes it, so gate M-4's " +
                "evidence would no longer come from executing what §5 says. This half of the pin " +
                "exists because a pin that reads only the runbook goes red when the RUNBOOK moves " +
                "and stays green when the DRILL does.");
        }
    }

    /// <summary>
    /// The ciphertext pattern is pinned differently on each side, because each side's correct
    /// spelling differs. The runbook is text an operator types, so it must carry the literal and
    /// that literal must equal production's constant. The drill is code, so it must reference the
    /// CONSTANT rather than a literal — a copy there would be a second truth that drifts.
    /// </summary>
    [Fact]
    public void CiphertextPattern_IsProductionsConstantInBothFiles()
    {
        var commands = CommandLines(ReadRepoFile(Runbook));

        commands.ShouldContain(
            line => line.Contains($"LIKE '{FieldEncryptionSentinel.SqlLikePattern}'", StringComparison.Ordinal),
            $"§5's evidence query (b2) must test for CIPHERTEXT using production's own pattern " +
            $"('{FieldEncryptionSentinel.SqlLikePattern}'). If FieldEncryptionSentinel changed, the " +
            "runbook an operator types is now wrong and (b2) would count the wrong users.");

        ReadRepoFile(Drill)
            .Contains($"{nameof(FieldEncryptionSentinel)}.{nameof(FieldEncryptionSentinel.SqlLikePattern)}",
                StringComparison.Ordinal)
            .ShouldBeTrue(
                "the drill must reference the production constant rather than repeat its value, so " +
                "that a change to the sentinel moves the drill and the runbook pin together.");
    }

    /// <summary>
    /// §5's command lines: fenced-block content with shell (<c>#</c>) and SQL (<c>--</c>) comment
    /// lines removed. See the class docblock for why the removal is the point.
    ///
    /// <para>
    /// Scoped to §5 rather than the whole file: §2's install block and §4's weekly check also
    /// contain <c>rclone</c> and <c>psql</c> lines, and a token that drifted out of §5 into one
    /// of those would otherwise still be "found".
    /// </para>
    /// </summary>
    private static List<string> CommandLines(string runbookText)
    {
        var lines = runbookText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        var start = Array.FindIndex(lines, l => l.StartsWith("## 5. Restore", StringComparison.Ordinal));
        start.ShouldBeGreaterThanOrEqualTo(0,
            "backup-restore.md no longer has a '## 5. Restore' heading — this pin cannot scope " +
            "itself, and an unscoped search is not the measurement it claims to be.");

        var end = Array.FindIndex(lines, start + 1, l => l.StartsWith("## ", StringComparison.Ordinal));
        if (end < 0)
        {
            end = lines.Length;
        }

        return [.. lines[start..end]
            .Select(line => line.TrimStart('>', ' ', '\t'))
            .Where(line => line.Length > 0
                           && !line.StartsWith('#')
                           && !line.StartsWith("--", StringComparison.Ordinal)
                           && !line.StartsWith("```", StringComparison.Ordinal))];
    }

    private static string ReadRepoFile(string relativePath)
    {
        var full = Path.Combine(RepositoryRoot(), relativePath);
        File.Exists(full).ShouldBeTrue($"{relativePath} does not exist at {full}");
        return File.ReadAllText(full);
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

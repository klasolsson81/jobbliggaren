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
/// to change: two fixed rule sets, each asserted against BOTH files.
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
/// <b>Comments are stripped from BOTH files before matching, and that is load-bearing rather than
/// tidy.</b> Several of these fragments appear twice — once in a command and once in the prose
/// explaining why the command needs it. A raw substring search would therefore pass against a file
/// whose COMMAND had lost a property while the comment explaining it survived, reporting the
/// opposite of the truth. This is <c>BackupUnitFilePinTests.Directives()</c>'s lesson, applied to
/// two more files and two more comment syntaxes.
/// </para>
///
/// <para>
/// <b>Presence alone was measured insufficient, which is why there are two rule sets.</b> Until
/// 2026-08-09 both files contained the string <c>ON_ERROR_STOP=1</c> while the property it names
/// was absent from both: §5's step-5 script lacked the flag where it decides everything, and the
/// drill carried it on a single-statement <c>psql -c</c> where it is inert. A presence pin
/// reported parity over an absent property. <see cref="CoLocated"/> is the answer, and it is a
/// UNIVERSAL claim over every line of a given shape rather than an existential one.
/// </para>
///
/// <para>
/// <b>What this still does NOT measure, stated rather than implied:</b> semantic equivalence. Two
/// files can satisfy every rule below and still do different things — a reordered pipeline, a
/// different database, an extra flag. What it catches is the drift that has actually happened to
/// files in this repository: a property deleted from one side of a pair.
/// </para>
/// </summary>
public partial class RestoreDrillRunbookParityTests
{
    private const string Runbook = "docs/runbooks/backup-restore.md";

    private const string Drill =
        "tests/Jobbliggaren.Worker.IntegrationTests/Backup/BackupRestoreDrillTests.cs";

    /// <summary>
    /// Fragments whose mere presence somewhere in the command set is the property. Anything whose
    /// property is about WHERE it sits belongs in <see cref="CoLocated"/> instead.
    ///
    /// <list type="bullet">
    /// <item><c>public._dek_restore</c> — SCHEMA-QUALIFIED. <c>pg_restore -f -</c> emits an empty
    /// <c>search_path</c>, so the unqualified name resolves to nothing and loads zero rows
    /// (#197 PR-1, measured).</item>
    /// <item>the <c>sed</c> pattern's left-hand side — the substitution has two sides and the
    /// schema-qualification defect had two sides too; pinning only the replacement leaves the
    /// half that selects what gets replaced unheld.</item>
    /// <item><c>(LIKE user_data_keys)</c> — the staging table is created from the real table's
    /// shape, so a column added to <c>user_data_keys</c> travels rather than silently truncating
    /// the restore.</item>
    /// <item><c>SELECT count(*) FROM _dek_restore</c> — §5's DIRECT repair for #197 PR-1: the
    /// check that the load itself happened, which the two <c>grep</c>s cannot see. Its absence is
    /// what let a zero-key restore report a flawless result.</item>
    /// <item><c>job_seeker_id IN (SELECT id FROM job_seekers)</c> — the orphan drop. Without it
    /// the INSERT hits the FK and takes the whole load down with it.</item>
    /// </list>
    /// </summary>
    private static readonly string[] SharedTokens =
    [
        "public._dek_restore",
        "^COPY public\\.user_data_keys ",
        "(LIKE user_data_keys)",
        "SELECT count(*) FROM _dek_restore",
        "job_seeker_id IN (SELECT id FROM job_seekers)",
    ];

    /// <summary>
    /// Properties that must hold ON THE SAME LINE, because presence alone cannot express them.
    ///
    /// <para>
    /// <b>This is the half a token list could not carry, and its absence let a real defect
    /// through.</b> Until 2026-08-09 §5's step-5 script ran the INSERT and all three evidence
    /// queries in one <c>psql</c> invocation with no <c>ON_ERROR_STOP</c> — the one shape where its
    /// absence is catastrophic — while the drill attached the flag to a single-statement
    /// <c>psql -c</c>, where it is inert. Both files contained the token; the property was absent
    /// from both; a presence pin reported parity. Requiring co-location is what makes the pin able
    /// to fail for the thing it exists to catch.
    /// </para>
    /// </summary>
    /// <remarks>
    /// The quantifier is UNIVERSAL, not existential, and that is the whole difference. "Some psql
    /// line carries the flag" was true of both files while the flag was missing from the one
    /// invocation that needed it. "Every script-fed psql line carries it" is false the moment any
    /// of them loses it.
    /// </remarks>
    private static readonly (string Name, Func<string, bool> Applies, string Required, string Why)[] CoLocated =
    [
        ("a script-fed psql invocation",
            line => line.Contains("psql", StringComparison.Ordinal)
                    && (line.Contains(" -f ", StringComparison.Ordinal)
                        || line.Contains("<<", StringComparison.Ordinal)),
            "-v ON_ERROR_STOP=1",
            "a psql fed a SCRIPT continues past a failed statement and exits 0, so later statements " +
            "report against state the failed one never produced. That is #197 PR-1's defect, and " +
            "then again at §5 step 5. A single-statement `psql -c` fails loudly either way, which " +
            "is exactly why presence of the token elsewhere proves nothing"),

        ("a pg_restore into a database",
            line => line.Contains("pg_restore", StringComparison.Ordinal)
                    && line.Contains(" -d ", StringComparison.Ordinal),
            "--no-owner --no-privileges",
            "the restore target is a cluster that has never heard of production's roles, so without " +
            "these the restore fails with `role \"jobbliggaren_migrations\" does not exist`. These " +
            "are the RESTORE's copies — measured to be the load-bearing pair; pg_dump's own -O is " +
            "documented as ignored for archive formats"),
    ];

    [Fact]
    public void RunbookSection5_StillCarriesEveryCommandTheDrillExecutes()
    {
        AssertTokens(CommandLines(ReadRepoFile(Runbook)), "backup-restore.md §5's COMMANDS");
        AssertCoLocation(CommandLines(ReadRepoFile(Runbook)), "backup-restore.md §5");
    }

    [Fact]
    public void Drill_StillExecutesEveryCommandTheRunbookPrescribes()
    {
        AssertTokens(DrillCommandLines(), "the drill");
        AssertCoLocation(DrillCommandLines(), "the drill");
    }

    private static void AssertTokens(List<string> lines, string where)
    {
        foreach (var token in SharedTokens)
        {
            lines.ShouldContain(
                line => line.Contains(token, StringComparison.Ordinal),
                $"'{token}' is gone from {where}. Either a property the restore depends on was " +
                "lost, or one side was rewritten and the other no longer proves it. Both are " +
                "findings; neither is a reason to delete this token.");
        }
    }

    private static void AssertCoLocation(List<string> lines, string where)
    {
        foreach (var (name, applies, required, why) in CoLocated)
        {
            var carriers = lines.Where(applies).ToList();

            // An empty carrier set would make the universal claim below vacuously true, which is
            // this repo's standing rule: an absence must never read as a verdict.
            carriers.ShouldNotBeEmpty(
                $"{where} contains no line matching '{name}' at all, so this pin measured nothing. " +
                "Either the shape moved or the scoping is wrong; neither is a pass.");

            var missing = carriers
                .Where(line => !line.Contains(required, StringComparison.Ordinal))
                .ToList();

            missing.ShouldBeEmpty(
                $"{name} in {where} is missing '{required}'. {why}. Offending line(s): " +
                string.Join(" | ", missing));
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
                           && !SqlComment().IsMatch(line)
                           && !line.StartsWith("```", StringComparison.Ordinal))];
    }

    /// <summary>
    /// The drill's command lines: its source with <c>//</c> comment lines removed.
    ///
    /// <para>
    /// <b>The same stripping as the runbook half, and for a sharper reason.</b> This file is
    /// roughly two-thirds comment, and its prose quotes the very commands it runs. Matching over
    /// the raw text would let a drill that lost <c>-v ON_ERROR_STOP=1</c> from a command stay green
    /// on a comment that explains why the flag is needed — the exact inversion this class's
    /// runbook half exists to prevent, on the half it had not been applied to.
    /// </para>
    /// </summary>
    private static List<string> DrillCommandLines() =>
        [.. ReadRepoFile(Drill)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0
                           && !line.StartsWith("//", StringComparison.Ordinal)
                           && !line.StartsWith("///", StringComparison.Ordinal))];

    /// <summary>
    /// A SQL comment line, which is <c>--</c> followed by whitespace.
    ///
    /// <para>
    /// The whitespace is not pedantry. A bare <c>StartsWith("--")</c> also matches a wrapped shell
    /// long option — <c>  --no-owner --no-privileges \</c> on a continuation line — so a purely
    /// cosmetic reflow of §5 would drop a real command from the corpus and this pin would report
    /// "the runbook lost a property the restore depends on" about a correct runbook. Every SQL
    /// comment in §5 is <c>--</c> plus a space; every long option is <c>--</c> plus a letter.
    /// </para>
    /// </summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"^--\s")]
    private static partial System.Text.RegularExpressions.Regex SqlComment();

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

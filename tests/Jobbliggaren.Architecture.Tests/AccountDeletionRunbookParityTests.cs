using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1746 (test-writer Minor 6): <c>AccountDeletionOperatorPathDrillTests</c> runs its own copy of
/// <c>docs/runbooks/account-deletion.md</c> §4.3 step 2 and proves the runbook only while the two agree. The copy must
/// be the runbook's statement but for its two placeholders, which the drill binds as parameters; whitespace is not
/// compared. The runbook's text is never executed, as for the restore drill (senior-cto-advisor, 2026-08-09, D2).
/// </summary>
public partial class AccountDeletionRunbookParityTests
{
    [Fact]
    public void The_drill_runs_the_runbooks_statement_but_for_its_placeholders()
    {
        var root = RepoRoot();
        var runbook = File.ReadAllText(Path.Combine(root, "docs", "runbooks", "account-deletion.md"));
        var start = runbook.IndexOf("### 4.3 ", StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, "account-deletion.md has no §4.3");
        var section = runbook[start..];
        var end = section.IndexOf("\n## ", StringComparison.Ordinal);
        section = end < 0 ? section : section[..end];
        var statement = SqlBlocks().Matches(section).Select(m => m.Groups["sql"].Value)
            .Where(sql => sql.Contains("WITH deleted AS (", StringComparison.Ordinal))
            .ShouldHaveSingleItem();

        var drill = File.ReadAllText(Path.Combine(
            root, "tests", "Jobbliggaren.Worker.IntegrationTests", "Auth", "AccountDeletionOperatorPathDrillTests.cs"));
        var copy = DrillStatement().Match(drill);
        copy.Success.ShouldBeTrue("the drill carries no Statement");

        Normalised(copy.Groups["sql"].Value).ShouldBe(Normalised(statement
            .Replace("'<jobSeekerId>'::uuid", "@jobSeekerId", StringComparison.Ordinal)
            .Replace(":'adress'", "@adress", StringComparison.Ordinal)));
    }

    private static string Normalised(string sql) => Whitespace().Replace(sql, " ").Trim();

    [GeneratedRegex(@"```sql\r?\n(?<sql>.*?)```", RegexOptions.Singleline)]
    private static partial Regex SqlBlocks();

    [GeneratedRegex(""""Statement = """\r?\n(?<sql>.*?)""";"""", RegexOptions.Singleline)]
    private static partial Regex DrillStatement();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    // thisFile = <repo>/tests/Jobbliggaren.Architecture.Tests/AccountDeletionRunbookParityTests.cs → up two = repo root.
    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}

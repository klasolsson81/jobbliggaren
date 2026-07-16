using System.Text.RegularExpressions;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #560 PR-3 (C-D8 / CTO Fork G1, 2026-07-16) — user delete of a <c>CompanyWatchCriterion</c> is
/// HARD (tracked <c>Remove</c>, the #782 template). The aggregate's <c>SoftDelete</c> method, the
/// <c>DeletedAt</c> property and the EF query filter are retained ONLY until the follow-up
/// schema-cleanup migration removes them — they must have <b>zero production callers</b> in the
/// meantime. A live-looking delete mechanism production never runs is the #868 decoy class
/// (<c>UserJobAdMatch.SoftDelete()</c> had zero callers while its query filter was credited as the
/// exclusion mechanism — #821/#805-3); this guard makes the deadness fail-closed instead of
/// documented-only, until the demolition PR lands and deletes both the method and this file.
/// </summary>
public class CompanyWatchCriterionSoftDeleteDecoyTests
{
    [Fact]
    public void CompanyWatchCriterion_SoftDelete_HasNoProductionCaller()
    {
        var srcRoot = Path.Combine(FindRepoRoot(), "src");
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            // Generated build artifacts are not source.
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            // The definition itself is allowed to exist (its summary carries the verdict).
            if (file.EndsWith(
                    Path.Combine("CompanyWatches", "CompanyWatchCriterion.cs"),
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // COMMENTS AND STRING LITERALS ARE STRIPPED FIRST — the guard reads code, not prose.
            // First deployment flagged ErasureCascadeRegistry.cs, whose only ".SoftDelete(" sat in
            // a runtime MESSAGE STRING ("SavedSearch.SoftDelete() leaves `criteria` in the row…")
            // inside a file that legitimately names CompanyWatchCriteria in its Art. 17 table. A
            // method CALL can never live inside a literal, so literals only carry false positives
            // — and a guard that fires on documentation trains people to delete documentation.
            var source = StripCommentsAndStrings(File.ReadAllText(file));

            // A CALL requires both: the criterion type in scope and a .SoftDelete( invocation in
            // the same file's CODE. CompanyWatch.SoftDelete stays legitimate — files that touch
            // only the org.nr follow (UnfollowCompanyCommandHandler) never name the criterion and
            // pass. A file whose CODE handles both aggregates and calls .SoftDelete( trips this
            // guard and earns a human look — that is the guard working, not a false positive to
            // widen away.
            if (source.Contains("CompanyWatchCriteri", StringComparison.Ordinal)
                && source.Contains(".SoftDelete(", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetRelativePath(srcRoot, file));
            }
        }

        offenders.ShouldBeEmpty(
            "CompanyWatchCriterion.SoftDelete är dokumenterad-till-demontering (C-D8/G1: user "
            + "delete är HARD) och får inte få en produktionsanropare. Filer som både nämner "
            + $"kriteriet och anropar .SoftDelete(: {string.Join(", ", offenders)}");
    }

    // Removes what code never lives in: string literals (raw """…""", verbatim @"…", regular
    // "…" — in that order, so an inner quote form is not half-eaten by an outer pattern), then
    // block comments, then line comments. All non-greedy so two separate literals/comments cannot
    // swallow the code between them. Literals go FIRST — a // inside a string is not a comment,
    // and a string stripped later could otherwise resurrect one.
    private static string StripCommentsAndStrings(string source)
    {
        var s = Regex.Replace(source, "\"\"\".*?\"\"\"", "\"\"", RegexOptions.Singleline);
        s = Regex.Replace(s, "@\"(?:[^\"]|\"\")*\"", "\"\"");
        s = Regex.Replace(s, "\"(?:[^\"\\\\\\r\\n]|\\\\.)*\"", "\"\"");
        s = Regex.Replace(s, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(s, @"//[^\r\n]*", "");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;

        dir.ShouldNotBeNull(
            "kunde inte hitta repo-roten (CLAUDE.md) uppåt från test-bin — guarden behöver "
            + "källträdet för källtext-scan");
        return dir!.FullName;
    }
}

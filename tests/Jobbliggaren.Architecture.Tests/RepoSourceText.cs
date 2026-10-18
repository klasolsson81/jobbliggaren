using System.Text.RegularExpressions;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// Shipped source files read as text, shared by <see cref="SuggestionKindWireContractTests"/> and
/// <see cref="NotificationFeedSourceKindWireContractTests"/>.
/// </summary>
internal static class RepoSourceText
{
    /// <summary>The quoted names in group 1 of <paramref name="anchorPattern"/>, comments dropped.</summary>
    internal static string[] QuotedList(string relativePath, string listName, string anchorPattern)
    {
        var source = File.ReadAllText(
            Path.Combine(
                Root(),
                relativePath.Replace('/', Path.DirectorySeparatorChar)));

        var body = Regex.Match(source, anchorPattern).Groups[1];

        if (!body.Success)
            throw new InvalidOperationException(
                $"Could not read {listName} out of {relativePath}. It was "
                + "renamed or reshaped - re-make this join deliberately, do not delete it.");

        // Drop comments before harvesting quoted names, so a commented-out member cannot be read
        // as a live one. BOTH forms: an earlier version of this method stripped `//` only, and
        // test-writer measured that a block-commented member then passed every fact in
        // SuggestionKindWireContractTests while the frontend decode table had silently lost it — the
        // exact failure this stripping exists to prevent, in the class whose whole subject is that failure.
        var withoutComments = Regex.Replace(
            body.Value, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        withoutComments = Regex.Replace(withoutComments, @"//[^\n]*", string.Empty);

        var names = Regex.Matches(withoutComments, "\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .ToArray();

        if (names.Length == 0)
            throw new InvalidOperationException(
                $"{listName} was found in {relativePath} but yielded no member "
                + "names. The array shape changed - re-make this join deliberately.");

        return names;
    }

    internal static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;

        dir.ShouldNotBeNull(
            "could not find the repo root (CLAUDE.md) walking up from the test bin - this class "
            + "needs the source tree for its cross-language source-text scan");
        return dir!.FullName;
    }
}

using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1810, #1803 — <c>CvExtractionResult.AuxiliaryText</c> and <c>RevisionText</c> are read by the personnummer
/// scan alone, through <c>ScanText</c> (ADR 0074 Amendment 2026-09-23). Segmentation and persistence read
/// <c>RawText</c>; a second reader would carry a header's text, or text a tracked change deleted, where the review
/// never shows it.
/// </summary>
public class ScanOnlyExtractionChannelTests
{
    private const string ResultFile = "Jobbliggaren.Application/Resumes/Abstractions/CvExtractionResult.cs";

    private static readonly Regex MemberAccess = new(@"\.(AuxiliaryText|RevisionText)\b", RegexOptions.Compiled);

    private static readonly Regex AnyMention = new(@"\b(AuxiliaryText|RevisionText)\b", RegexOptions.Compiled);

    [Fact]
    public void No_production_code_reads_the_scan_only_channels_by_member_access()
    {
        var readers = new List<string>();
        foreach (var (relative, text) in SourceFiles())
        {
            foreach (Match match in MemberAccess.Matches(StripComments(text)))
                readers.Add(relative + ": " + match.Value);
        }

        readers.ShouldBeEmpty();
    }

    [Fact]
    public void The_result_type_mentions_the_scan_only_channels_only_in_its_declaration_and_ScanText()
    {
        var text = StripComments(File.ReadAllText(Path.Combine(SourceRoot(), ResultFile)));
        var scanText = Regex.Match(text, @"public string ScanText =>[^;]*;", RegexOptions.Singleline);
        scanText.Success.ShouldBeTrue("ScanText saknas i CvExtractionResult.");
        AnyMention.Count(scanText.Value).ShouldBeGreaterThan(0);

        var rest = text.Remove(scanText.Index, scanText.Length)
            .Replace("string AuxiliaryText,", string.Empty, StringComparison.Ordinal)
            .Replace("string RevisionText)", string.Empty, StringComparison.Ordinal);

        AnyMention.Matches(rest).Select(match => match.Value).ShouldBeEmpty();
    }

    private static IEnumerable<(string Relative, string Text)> SourceFiles()
    {
        var root = SourceRoot();
        foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;

            yield return (Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'), File.ReadAllText(path));
        }
    }

    // A doc comment may name a member in a cref, and a comment reads nothing.
    private static string StripComments(string text) => Regex.Replace(text, @"//[^\r\n]*", string.Empty);

    private static string SourceRoot()
    {
        var root = Path.Combine(RepoRoot(), "src");
        Directory.Exists(root).ShouldBeTrue($"Hittade inte src-roten: {root}");
        return root;
    }

    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}

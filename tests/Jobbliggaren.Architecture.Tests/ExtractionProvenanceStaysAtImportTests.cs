using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #2353: the extractor's per-line geometry (<c>CvTextProvenance</c>, <c>CvLineProvenance</c>) is
/// CV-derived and exists at import only. The extractor builds it, the import hands it on and the
/// segmenter reads it. Code anywhere else naming the types could persist, log or return it.
/// </summary>
public class ExtractionProvenanceStaysAtImportTests
{
    private static readonly Regex Mention = new(@"\bCv(Text|Line)Provenance\b", RegexOptions.Compiled);

    private static readonly string[] AllowedFolders =
    [
        "Jobbliggaren.Application/Resumes/Abstractions/",
        "Jobbliggaren.Application/Resumes/Commands/ImportResume/",
        "Jobbliggaren.Infrastructure/Resumes/Parsing/",
    ];

    [Fact]
    public void Only_the_extraction_port_the_import_and_the_parser_name_the_provenance_types()
    {
        var outside = new List<string>();
        foreach (var (relative, text) in SourceFiles())
        {
            if (AllowedFolders.Any(folder => relative.StartsWith(folder, StringComparison.Ordinal)))
                continue;

            foreach (Match match in Mention.Matches(StripComments(text)))
                outside.Add(relative + ": " + match.Value);
        }

        outside.ShouldBeEmpty();
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

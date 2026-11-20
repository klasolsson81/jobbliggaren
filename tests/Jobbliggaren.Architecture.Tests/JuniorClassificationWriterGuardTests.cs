using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #2264 (ADR 0187, senior-cto-advisor's ruling) — who may write the junior assessment and who may run the junior rules,
/// pinned so a second writer or a request-path classifier is a red build, not a review comment.
/// <list type="bullet">
/// <item>Only the Worker's sweep creates and re-classifies an assessment.</item>
/// <item>Only the admin decide command decides.</item>
/// <item>Only the sweep runs the rules (through <c>JuniorFieldClassifier</c>, the one wrapper over <c>JuniorClassifier</c>):
/// no regex runs on a request path. The phase-0 measurement in <c>tests/Jobbliggaren.QA.Corpus</c> is not production code
/// and is outside this scan.</item>
/// </list>
/// A source scan over <c>src/</c>, comments stripped, in the form of <c>AddressSwapCallerTests</c>.
/// </summary>
public sealed class JuniorClassificationWriterGuardTests
{
    private const string Sweep = "Jobbliggaren.Application/JobAds/Jobs/ClassifyJuniorJobAds/ClassifyJuniorJobAdsJob.cs";
    private const string DecideHandler =
        "Jobbliggaren.Application/Admin/JuniorJobAds/Commands/DecideJuniorJobAd/DecideJuniorJobAdCommandHandler.cs";
    private const string Assessment = "Jobbliggaren.Domain/JobAds/JobAdJuniorAssessment.cs";
    private const string FieldClassifier = "Jobbliggaren.Application/JobAds/Junior/JuniorFieldClassifier.cs";

    [Fact]
    public void Only_the_sweep_creates_or_reclassifies_an_assessment() =>
        // The aggregate's own Classify calls Reclassify; every other hit is the sweep.
        FilesMatching(@"\bJobAdJuniorAssessment\s*\.\s*Classify\s*\(|\.\s*Reclassify\s*\(")
            .ShouldBe([Sweep, Assessment]);

    [Fact]
    public void Only_the_decide_command_decides() =>
        // A file that names the assessment and calls a Decide method on something: the one handler.
        FilesMatching(@"\.\s*Decide\s*\(", alsoMatching: @"\bJuniorAssessment")
            .ShouldBe([DecideHandler]);

    [Fact]
    public void Only_the_field_classifier_runs_the_junior_rules() =>
        FilesMatching(@"\bJuniorClassifier\s*\.\s*(Classify|Evaluate)\s*\(")
            .ShouldBe([FieldClassifier]);

    [Fact]
    public void Only_the_sweep_builds_a_field_classifier() =>
        FilesMatching(@"\bnew\s+JuniorFieldClassifier\s*\(")
            .ShouldBe([Sweep]);

    private static List<string> FilesMatching(string pattern, string? alsoMatching = null)
    {
        var srcRoot = Path.Combine(RepoRoot(), "src");
        Directory.Exists(srcRoot).ShouldBeTrue($"src root not found: {srcRoot}");

        return Directory
            .EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Select(path => (Path: path, Code: StripComments(File.ReadAllText(path))))
            .Where(file => Regex.IsMatch(file.Code, pattern)
                           && (alsoMatching is null || Regex.IsMatch(file.Code, alsoMatching)))
            .Select(file => Path.GetRelativePath(srcRoot, file.Path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static string StripComments(string code) =>
        Regex.Replace(code, @"//[^\n]*|/\*[\s\S]*?\*/", string.Empty);

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    // thisFile = <repo>/tests/Jobbliggaren.Architecture.Tests/<this file> → up two = repo root.
    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}

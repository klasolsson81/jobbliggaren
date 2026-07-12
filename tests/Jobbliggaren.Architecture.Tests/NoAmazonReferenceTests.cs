using System.Collections.Generic;
using System.IO;
using System.Linq;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #802 / ADR 0066 — ratchet som håller AWS-exiten permanent. Efter att den
/// AWS-KMS-baserade DEK-providern togs bort är fält-krypteringen Local-only
/// (LocalDataKeyProvider) och lösningen har 0 Amazon-paket. Detta test
/// fail-fastar om någon återinför en AWSSDK-/Amazon-paketreferens eller ett
/// <c>using Amazon</c>-import i produktionskod.
///
/// En NetArchTest-<c>HaveDependencyOn</c> duger inte — när paketet är borta kan
/// typen inte ens refereras, så det finns inget beroende att asserta emot. En
/// csproj-/källtext-scan är rätt mekanism (samma mönster som husets övriga
/// källtext-guardrail-tester). Scannar bara paket-<b>element</b>
/// (<c>PackageReference</c>/<c>PackageVersion Include="AWSSDK.../Amazon..."</c>),
/// aldrig kommentarer eller prosa som nämner Amazon historiskt.
/// </summary>
public class NoAmazonReferenceTests
{
    private static readonly string[] ScannedRoots = ["src", "tests"];

    [Fact]
    public void NoProjectReferencesAnAmazonPackage()
    {
        var repoRoot = FindRepoRoot();

        var projectFiles = ScannedRoots
            .Select(sub => Path.Combine(repoRoot, sub))
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
            .Append(Path.Combine(repoRoot, "Directory.Packages.props"))
            .Where(File.Exists)
            .Where(p => !IsUnderBinOrObj(p))
            .ToArray();

        var offenders = new List<string>();
        foreach (var file in projectFiles)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var isPackageElement =
                    line.Contains("PackageReference", StringComparison.Ordinal)
                    || line.Contains("PackageVersion", StringComparison.Ordinal);
                var namesAmazon =
                    line.Contains("Include=\"AWSSDK", StringComparison.Ordinal)
                    || line.Contains("Include=\"Amazon", StringComparison.Ordinal);
                if (isPackageElement && namesAmazon)
                {
                    offenders.Add($"{RelativeTo(repoRoot, file)}:{i + 1}");
                }
            }
        }

        offenders.ShouldBeEmpty(
            "AWS-exiten är slutförd (#802 / ADR 0066) — inga Amazon/AWSSDK-paket-" +
            "referenser får återinföras. Fält-krypteringen är Local-only " +
            "(LocalDataKeyProvider); prod-master-nyckelns skydd är TD-102, ej KMS.");
    }

    [Fact]
    public void NoProductionSourceFileImportsAnAmazonNamespace()
    {
        var repoRoot = FindRepoRoot();
        var srcRoot = Path.Combine(repoRoot, "src");

        var offenders = Directory
            .EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(p => !IsUnderBinOrObj(p))
            .Where(file => File.ReadLines(file).Any(line =>
                line.TrimStart().StartsWith("using Amazon", StringComparison.Ordinal)))
            .Select(file => RelativeTo(repoRoot, file))
            .ToArray();

        offenders.ShouldBeEmpty(
            "ingen produktions-källfil får importera en Amazon-namespace " +
            "(#802 / ADR 0066 — AWS-fri fält-kryptering).");
    }

    private static bool IsUnderBinOrObj(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string RelativeTo(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;

        dir.ShouldNotBeNull(
            "kunde inte hitta repo-roten (CLAUDE.md) uppåt från test-bin — arch-testet " +
            "behöver källträdet för källtext-scan");
        return dir!.FullName;
    }
}

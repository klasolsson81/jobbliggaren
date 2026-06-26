using System.Runtime.CompilerServices;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// Fitness function (#239 / TD-63 kind-union): production code must construct a
/// <c>DomainError</c> ONLY through its factories (<c>NotFound</c> / <c>Validation</c> /
/// <c>Conflict</c> / <c>Gone</c>), never via the raw <c>new DomainError(...)</c> constructor.
///
/// <para>The factories stamp <c>ErrorKind</c>, which the central API mapper
/// (<c>DomainErrorResults.ToProblemResult</c>) translates to an HTTP status. A raw construction
/// silently defaults to <see cref="Jobbliggaren.Domain.Common.ErrorKind.Validation"/> (the 400
/// floor) — exactly the latent mis-kind that shipped a genuine not-found
/// (<c>Application.FollowUpNotFound</c>) as 400 instead of 404 until #239 fixed it. Forbidding raw
/// construction makes the kind-stamping invariant structural, not conventional.</para>
///
/// <para>NetArchTest analyses type DEPENDENCIES, not constructor CALL-SITES, so it cannot express
/// this rule; instead this is a source-text scan anchored to the repo root via
/// <see cref="CallerFilePathAttribute"/> (compile-time path of this file). The factory file itself
/// builds via target-typed <c>new(...)</c>, so it is not a match — but it is excluded for clarity
/// of intent ("the sanctioned construction site").</para>
///
/// <para>Known limitations (acceptable for a fitness function): (1) the scan is a substring match, so a
/// future doc-comment or string literal in a non-factory <c>src</c> file that quotes the forbidden
/// pattern would trip it — itself a mild smell. (2) The repo-root anchor assumes the
/// <c>tests/&lt;project&gt;/&lt;file&gt;</c> depth; relocating this file needs the <c>RepoRoot</c> hop
/// adjusted (the <c>src</c>-exists guard catches gross breakage, not an off-by-one).</para>
/// </summary>
public class DomainErrorConstructionTests
{
    private const string RawConstruction = "new DomainError(";
    private const string FactoryFile = "DomainError.cs";

    [Fact]
    public void No_production_code_constructs_DomainError_via_the_raw_constructor()
    {
        var srcRoot = Path.Combine(RepoRoot(), "src");
        Directory.Exists(srcRoot).ShouldBeTrue($"Hittade inte src-roten: {srcRoot}");

        var offenders = Directory
            .EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsGeneratedOrFactory(path))
            .Where(path => File.ReadAllText(path).Contains(RawConstruction, StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(srcRoot, path))
            .OrderBy(path => path)
            .ToList();

        offenders.ShouldBeEmpty(
            "Rå DomainError-konstruktion i produktionskod defaultar tyst till ErrorKind.Validation " +
            "(400) och kringgår den centrala kind→status-mappningen (DomainErrorResults). Använd en " +
            "factory: DomainError.NotFound / Validation / Conflict / Gone. Brytande filer: " +
            string.Join(", ", offenders));
    }

    private static bool IsGeneratedOrFactory(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
        Path.GetFileName(path).Equals(FactoryFile, StringComparison.Ordinal);

    // thisFile = <repo>/tests/Jobbliggaren.Architecture.Tests/DomainErrorConstructionTests.cs
    // → up two directories (Architecture.Tests, tests) = repo root, regardless of bin layout.
    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}

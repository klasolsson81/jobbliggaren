using System.IO;
using System.Text.RegularExpressions;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #2353 — the CV import's two header vocabularies each have one predicate that asks them, enforced
/// rather than agreed, as <see cref="NameBannerSingleOwnerGuardTests"/> does for the banner set.
///
/// <para><b>The place lexicon.</b> <c>ContactPatterns.IsBarePlace</c> and <c>TryBarePlaceLine</c>
/// carry the normalisation every place question needs. The contact block, the preamble residue and
/// the entry-boundary reader all ask through them; a call site that asks
/// <c>CvPlaceLexicon.Contains</c> on its own text would normalise differently and could disagree
/// about the same city, which is how #898's banner fork happened.</para>
///
/// <para><b>The organisation forms.</b> <c>CvParsingLexiconData.IsOrganisation</c> reads a field
/// through the same normaliser the loader stores the forms through. Asking the sets from anywhere
/// else skips it.</para>
/// </summary>
public partial class CvVocabularySingleOwnerGuardTests
{
    private const string ParsingFolder = "src/Jobbliggaren.Infrastructure/Resumes/Parsing/";

    [Fact]
    public void ThePlaceLexicon_IsOnlyAskedThroughContactPatterns()
    {
        string[] allowed = [ParsingFolder + "CvPlaceLexicon.cs", ParsingFolder + "ContactPatterns.cs"];

        var (scanned, owners, offenders) = Scan(text =>
            PlaceLexiconDeclaration().Matches(text)
                .Select(m => m.Groups["name"].Value)
                .Distinct(StringComparer.Ordinal)
                .Any(name => Regex.IsMatch(text, $@"\b{Regex.Escape(name)}\s*\.\s*Contains\s*\(",
                    RegexOptions.CultureInvariant)),
            allowed);

        scanned.ShouldBeGreaterThan(100, "källscanningen hittade nästan inga filer — den vaktar då ingenting.");
        owners.ShouldContain(ParsingFolder + "ContactPatterns.cs",
            "hittade ingen platsfråga i ContactPatterns.cs — antingen har ägaren flyttat (uppdatera " +
            "listan) eller så matchar inte mönstret längre, och testet skulle passera oavsett.");
        offenders.ShouldBeEmpty(
            "Dessa filer frågar CvPlaceLexicon.Contains direkt i stället för via " +
            $"ContactPatterns.IsBarePlace/TryBarePlaceLine: {string.Join(", ", offenders)}. Normaliseringen " +
            "bor i predikatet, och ett anrop som normaliserar själv kan läsa samma ort olika.");
    }

    [Fact]
    public void TheOrganisationForms_AreOnlyAskedByIsOrganisation()
    {
        string[] allowed = [ParsingFolder + "CvParsingLexicon.cs"];

        var (scanned, owners, offenders) = Scan(OrganisationFormsQuery().IsMatch, allowed);

        scanned.ShouldBeGreaterThan(100, "källscanningen hittade nästan inga filer — den vaktar då ingenting.");
        owners.ShouldContain(ParsingFolder + "CvParsingLexicon.cs",
            "hittade ingen fråga mot organisationsformerna i CvParsingLexicon.cs — antingen har ägaren " +
            "flyttat (uppdatera listan) eller så matchar inte mönstret längre.");
        offenders.ShouldBeEmpty(
            "Dessa filer frågar OrganisationSuffixes/OrganisationPrefixes direkt i stället för via " +
            $"CvParsingLexiconData.IsOrganisation: {string.Join(", ", offenders)}.");
    }

    private static (int Scanned, List<string> Owners, List<string> Offenders) Scan(
        Func<string, bool> asks, string[] allowed)
    {
        var repoRoot = FindRepoRoot();
        var srcRoot = Path.Combine(repoRoot, "src");
        Directory.Exists(srcRoot).ShouldBeTrue($"källträdet saknas ({srcRoot}).");

        var scanned = 0;
        var owners = new List<string>();
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            scanned++;
            var relative = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            if (!asks(File.ReadAllText(file)))
                continue;

            (allowed.Contains(relative, StringComparer.Ordinal) ? owners : offenders).Add(relative);
        }

        return (scanned, owners, offenders);
    }

    // A field, parameter or positional member typed as the place lexicon: "CvPlaceLexicon places".
    [GeneratedRegex(@"\bCvPlaceLexicon\s+(?<name>[A-Za-z_]\w*)\b(?!\s*\()", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceLexiconDeclaration();

    [GeneratedRegex(@"\bOrganisation(Suffixes|Prefixes)\s*\.\s*(Contains|Any|Where|FirstOrDefault|Count)\s*\(",
        RegexOptions.CultureInvariant)]
    private static partial Regex OrganisationFormsQuery();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;

        dir.ShouldNotBeNull("kunde inte hitta repo-roten (CLAUDE.md) uppåt från test-bin.");
        return dir!.FullName;
    }
}

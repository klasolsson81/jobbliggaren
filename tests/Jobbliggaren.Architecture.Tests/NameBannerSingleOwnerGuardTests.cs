using System.IO;
using System.Text.RegularExpressions;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #898 — the CV-title BANNER question has exactly one owner, enforced rather than agreed.
///
/// <para><b>The defect this closes.</b> The predicate had two spellings: the preamble residue asked
/// <c>NormalizeHeading(TrimGlue(line))</c> and the segmenter asked <c>NormalizeHeading(line)</c>,
/// with no glue trim. On <c>"- Curriculum Vitae"</c> — an ASCII hyphen is exactly what a PDF
/// extractor emits for a sidebar bullet — one side called it a banner and the other called it
/// content, so the document title landed in the field labelled <i>namn</i>. A rule with two
/// normalisers is two rules.</para>
///
/// <para><b>Why a source scan and not a convention.</b> Collapsing both call sites onto
/// <c>CvParsingLexiconData.IsNameBanner</c> fixed today's disagreement, but <c>NameBanners</c> stays
/// a readable member of the record, so nothing stops a future call site from writing
/// <c>lexicon.NameBanners.Contains(NormalizeHeading(line))</c> and re-opening the exact fork — and
/// it would look correct in review. This test makes the single ownership a build-time property: the
/// SET may only be interrogated inside the one predicate that owns the question, plus the loader
/// that builds it. (House source-scan idiom, mirroring
/// <see cref="AccountHardDeleteCascadeFitnessTests"/> and <c>EncryptedFieldProjectionGuardTests</c>;
/// the repo's written doctrine prefers a focused source scan over brittle IL inspection.)</para>
///
/// <para>Scoped to the banner question deliberately. It is NOT a general rule about lexicon members:
/// heading detection reads its own maps with its own normaliser, and giving THAT the glue trim would
/// change how every CV is segmented.</para>
/// </summary>
public partial class NameBannerSingleOwnerGuardTests
{
    private const string LexiconRelativePath =
        "src/Jobbliggaren.Infrastructure/Resumes/Parsing/CvParsingLexicon.cs";

    /// <summary>The one file allowed to interrogate the set: it holds both the predicate that owns
    /// the question and the loader that stores the set through the same normaliser.</summary>
    private static readonly string[] AllowedFiles = [LexiconRelativePath];

    [Fact]
    public void NameBanners_IsOnlyInterrogatedByItsOwnPredicate()
    {
        var repoRoot = FindRepoRoot();
        var srcRoot = Path.Combine(repoRoot, "src");

        Directory.Exists(srcRoot).ShouldBeTrue(
            $"källträdet saknas ({srcRoot}) — scanningen skulle passera utan att kunna falla.");

        var offenders = new List<string>();
        var scannedFiles = 0;
        var sawTheOwner = false;

        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
            {
                continue;
            }

            scannedFiles++;

            var relative = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
            var text = File.ReadAllText(file);

            // Reading the member is fine (the record exposes it); INTERROGATING it — asking whether a
            // line is in the set — is the question, and the question has an owner.
            if (!NameBannersQuery().IsMatch(text))
                continue;

            if (AllowedFiles.Contains(relative, StringComparer.Ordinal))
            {
                sawTheOwner = true;
                continue;
            }

            offenders.Add(relative);
        }

        // Vacuity guards: without them a renamed member, a moved file or a broken scan would make the
        // assertion below pass while guarding nothing.
        scannedFiles.ShouldBeGreaterThan(100,
            "källscanningen hittade nästan inga filer — den vaktar då ingenting.");
        sawTheOwner.ShouldBeTrue(
            $"hittade ingen NameBanners-fråga i {LexiconRelativePath} — antingen har ägaren flyttat " +
            "(uppdatera AllowedFiles) eller så matchar inte scan-mönstret längre, och testet skulle " +
            "då passera oavsett hur många andra call sites som ställer frågan.");

        offenders.ShouldBeEmpty(
            "Dessa filer frågar NameBanners direkt i stället för att gå via " +
            $"CvParsingLexiconData.IsNameBanner: {string.Join(", ", offenders)}. Bannerfrågan bär sin " +
            "normalisering (glue-trim + NormalizeHeading) INUTI predikatet — ett call site som " +
            "normaliserar själv återskapar #898:s tvånormaliserare-defekt, och det ser rätt ut i " +
            "granskning.");
    }

    [GeneratedRegex(@"NameBanners\s*\.\s*(Contains|Any|Where|FirstOrDefault|Count)\s*\(",
        RegexOptions.CultureInvariant)]
    private static partial Regex NameBannersQuery();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;

        dir.ShouldNotBeNull(
            "kunde inte hitta repo-roten (CLAUDE.md) uppåt från test-bin — " +
            "arch-testet behöver källträdet för källtext-scan");
        return dir!.FullName;
    }
}

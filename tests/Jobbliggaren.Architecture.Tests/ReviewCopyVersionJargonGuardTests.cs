using System.IO;
using System.Text.RegularExpressions;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// CLAUDE.md §10 / AGENTS.md §5 (CV engines, "not assessed v1" is a status, never copy) — no engine
/// version token in rendered CV-review copy. Until #2083 three NotAssessed reasons told the job-seeker
/// that something "bedöms ej v1": developer shorthand for "this version of the engine", rendered
/// verbatim under a criterion. The rubric side of the same rule is pinned on the asset
/// (RubricPassCriterionAndNotAssessedKindTests and RubricProviderTests); this guard covers the C#
/// side, the string literals under <see cref="EmDashInReviewCopyGuardTests.ScannedCopyDirectories"/>.
///
/// <para>Same lexer as the em-dash guard (<see cref="ReviewCopyScan"/>): comments and XML-doc are
/// exempt, since they legitimately say "rubric v1.2 DATA". A version token is <c>v</c> + digits as a
/// word of its own, so an asset resource name (<c>verb-mapping.v1.json</c>) and an identifier
/// (<c>NotAssessedV1</c>) are not one.</para>
/// </summary>
public class ReviewCopyVersionJargonGuardTests
{
    private static readonly Regex VersionToken = new(
        @"(?<![\p{L}\p{N}_.\-/])v\d+(?![\p{L}\p{N}_])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static IReadOnlyList<(int Line, string Text)> Scan(string source) =>
        ReviewCopyScan.StringLiteralsMatching(source, VersionToken);

    [Fact]
    public void Rendered_review_copy_string_literals_name_no_engine_version()
    {
        var offending = new List<string>();

        foreach (var relativeDir in EmDashInReviewCopyGuardTests.ScannedCopyDirectories)
        {
            var absoluteDir = EmDashInReviewCopyGuardTests.SourceAbsolutePath(relativeDir);
            foreach (var file in Directory.EnumerateFiles(absoluteDir, "*.cs", SearchOption.AllDirectories))
            {
                foreach (var (line, text) in Scan(File.ReadAllText(file)))
                {
                    offending.Add($"{Path.GetFileName(file)}:{line}: {text.Trim()}");
                }
            }
        }

        offending.ShouldBeEmpty(
            "Följande rad(er) har en versionsbeteckning (t.ex. \"ej v1\") i en STRÄNG-literal i " +
            "CV-gransknings-/CV-förbättrings-copyn. Den renderas ordagrant för användaren och är " +
            "utvecklarjargong (CLAUDE.md §10). Skriv vad som saknas i civil svenska, eller \"i den här " +
            "versionen\" när det är en mätning som inte finns ännu. Kommentarer/XML-doc är undantagna. " +
            "Träffar:\n" + string.Join("\n", offending));
    }

    // --- self-proving positives: the three reasons #2083 removed, verbatim from their call sites ---

    [Theory]
    [InlineData("                \"A4\", category, \"Perioderna kunde inte tolkas till datum. Tidsluckor bedöms ej v1.\");")]
    [InlineData("                \"B6\", category, \"Perioderna kunde inte tolkas. Datumformat-konsekvens bedöms ej v1.\");")]
    [InlineData("                \"B7\", category, \"Perioderna kunde inte tolkas. Kronologi bedöms ej v1.\");")]
    public void Scan_flags_the_removed_bedoms_ej_v1_reasons(string source)
    {
        Scan(source).ShouldHaveSingleItem("den borttagna \"bedöms ej v1\"-copyn ska flaggas.");
    }

    [Fact]
    public void Scan_flags_a_version_token_in_an_interpolated_string_literal()
    {
        const string source = "var reason = $\"{id}: ej bedömt v1\";";
        Scan(source).ShouldNotBeEmpty("en versionsbeteckning i en interpolerad sträng-literal ska flaggas.");
    }

    [Fact]
    public void Scan_flags_a_version_token_in_a_verbatim_string_literal()
    {
        const string source = "var reason = @\"Kräver POS/NER bortom V1.\";";
        Scan(source).ShouldNotBeEmpty("en versionsbeteckning i en verbatim sträng-literal ska flaggas.");
    }

    [Fact]
    public void Scan_flags_a_dotted_rubric_version_in_a_string_literal()
    {
        const string source = "var note = \"Enligt rubric v2.3 räknas detta.\";";
        Scan(source).ShouldNotBeEmpty("\"v2.3\" är också en versionsbeteckning.");
    }

    // --- self-proving negatives: comments, resource names, identifiers and civic copy pass ---

    [Fact]
    public void Scan_ignores_a_version_token_in_a_comment()
    {
        const string source =
            "// The ratio is rubric v1.2 DATA (thresholds.failRatio).\n" +
            "/// <summary>out of scope for v1</summary>\n" +
            "var note = \"ren copy\"; // bedöms ej v1";
        Scan(source).ShouldBeEmpty("kommentarer och XML-doc är undantagna.");
    }

    [Fact]
    public void Scan_ignores_an_asset_resource_name_and_an_identifier()
    {
        const string source =
            "const string Name = \"Jobbliggaren.Infrastructure.KnowledgeBank.verb-mapping.v1.json\";\n" +
            "var token = \"NotAssessedV1\";";
        Scan(source).ShouldBeEmpty("ett resursnamn eller en identifierare är ingen versionsbeteckning i copy.");
    }

    [Fact]
    public void Scan_ignores_the_civic_sentence_that_replaced_the_jargon()
    {
        const string source =
            "var reason = \"Det här bedöms inte i den här versionen av granskningen.\";\n" +
            "public const string UnparsedPeriodsReason = \"Perioderna kunde inte tolkas till datum.\";";
        Scan(source).ShouldBeEmpty("civil svenska utan versionsbeteckning ska passera.");
    }
}

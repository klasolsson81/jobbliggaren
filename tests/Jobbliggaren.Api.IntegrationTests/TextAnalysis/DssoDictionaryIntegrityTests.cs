using System.Security.Cryptography;
using System.Text;
using Shouldly;
using WeCantSpell.Hunspell;

namespace Jobbliggaren.Api.IntegrationTests.TextAnalysis;

// Fas 4 STEG 2 (F4-2) — DSSO sv_SE Content-fil-integritet (CTO-dom fråga 4 +
// architect §7.1). The LGPL-3.0 DSSO dictionary MUST ship as an UNMODIFIED,
// separate <Content> data file next to AppContext.BaseDirectory (copyleft
// separation, BUILD §3.1). A missing/feldeployd file otherwise fails LATE in
// prod on the first spell-check — this test moves the discovery to CI/boot.
//
// RED until sv_SE.dic / sv_SE.aff land as <Content CopyToOutputDirectory>.
//
// The SHA-256 pins enforce the "unmodified" LGPL constraint going forward:
// CC fills the placeholder constants when the files land. Until then the pin
// asserts are expected to fail (placeholder ≠ real hash) — that is intended
// RED, and the regression guard is active the moment CC fills them.
//
// Naming: Method_Scenario_Expected.
public class DssoDictionaryIntegrityTests
{
    private const string DicFileName = "sv_SE.dic";
    private const string AffFileName = "sv_SE.aff";

    // PLACEHOLDER — CC fills these with the real SHA-256 (lowercase hex) of the
    // shipped DSSO files when they land. Pinning the hash makes any later
    // modification to the LGPL data file a hard test failure (unmodified
    // constraint). Format: 64 lowercase hex chars.
    private const string ExpectedDicSha256 =
        "540ff8d0d9b05aae66dd919625d3a0bd25026d1e157027e6602e5dd11a07b645";
    private const string ExpectedAffSha256 =
        "ff8ddb979daa9f128e7489ece5edd666ada7af787933221543b7cecb2b3ed478";

    // The DSSO Content files preserve their TextAnalysis subfolder in output
    // (<Content Include="TextAnalysis\sv_SE.dic" CopyToOutputDirectory>), matching
    // HunspellSpellChecker.DictionaryPath / .AffixPath.
    private static string DicPath =>
        Path.Combine(AppContext.BaseDirectory, "TextAnalysis", DicFileName);
    private static string AffPath =>
        Path.Combine(AppContext.BaseDirectory, "TextAnalysis", AffFileName);

    // ===============================================================
    // Existence + non-empty (path built from AppContext.BaseDirectory,
    // never Directory.GetCurrentDirectory() — Api/Worker cwd may differ)
    // ===============================================================

    [Fact]
    public void DicFile_ExistsNextToBaseDirectory_AndIsNonEmpty()
    {
        File.Exists(DicPath).ShouldBeTrue(
            $"DSSO-ordlistan saknas på {DicPath} — den måste kopieras till output " +
            "som <Content> (LGPL-separation, BUILD §3.1).");
        new FileInfo(DicPath).Length.ShouldBeGreaterThan(0,
            "sv_SE.dic får inte vara tom.");
    }

    [Fact]
    public void AffFile_ExistsNextToBaseDirectory_AndIsNonEmpty()
    {
        File.Exists(AffPath).ShouldBeTrue(
            $"DSSO-affixfilen saknas på {AffPath} — den måste kopieras till output " +
            "som <Content> (LGPL-separation, BUILD §3.1).");
        new FileInfo(AffPath).Length.ShouldBeGreaterThan(0,
            "sv_SE.aff får inte vara tom.");
    }

    // ===============================================================
    // UTF-8 decode — åäö survive (encoding guard)
    // ===============================================================

    [Fact]
    public void DicFile_DecodesAsValidUtf8_AndPreservesAao()
    {
        var bytes = File.ReadAllBytes(DicPath);

        // Strict UTF-8 (throwOnInvalidBytes) — a Latin-1/CP1252 dictionary would
        // throw here, catching an encoding regression at the source.
        var strictUtf8 = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        var text = Should.NotThrow(() => strictUtf8.GetString(bytes));

        text.ShouldNotContain('�'); // replacement char = decode failure
        text.ShouldContain('å');
        text.ShouldContain('ä');
        text.ShouldContain('ö');
    }

    // ===============================================================
    // WordList loads from the two files without an encoding-failure warning
    // ===============================================================

    [Fact]
    public void WordList_LoadsFromStreams_WithoutEncodingFailureWarnings()
    {
        using var dicStream = File.OpenRead(DicPath);
        using var affStream = File.OpenRead(AffPath);

        // Same path the impl uses: CreateFromStreams(dic, aff).
        var wordList = Should.NotThrow(() =>
            WordList.CreateFromStreams(dicStream, affStream));

        wordList.ShouldNotBeNull();
        // A successful load of a non-trivial Swedish dictionary must contain
        // known correct words — proves the affix rules parsed, not just opened.
        wordList.Check("arbete", TestContext.Current.CancellationToken).ShouldBeTrue(
            "WordList laddades men kände inte igen 'arbete' — affix/encoding-fel.");
    }

    // ===============================================================
    // SHA-256 pins — "unmodified LGPL data file" regression guard
    // ===============================================================

    [Fact]
    public void DicFile_Sha256_MatchesPinnedHash()
    {
        ExpectedDicSha256.ShouldNotBe("<filled when files land>",
            "CC måste fylla i ExpectedDicSha256 när DSSO-filen landar (LGPL " +
            "unmodified-pin). Tills dess är detta avsiktligt RÖTT.");

        var actual = ComputeSha256(DicPath);
        actual.ShouldBe(ExpectedDicSha256,
            "sv_SE.dic SHA-256 avviker från pinnen — filen har modifierats " +
            "(LGPL-villkor: oförändrad datafil).");
    }

    [Fact]
    public void AffFile_Sha256_MatchesPinnedHash()
    {
        ExpectedAffSha256.ShouldNotBe("<filled when files land>",
            "CC måste fylla i ExpectedAffSha256 när DSSO-filen landar (LGPL " +
            "unmodified-pin). Tills dess är detta avsiktligt RÖTT.");

        var actual = ComputeSha256(AffPath);
        actual.ShouldBe(ExpectedAffSha256,
            "sv_SE.aff SHA-256 avviker från pinnen — filen har modifierats " +
            "(LGPL-villkor: oförändrad datafil).");
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexStringLower(hash);
    }
}

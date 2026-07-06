using System.Text;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Infrastructure.KnowledgeBank;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.KnowledgeBank;

/// <summary>
/// Fas 4b PR-6a (#655, ADR 0093 §D4) — the versioned spelling allowlist the C7 criterion reads.
/// Two surfaces are pinned:
/// <list type="number">
/// <item><b>The real committed asset</b> (<c>spelling-allowlist.v1.json</c>) loads through the
/// real <see cref="SpellingAllowlistProvider"/> (golden source, parity <c>RubricProviderTests</c>)
/// — a data drift or a missing embedded resource fails CI, never surfaces mid-review.</item>
/// <item><b>The <see cref="SpellingAllowlist"/> membership contract</b>: NFC-folded,
/// case-insensitive, whole-token (its OWN SoC, CTO-bind PR-6 D-D) — proven directly on
/// constructed instances so the å/ä/ö-combining-diacritic + substring edges are covered.</item>
/// <item><b>The <c>LoadFrom(Stream)</c> fail-loud seam</b> (parity <c>RubricLoader.LoadFrom</c>):
/// synthetic MemoryStream fixtures drive the REAL deserialise + validate path — a corrupt asset
/// (empty terms / blank version / a null document) must fail LOUD at load, never spell-check with
/// zero proper-noun tolerance.</item>
/// </list>
/// </summary>
public class SpellingAllowlistProviderTests
{
    private static MemoryStream Json(string json) => new(Encoding.UTF8.GetBytes(json));

    // ── The real embedded asset via the real provider (golden source) ─────

    [Fact]
    public void GetAllowlist_ShouldLoadTheEmbeddedAssetWithVersionOne_WhenCalled()
    {
        var allowlist = new SpellingAllowlistProvider().GetAllowlist();

        allowlist.ShouldNotBeNull();
        allowlist.Version.ShouldBe("1");
        allowlist.Count.ShouldBeGreaterThan(0, "det committade assetet bär tekniska termer.");
    }

    [Fact]
    public void GetAllowlist_ShouldReturnTheSameCachedInstance_OnRepeatedCalls()
    {
        // The provider loads the embedded asset ONCE at construction and serves the cached
        // immutable contract (singleton posture) — the same reference on every call.
        var provider = new SpellingAllowlistProvider();

        provider.GetAllowlist().ShouldBeSameAs(provider.GetAllowlist());
    }

    [Theory]
    [InlineData("backend")]
    [InlineData("Backend")]
    [InlineData("BACKEND")]
    public void GetAllowlist_ShouldMatchAnAllowlistedTermCaseInsensitively_WhenCalled(string token)
    {
        // "backend" is a committed term; membership is case-insensitive (the C7 tokenizer only
        // ever hands a lowercase-initial token, but the allowlist's own contract is casing-blind).
        var allowlist = new SpellingAllowlistProvider().GetAllowlist();

        allowlist.Contains(token).ShouldBeTrue($"'{token}' är en committad tillåten term (case-insensitivt).");
    }

    [Fact]
    public void GetAllowlist_ShouldNotMatchASubstringOfAnAllowlistedTerm_WhenCalled()
    {
        // Whole-token only (SoC): "back" is a substring of "backend" but is NOT itself a term, so it
        // must not be suppressed — otherwise "back" (a genuine typo candidate) would slip through.
        var allowlist = new SpellingAllowlistProvider().GetAllowlist();

        allowlist.Contains("back").ShouldBeFalse("delsträng av en term är inte en medlem (whole-token).");
    }

    [Fact]
    public void GetAllowlist_ShouldNotMatchABlankToken_WhenCalled()
    {
        var allowlist = new SpellingAllowlistProvider().GetAllowlist();

        allowlist.Contains("   ").ShouldBeFalse();
        allowlist.Contains(string.Empty).ShouldBeFalse();
    }

    // ── SpellingAllowlist membership contract (constructed directly) ──────

    [Fact]
    public void Contains_ShouldMatchACombiningDiacriticFormAgainstAPrecomposedTerm_WhenCalled()
    {
        // NFC-fold safety (CTO-bind PR-6 D-D): the term is stored precomposed ("ä" = U+00E4); a
        // query in the DECOMPOSED form ("a" + U+0308 combining diaeresis) must still match, because
        // the allowlist NFC-folds BOTH sides. A combining-diacritic drift between the asset and the
        // CV text can never slip a term past the allowlist.
        var allowlist = new SpellingAllowlist("1", ["räka"]); // precomposed ä

        allowlist.Contains("räka").ShouldBeTrue(
            "den dekomponerade formen (a + U+0308) NFC-viks till 'räka' och matchar det precomponerade.");
    }

    [Fact]
    public void Constructor_ShouldDropBlankTerms_WhenBuildingTheSet()
    {
        // Blank entries are dropped so a stray "" in the asset never becomes a member that a blank
        // token could (incorrectly) match; Count reflects the distinct non-blank terms.
        var allowlist = new SpellingAllowlist("1", ["docker", "   ", "", "kubernetes"]);

        allowlist.Count.ShouldBe(2);
        allowlist.Contains("docker").ShouldBeTrue();
        allowlist.Contains("kubernetes").ShouldBeTrue();
    }

    // ── LoadFrom(Stream) — the fail-loud validate seam ───────────────────

    [Fact]
    public void LoadFrom_ShouldMapAValidDocument_WhenWellFormed()
    {
        using var stream = Json("""{ "allowlistVersion": "7", "terms": ["alpha", "beta"] }""");

        var allowlist = SpellingAllowlistProvider.LoadFrom(stream);

        allowlist.Version.ShouldBe("7");
        allowlist.Count.ShouldBe(2);
        allowlist.Contains("ALPHA").ShouldBeTrue("mappningen behåller termerna (case-insensitivt).");
    }

    [Fact]
    public void LoadFrom_ShouldIgnoreUnknownMembers_LikeTheLeadingComment()
    {
        // The asset carries a leading "_comment"; KnowledgeBankJson.Options ignores unknown members
        // (skip-unknown forward/back-compat), so an extra field never breaks the load.
        using var stream = Json(
            """{ "_comment": "authoring note", "allowlistVersion": "1", "terms": ["docker"] }""");

        var allowlist = SpellingAllowlistProvider.LoadFrom(stream);

        allowlist.Contains("docker").ShouldBeTrue();
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenTermsAreEmpty()
    {
        // A shipped empty allowlist is almost certainly a corrupt/missing asset, not an intentional
        // "suppress nothing" — fail loud rather than spell-check with zero proper-noun tolerance.
        using var stream = Json("""{ "allowlistVersion": "1", "terms": [] }""");

        Should.Throw<InvalidOperationException>(() => SpellingAllowlistProvider.LoadFrom(stream));
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenVersionIsBlank()
    {
        using var stream = Json("""{ "allowlistVersion": "", "terms": ["docker"] }""");

        var ex = Should.Throw<InvalidOperationException>(() => SpellingAllowlistProvider.LoadFrom(stream));
        ex.Message.ShouldContain("allowlistVersion");
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenVersionIsWhitespace()
    {
        using var stream = Json("""{ "allowlistVersion": "   ", "terms": ["docker"] }""");

        Should.Throw<InvalidOperationException>(() => SpellingAllowlistProvider.LoadFrom(stream));
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenVersionIsExplicitNull()
    {
        using var stream = Json("""{ "allowlistVersion": null, "terms": ["docker"] }""");

        Should.Throw<InvalidOperationException>(() => SpellingAllowlistProvider.LoadFrom(stream));
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenTheDocumentDeserialisesToNull()
    {
        // A bare JSON `null` document deserialises the file to null → the fail-loud null-guard fires
        // (parity RubricLoader.LoadFrom), never a NullReferenceException downstream.
        using var stream = Json("null");

        Should.Throw<InvalidOperationException>(() => SpellingAllowlistProvider.LoadFrom(stream));
    }

    [Fact]
    public void LoadFrom_ShouldDefaultVersionToUnknown_WhenTheKeyIsAbsent()
    {
        // DOCUMENTED tolerant behaviour (not a fail-loud case): an ABSENT allowlistVersion key keeps
        // the deserialisation record's default ("unknown") rather than throwing — only an explicitly
        // blank/null version fails loud. The shipped asset always authors the field (pinned above),
        // so this tolerant default is only reachable through a malformed hand-authored document.
        using var stream = Json("""{ "terms": ["docker"] }""");

        var allowlist = SpellingAllowlistProvider.LoadFrom(stream);

        allowlist.Version.ShouldBe("unknown");
        allowlist.Contains("docker").ShouldBeTrue();
    }
}

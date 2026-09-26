using System.Text;
using System.Text.RegularExpressions;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Infrastructure.CompanyRegister.Reference;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.CompanyWatches;

/// <summary>
/// #1115 — the SNI search-alias asset. Two jobs here, and they are different in kind: the FORM
/// checks drive synthetic assets through the real <c>LoadAliasesFrom</c> seam (the
/// <see cref="CriterionReferenceLoaderTests"/> mold), and the REAL-asset pins assert the properties
/// that make this a lookup aid rather than the SNI↔SSYK crosswalk #560 bind 4 forbids.
/// </summary>
public class CriterionReferenceAliasTests
{
    private static MemoryStream Json(string json) => new(Encoding.UTF8.GetBytes(json));

    /// <summary>Injects a defect and PROVES it landed — an unmatched Replace is a silent no-op and
    /// the test would then pass for a reason that has nothing to do with the loader.</summary>
    private static MemoryStream Mutated(string find, string replaceWith)
    {
        MinimalValid.Contains(find, StringComparison.Ordinal).ShouldBeTrue(
            $"test-buggen: mönstret '{find}' finns inte i MinimalValid — mutationen landade aldrig.");
        return Json(MinimalValid.Replace(find, replaceWith, StringComparison.Ordinal));
    }

    private const string MinimalValid = """
        {
          "sniVersion": "test.v1",
          "aliasVersion": "test.alias.v1",
          "demandVersion": "test.demand.v1",
          "aliases": [
            { "code": "62201", "source": "scb", "terms": [ "Agil systemutveckling" ] },
            { "code": "43", "source": "authored", "terms": [ "rörmokare" ] },
            { "code": "J", "source": "authored", "terms": [ "it-bransch" ] }
          ]
        }
        """;

    /// <summary>Collects every property name and string value, skipping the top-level "//"
    /// attribution header — that is documentation ABOUT the data, not data.</summary>
    private static void Walk(System.Text.Json.JsonElement el, List<string> into, bool isRoot = false)
    {
        switch (el.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                foreach (var prop in el.EnumerateObject())
                {
                    if (isRoot && prop.NameEquals("//")) continue;
                    into.Add(prop.Name);
                    Walk(prop.Value, into);
                }
                break;
            case System.Text.Json.JsonValueKind.Array:
                foreach (var item in el.EnumerateArray()) Walk(item, into);
                break;
            case System.Text.Json.JsonValueKind.String:
                into.Add(el.GetString() ?? "");
                break;
        }
    }

    private static string RealAssetText()
    {
        var asm = typeof(CriterionReferenceLoader).Assembly;
        using var stream = asm.GetManifestResourceStream(
            "Jobbliggaren.Infrastructure.CompanyRegister.Reference.sni-aliases-2025.v1.json")!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // ── Form ────────────────────────────────────────────────────────────────

    [Fact]
    public void LoadAliasesFrom_MapsTheContract_WhenTheAssetIsWellFormed()
    {
        var catalog = CriterionReferenceLoader.LoadAliasesFrom(Json(MinimalValid));

        catalog.AliasVersion.ShouldBe("test.alias.v1");
        catalog.SniVersion.ShouldBe("test.v1");
        catalog.DemandVersion.ShouldBe("test.demand.v1");
        catalog.TermsFor("62201").ShouldBe(["Agil systemutveckling"]);
        catalog.TermsFor("43").ShouldBe(["rörmokare"]);
        // The SECTION arm of IsSniCodeShaped. Without a section in this fixture the arm is
        // dead-testable: deleting `SectionPattern().IsMatch(code) ||` left the whole suite green,
        // because "62201" matches the leaf arm, "43" the two-digit arm, and the "W" negative is
        // rejected either way.
        catalog.TermsFor("J").ShouldBe(["it-bransch"]);
        catalog.TermsFor("01110").ShouldBeEmpty();
    }

    [Fact]
    public void LoadAliasesFrom_MergesBothSources_WhenOneCodeCarriesEach()
    {
        var both = MinimalValid.Replace(
            """{ "code": "43", "source": "authored", "terms": [ "rörmokare" ] }""",
            """{ "code": "62201", "source": "authored", "terms": [ "påhittad term" ] }""",
            StringComparison.Ordinal);

        var catalog = CriterionReferenceLoader.LoadAliasesFrom(Json(both));

        // The picker wants one list per code; the rows keep their provenance for review.
        catalog.TermsFor("62201").ShouldBe(["Agil systemutveckling", "påhittad term"]);
        // Three rows in the fixture (the section row is untouched by this replacement), two of
        // which share code 62201 and are what the merge above collapses.
        catalog.Aliases.Count.ShouldBe(3);
        catalog.Aliases.Where(static a => a.Code == "62201").Select(static a => a.Source)
            .ShouldBe([SniAliasSource.Scb, SniAliasSource.Authored]);
    }

    [Theory]
    [InlineData("\"source\": \"scb\"", "\"source\": \"jobtech\"")]
    [InlineData("\"source\": \"scb\"", "\"source\": \"\"")]
    // NUMERIC strings are what Enum.TryParse would let through, in two different ways: "7" parses
    // to an undefined (SniAliasSource)7, and "1" parses to a DEFINED value (Authored) the asset
    // never named. Both existing rows are non-numeric and stay green either way, which is why these
    // two have to exist — the second one is the case Enum.IsDefined alone would still admit.
    [InlineData("\"source\": \"scb\"", "\"source\": \"7\"")]
    [InlineData("\"source\": \"scb\"", "\"source\": \"1\"")]
    public void LoadAliasesFrom_Throws_WhenTheSourceIsNotOneThisRepoRecognises(string find, string replaceWith)
    {
        // A third provenance is a decision about where alias vocabulary may come from, not a typo:
        // it must fail here rather than ship an unreviewed source silently.
        Should.Throw<InvalidOperationException>(
            () => CriterionReferenceLoader.LoadAliasesFrom(Mutated(find, replaceWith)));
    }

    [Theory]
    [InlineData("\"code\": \"62201\"", "\"code\": \"622011\"")]
    [InlineData("\"code\": \"62201\"", "\"code\": \"6\"")]
    [InlineData("\"code\": \"62201\"", "\"code\": \"W\"")]
    [InlineData("\"code\": \"62201\"", "\"code\": \"６２２０１\"")]
    public void LoadAliasesFrom_Throws_WhenACodeIsNotSniShaped(string find, string replaceWith)
    {
        Should.Throw<InvalidOperationException>(
            () => CriterionReferenceLoader.LoadAliasesFrom(Mutated(find, replaceWith)));
    }

    [Fact]
    public void LoadAliasesFrom_Throws_WhenARowCarriesNoTerms()
    {
        Should.Throw<InvalidOperationException>(() => CriterionReferenceLoader.LoadAliasesFrom(
            Mutated("[ \"Agil systemutveckling\" ]", "[ ]")));
    }

    [Fact]
    public void LoadAliasesFrom_Throws_WhenATermIsBlank()
    {
        Should.Throw<InvalidOperationException>(() => CriterionReferenceLoader.LoadAliasesFrom(
            Mutated("\"Agil systemutveckling\"", "\"   \"")));
    }

    [Fact]
    public void LoadAliasesFrom_Throws_WhenTheSameCodeAndSourceIsDeclaredTwice()
    {
        Should.Throw<InvalidOperationException>(() => CriterionReferenceLoader.LoadAliasesFrom(
            Mutated(
                """{ "code": "43", "source": "authored", "terms": [ "rörmokare" ] }""",
                """{ "code": "62201", "source": "scb", "terms": [ "annan term" ] }""")));
    }

    [Theory]
    [InlineData("\"aliasVersion\": \"test.alias.v1\",", "")]
    [InlineData("\"sniVersion\": \"test.v1\",", "")]
    [InlineData("\"demandVersion\": \"test.demand.v1\",", "")]
    public void LoadAliasesFrom_Throws_WhenAVersionStampIsMissing(string find, string replaceWith)
    {
        Should.Throw<InvalidOperationException>(
            () => CriterionReferenceLoader.LoadAliasesFrom(Mutated(find, replaceWith)));
    }

    // ── The cross-dataset checks, which live in the provider ────────────────

    [Fact]
    public void Provider_Throws_WhenAnAliasPointsAtACodeSniDoesNotHave()
    {
        var sni = CriterionReferenceLoader.LoadSni();
        var kommuner = CriterionReferenceLoader.LoadKommuner();
        // The version stamp is corrected to the real one FIRST, or the version guard fires and this
        // test passes on the wrong throw — which is exactly what it did before this line existed.
        var aliases = CriterionReferenceLoader.LoadAliasesFrom(Json(MinimalValid
            .Replace("\"sniVersion\": \"test.v1\"", $"\"sniVersion\": \"{sni.Version}\"", StringComparison.Ordinal)
            .Replace("\"code\": \"62201\"", "\"code\": \"99999\"", StringComparison.Ordinal)));

        // 99999 is well-formed and absent from SNI 2025 — exactly the vacuous-filter shape: a picker
        // row for a concept that cannot be selected. It must never reach a running host.
        var ex = Should.Throw<InvalidOperationException>(
            () => new CriterionReferenceProvider(sni, kommuner, aliases));
        ex.Message.ShouldContain("99999");
    }

    [Fact]
    public void Provider_Throws_WhenTheAliasAssetWasBuiltAgainstAnotherSniVersion()
    {
        var sni = CriterionReferenceLoader.LoadSni();
        var kommuner = CriterionReferenceLoader.LoadKommuner();
        var aliases = CriterionReferenceLoader.LoadAliasesFrom(Json(MinimalValid));

        // MinimalValid says "test.v1"; the real catalogue says "2025.v1". Re-versioning SNI without
        // regenerating the aliases must fail the host, not silently keep a stale vocabulary.
        var ex = Should.Throw<InvalidOperationException>(
            () => new CriterionReferenceProvider(sni, kommuner, aliases));
        ex.Message.ShouldContain("generate.mjs");
    }

    [Fact]
    public void Provider_Accepts_TheRealAssetPair()
    {
        var provider = new CriterionReferenceProvider(
            CriterionReferenceLoader.LoadSni(),
            CriterionReferenceLoader.LoadKommuner(),
            CriterionReferenceLoader.LoadAliases());

        // NOT `SniVersion == Sni.Version` — the constructor throws when they differ, so reaching
        // that assertion would already imply it and it could never fail. These can: a gutted or
        // demand-filtered-to-nothing asset builds a provider just fine.
        provider.Aliases.Aliases.ShouldNotBeEmpty();
        provider.Aliases.TermsFor("62201").ShouldNotBeEmpty();
    }

    // ── Real-asset pins ─────────────────────────────────────────────────────

    [Fact]
    public void LoadAliases_RealAsset_ResolvesTheWordThatOpenedTheIssue()
    {
        var aliases = CriterionReferenceLoader.LoadAliases();

        // #1115's headline measurement: "systemutveckl" matched 0 of 944 SNI names, because SNI
        // classifies activities and has no such word. SCB's own example register does — under 62201
        // Datakonsultverksamhet, NOT 62100 Dataprogrammering as the issue body assumed.
        var hits = aliases.Aliases
            .Where(static a => a.Terms.Any(static t =>
                t.Contains("systemutveckl", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        hits.ShouldNotBeEmpty();
        hits.Select(static a => a.Code).ShouldContain("62201");
        aliases.TermsFor("62201").ShouldContain(
            static t => t.Contains("systemutveckling", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LoadAliases_RealAsset_CarriesOnlyCodesThatExistInSni2025()
    {
        var sni = CriterionReferenceLoader.LoadSni();
        var aliases = CriterionReferenceLoader.LoadAliases();
        var known = sni.Sections.Select(static s => s.Code)
            .Concat(sni.Divisions.Select(static d => d.Code))
            .Concat(sni.Leaves.Select(static l => l.Code))
            .ToHashSet(StringComparer.Ordinal);

        aliases.Aliases.ShouldAllBe(a => known.Contains(a.Code));

        // The pin that would have caught the issue body's own error: 62010 is an SNI 2007 code, so
        // an alias file written from #1115's prose would have shipped a dead row.
        aliases.TermsFor("62010").ShouldBeEmpty();
    }

    [Fact]
    public void LoadAliases_RealAsset_DeclaresOnlyTheTwoReviewedSources()
    {
        var aliases = CriterionReferenceLoader.LoadAliases();

        aliases.Aliases.Select(static a => a.Source).Distinct()
            .ShouldBe([SniAliasSource.Scb, SniAliasSource.Authored], ignoreOrder: true);
    }

    [Fact]
    public void RealAsset_CarriesNoOccupationIdentifier_SoItCannotBeACrosswalk()
    {
        // #560 bind 4: company branches (SNI) and job-ad occupation groups (SSYK/JobTech) are
        // different taxonomies and mapping them is a claim the data does not support. What makes
        // this asset a lookup aid rather than that mapping is structural — it carries SNI codes and
        // free text, and nothing that identifies an occupation.
        //
        // Scanned over the DATA, not the file text: the top-level "//" header states in prose that
        // no SSYK id appears here, so a raw-text grep matches the documentation and fails on its own
        // description. Every property name and string value outside that header is walked instead,
        // so a field added outside the mapped contract is still caught.
        using var doc = System.Text.Json.JsonDocument.Parse(RealAssetText());
        var scanned = new List<string>();
        Walk(doc.RootElement, scanned, isRoot: true);

        foreach (var forbidden in new[] { "ssyk", "conceptId", "concept_id", "occupationGroup" })
        {
            scanned.ShouldNotContain(
                s => s.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"alias-assetet bär '{forbidden}' i datat — det vore en SNI↔SSYK-crosswalk, som #560 bind 4 förbjuder.");
        }

        // JobTech concept ids are three underscore-joined base64-ish groups ("fg7B_yov_smw").
        scanned.ShouldNotContain(
            s => Regex.IsMatch(s, @"[A-Za-z0-9]{3,4}_[A-Za-z0-9]{3,4}_[A-Za-z0-9]{3,4}"),
            "alias-assetet bär något som har formen av ett JobTech concept-id.");
    }

    [Fact]
    public void RealAsset_AuthoredRowsPointAtALeafOrADivision_NeverASection()
    {
        // Entry condition 2 (graded): an authored term names the leaf only where exactly one leaf is
        // unambiguous, otherwise the division. A SECTION is too coarse to be a useful answer — it is
        // a fifth of the economy — so an authored row landing there is a grading mistake.
        var authored = CriterionReferenceLoader.LoadAliases().Aliases
            .Where(static a => a.Source == SniAliasSource.Authored)
            .ToList();

        authored.ShouldNotBeEmpty();
        authored.ShouldAllBe(a => a.Code.Length == 2 || a.Code.Length == 5);
    }

    [Fact]
    public void RealAsset_CarriesTheAuthoredCodesTheResidueWasWrittenFor()
    {
        // The authored half comes from a hand-written in-repo file, so this pin moves only when
        // someone edits authored-terms.json in the same commit — the SNI count-pin discipline.
        var authored = CriterionReferenceLoader.LoadAliases().Aliases
            .Where(static a => a.Source == SniAliasSource.Authored)
            .Select(static a => a.Code)
            .Order(StringComparer.Ordinal);

        authored.ShouldBe(["16", "41", "43", "43210", "62"]);
    }

    [Fact]
    public void RealAsset_TermsAreStoredVerbatim_NotLowercasedOrTruncated()
    {
        var aliases = CriterionReferenceLoader.LoadAliases();

        // Normalisation is a matching concern. If the stored strings were folded or clipped, the row
        // could not show the user the term SCB actually published.
        //
        // Asserted as PROPERTIES, not as one exact sentence: SCB may reword any single entry, and a
        // hardcoded phrase would then fail for a reason that has nothing to do with verbatim storage.
        //
        var terms = aliases.Aliases.SelectMany(static a => a.Terms).ToList();
        terms.ShouldContain(static t => t.Any(char.IsUpper), "termerna är gemenfällda");
        terms.ShouldContain(static t => t.Length > 60, "termerna är avkortade");
    }
}

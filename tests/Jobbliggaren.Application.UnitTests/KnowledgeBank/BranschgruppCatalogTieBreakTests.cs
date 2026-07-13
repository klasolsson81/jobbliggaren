using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.KnowledgeBank;

/// <summary>
/// Fas 4b 8b.4a — the tie-break (senior-cto-advisor bind, 2026-07-13). A pure function over the
/// catalog: no DB, no handler, no fixtures. The rule is deliberately conservative — the confirmed
/// occupation-fields resolve to exactly ONE non-Övriga branschgrupp → that one; anything else →
/// Övriga.
/// <para>
/// The refusal case is the point. A user who states both an IT and a vård occupation gets the
/// generic row, never a coin flip: the two rule-tables genuinely disagree (IT makes Projekt its
/// standard section; vård deliberately does not offer it at all), and guessing which one she
/// "really" meant is exactly the mis-suggestion the Övriga row exists to avoid. Coverage bought by
/// mis-suggesting is negative value.
/// </para>
/// </summary>
public class BranschgruppCatalogTieBreakTests
{
    private const string It = "apaJ_2ja_LuF";           // Data/IT
    private const string Vard = "NYW6_mP6_vwf";         // Hälso- och sjukvård
    private const string Social = "GazW_2TU_kJw";       // Yrken med social inriktning → vard
    private const string Bygg = "j7Cq_ZJe_GkT";         // Bygg och anläggning → ovriga
    private const string Teknisk = "6Hq3_tKo_V57";      // Yrken med teknisk inriktning → ovriga

    private static BranschgruppCatalog Catalog() => new(
        "1.0",
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [It] = "it",
            [Vard] = "vard",
            [Social] = "vard",
            [Bygg] = "ovriga",
            [Teknisk] = "ovriga",
        },
        new Dictionary<string, BranschgruppRules>(StringComparer.Ordinal)
        {
            ["it"] = new("it", "Vanligt inom data och IT", [], []),
            ["vard"] = new("vard", "Vanligt inom vård och omsorg", [], []),
            ["ovriga"] = new("ovriga", "Vanliga sektioner i svenska CV", [], []),
        });

    [Fact]
    public void ResolveBranschgrupp_ShouldReturnIt_WhenTheOnlyFieldIsDataIt()
    {
        Catalog().ResolveBranschgrupp([It]).ShouldBe("it");
    }

    [Fact]
    public void ResolveBranschgrupp_ShouldReturnVard_WhenTwoFieldsBothMapToVard()
    {
        // Klas' C3(i): Hälso- och sjukvård AND Yrken med social inriktning both mean vård. Two
        // fields, ONE branschgrupp → no ambiguity, no fallback.
        Catalog().ResolveBranschgrupp([Vard, Social]).ShouldBe("vard");
    }

    [Fact]
    public void ResolveBranschgrupp_ShouldReturnOvriga_WhenFieldsSpanTwoDifferentBranschgrupper()
    {
        // The refusal. NOT "pick the first", NOT "pick the most common" — refuse.
        Catalog().ResolveBranschgrupp([It, Vard]).ShouldBe(BranschgruppCatalog.Fallback);
    }

    [Fact]
    public void ResolveBranschgrupp_ShouldReturnIt_WhenANamedFieldIsMixedWithAnOvrigaField()
    {
        // An Övriga field is NOT a competing opinion — it is the absence of one. A developer who
        // stated "Data/IT" and "Bygg och anläggning" still gets the IT rules: only NAMED
        // branschgrupper compete in the tie-break, and exactly one is named here.
        Catalog().ResolveBranschgrupp([It, Bygg]).ShouldBe("it");
    }

    [Fact]
    public void ResolveBranschgrupp_ShouldReturnOvriga_WhenEveryFieldMapsToOvriga()
    {
        // The 62.1 % majority path, reached with a STATED occupation. The DTO's
        // HasOccupationPreference flag is what keeps this apart from "no occupation stated" —
        // this method cannot and must not distinguish them.
        Catalog().ResolveBranschgrupp([Bygg, Teknisk]).ShouldBe(BranschgruppCatalog.Fallback);
    }

    [Fact]
    public void ResolveBranschgrupp_ShouldReturnOvriga_WhenTheFieldIsUnknownToTheAsset()
    {
        // Taxonomy drift: a field id the asset does not map. Graceful — never throw. (The
        // completeness test is what stops this from happening silently in production.)
        Catalog().ResolveBranschgrupp(["ZZZZ_unknown_id"]).ShouldBe(BranschgruppCatalog.Fallback);
    }

    [Fact]
    public void ResolveBranschgrupp_ShouldReturnOvriga_WhenNoFieldsAreGiven()
    {
        Catalog().ResolveBranschgrupp([]).ShouldBe(BranschgruppCatalog.Fallback);
    }

    [Fact]
    public void RulesFor_ShouldThrow_WhenTheBranschgruppIsUnknown()
    {
        // An unknown id is a BUG (a field mapped to a grupp with no rules — which the loader
        // already refuses), not a user state. Degrading to an empty table would hide it.
        Should.Throw<InvalidOperationException>(() => Catalog().RulesFor("skola"));
    }
}

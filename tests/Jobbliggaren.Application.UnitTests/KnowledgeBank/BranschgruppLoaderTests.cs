using System.Text;
using Jobbliggaren.Infrastructure.KnowledgeBank;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.KnowledgeBank;

/// <summary>
/// Fas 4b 8b.4a — <see cref="BranschgruppLoader"/> SHAPE validation, driven through the real
/// <c>LoadFrom(Stream)</c> seam with synthetic assets (parity <c>FramesLoaderTests</c>). Every one
/// of these throws at host build, never mid-request: a broken rules asset must stop the app, not
/// surface as a 500 inside a user's CV import.
/// </summary>
public class BranschgruppLoaderTests
{
    private static MemoryStream Json(string json) => new(Encoding.UTF8.GetBytes(json));

    /// <summary>
    /// Injects a defect into <see cref="MinimalValid"/> and PROVES it landed. A bare
    /// <c>string.Replace</c> whose pattern does not match is a silent no-op — the "defective" asset
    /// is then byte-identical to the valid one, the loader correctly does not throw, and the test
    /// fails for a reason that has nothing to do with the loader. Worse, had the assertion been
    /// inverted it would have passed while checking nothing. Fail here instead, loudly.
    /// </summary>
    private static MemoryStream Mutated(string find, string replaceWith)
    {
        MinimalValid.Contains(find, StringComparison.Ordinal).ShouldBeTrue(
            $"test-buggen: mönstret '{find}' finns inte i MinimalValid — mutationen landade aldrig.");
        return Json(MinimalValid.Replace(find, replaceWith, StringComparison.Ordinal));
    }

    private const string MinimalValid = """
        {
          "branschgruppVersion": "1.0",
          "occupationFields": [
            { "conceptId": "apaJ_2ja_LuF", "branschgrupp": "it" },
            { "conceptId": "X82t_awd_Qyc", "branschgrupp": "ovriga" }
          ],
          "branschgrupper": [
            { "id": "it", "rationale": "Vanligt inom data och IT",
              "standardSections": [ { "sectionId": "projekt", "heading": "Projekt" } ],
              "suggestedSections": [], "suppressedSectionIds": [] },
            { "id": "ovriga", "rationale": "Vanliga sektioner i svenska CV",
              "standardSections": [], "suggestedSections": [], "suppressedSectionIds": [] }
          ]
        }
        """;

    [Fact]
    public void LoadFrom_ShouldMapTheContract_WhenTheAssetIsWellFormed()
    {
        // The positive control. Without it, every negative test below could pass against a loader
        // that simply throws on everything.
        var catalog = BranschgruppLoader.LoadFrom(Json(MinimalValid));

        catalog.Version.ShouldBe("1.0");
        catalog.BranschgruppByOccupationField["apaJ_2ja_LuF"].ShouldBe("it");
        catalog.RulesFor("it").StandardSections.ShouldHaveSingleItem().SectionId.ShouldBe("projekt");
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenVersionIsMissing()
    {
        Should.Throw<InvalidOperationException>(
            () => BranschgruppLoader.LoadFrom(Mutated("\"branschgruppVersion\": \"1.0\",", "")));
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenAnOccupationFieldHasNoConceptId()
    {
        // Fail LOUD, never skip. System.Text.Json silently ignores unknown members, so a typo'd
        // key ("conceptid") deserialises to null — and a silently DROPPED occupation-field is the
        // vacuous-filter bug: that occupation falls to Övriga forever and nothing goes red.
        Should.Throw<InvalidOperationException>(() => BranschgruppLoader.LoadFrom(
            Mutated("\"conceptId\": \"apaJ_2ja_LuF\"", "\"conceptid\": \"apaJ_2ja_LuF\"")));
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenAFieldPointsAtAnUnknownBranschgrupp()
    {
        Should.Throw<InvalidOperationException>(() => BranschgruppLoader.LoadFrom(
            Mutated("\"branschgrupp\": \"it\"", "\"branschgrupp\": \"itt\"")));
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenTheFallbackBranschgruppIsMissing()
    {
        // Övriga is not optional: it is where every unmapped/ambiguous occupation lands (62.1 % of
        // users). Without a rule-table it would resolve to a branschgrupp with no rules and the
        // feature would be dead for the majority while looking alive.
        var noFallback = """
            {
              "branschgruppVersion": "1.0",
              "occupationFields": [ { "conceptId": "apaJ_2ja_LuF", "branschgrupp": "it" } ],
              "branschgrupper": [
                { "id": "it", "rationale": "x", "standardSections": [], "suggestedSections": [], "suppressedSectionIds": [] }
              ]
            }
            """;

        Should.Throw<InvalidOperationException>(() => BranschgruppLoader.LoadFrom(Json(noFallback)));
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenAnOccupationFieldIsDeclaredTwice()
    {
        Should.Throw<InvalidOperationException>(() => BranschgruppLoader.LoadFrom(Mutated(
            "{ \"conceptId\": \"X82t_awd_Qyc\", \"branschgrupp\": \"ovriga\" }",
            "{ \"conceptId\": \"X82t_awd_Qyc\", \"branschgrupp\": \"ovriga\" },\n    " +
            "{ \"conceptId\": \"apaJ_2ja_LuF\", \"branschgrupp\": \"ovriga\" }")));
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenABranschgruppBothSuggestsAndSuppressesTheSameSection()
    {
        // A contradiction the data must not be able to express: the read-slice would silently drop
        // the section, and the asset would READ as if it offered something it never does.
        //
        // Written out in full rather than string-replaced into MinimalValid: the first version of
        // this test used a .Replace() whose pattern never matched, so the "contradiction" was never
        // injected and the assertion could not fail. A negative test that cannot see the thing it
        // negates is worse than no test — it reports safety it never checked.
        var contradiction = """
            {
              "branschgruppVersion": "1.0",
              "occupationFields": [ { "conceptId": "apaJ_2ja_LuF", "branschgrupp": "it" } ],
              "branschgrupper": [
                { "id": "it", "rationale": "Vanligt inom data och IT",
                  "standardSections": [ { "sectionId": "projekt", "heading": "Projekt" } ],
                  "suggestedSections": [], "suppressedSectionIds": ["projekt"] },
                { "id": "ovriga", "rationale": "Vanliga sektioner i svenska CV",
                  "standardSections": [], "suggestedSections": [], "suppressedSectionIds": [] }
              ]
            }
            """;

        var ex = Should.Throw<InvalidOperationException>(
            () => BranschgruppLoader.LoadFrom(Json(contradiction)));

        ex.Message.ShouldContain("projekt");
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenASectionHasAnEmptyHeading()
    {
        // The heading is written into the user's CV — it cannot be blank.
        Should.Throw<InvalidOperationException>(() => BranschgruppLoader.LoadFrom(
            Mutated("\"heading\": \"Projekt\"", "\"heading\": \"\"")));
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenABranschgruppHasNoRationale()
    {
        Should.Throw<InvalidOperationException>(() => BranschgruppLoader.LoadFrom(
            Mutated("\"rationale\": \"Vanligt inom data och IT\",", "")));
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenTheAssetMapsNoOccupationFields()
    {
        var empty = """
            {
              "branschgruppVersion": "1.0",
              "occupationFields": [],
              "branschgrupper": [
                { "id": "ovriga", "rationale": "x", "standardSections": [], "suggestedSections": [], "suppressedSectionIds": [] }
              ]
            }
            """;

        Should.Throw<InvalidOperationException>(() => BranschgruppLoader.LoadFrom(Json(empty)));
    }
}

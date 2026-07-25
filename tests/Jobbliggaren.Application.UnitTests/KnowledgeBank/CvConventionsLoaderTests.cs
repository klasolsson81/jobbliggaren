using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.KnowledgeBank;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.KnowledgeBank;

/// <summary>
/// Fas 4b 8b.4b (ADR 0108) — <see cref="CvConventionsLoader"/>'s fail-loud SHAPE rules, driven
/// through the real <c>LoadFrom(Stream)</c> seam with synthetic assets (parity
/// <c>BranschgruppLoaderTests</c>/<c>FramesLoaderTests</c>). Every rule here exists because its
/// absence would let a malformed asset load CLEAN and then behave wrongly — a section silently
/// dropped from the order, or an order so empty the transform can never fire.
/// <para>
/// The FREE-section half of the identity check is NOT here: it needs the parsing lexicon, so it is
/// the provider's cross-asset pin (<see cref="CvConventionsProviderTests"/>).
/// </para>
/// </summary>
public class CvConventionsLoaderTests
{
    private const string MinimalValid = """
        {
          "conventionsVersion": "1.0.0",
          "sectionOrder": ["contact", "experience", "education"],
          "fontAllowlist": ["Arial", "Calibri"],
          "coreSections": ["contact"]
        }
        """;

    private static MemoryStream Json(string json) =>
        new(System.Text.Encoding.UTF8.GetBytes(json));

    private static MemoryStream Mutated(string find, string replaceWith) =>
        Json(MinimalValid.Replace(find, replaceWith, StringComparison.Ordinal));

    [Fact]
    public void LoadFrom_ShouldMapTheContract_WhenTheAssetIsWellFormed()
    {
        var conventions = CvConventionsLoader.LoadFrom(Json(MinimalValid));

        conventions.Version.ShouldBe("1.0.0");
        conventions.SectionOrder.Select(e => e.SectionId)
            .ShouldBe(["contact", "experience", "education"]);
    }

    [Fact]
    public void LoadFrom_ShouldResolveTypedSectionsToTheirParsedSectionKind_WhenTheyAreTyped()
    {
        var conventions = CvConventionsLoader.LoadFrom(Json(MinimalValid));

        conventions.SectionOrder[0].TypedKind.ShouldBe(ParsedSectionKind.Contact);
        conventions.SectionOrder[1].TypedKind.ShouldBe(ParsedSectionKind.Experience);
        conventions.SectionOrder[2].TypedKind.ShouldBe(ParsedSectionKind.Education);
    }

    [Fact]
    public void LoadFrom_ShouldLeaveFreeSectionsUnresolved_SoTheProviderCanPinThemAgainstTheLexicon()
    {
        // A non-typed id is a FREE-section CANDIDATE. The loader cannot judge it (it has no
        // lexicon), so it must pass it through unresolved rather than guess or drop it — dropping
        // is the vacuous-filter failure mode, and guessing forks the vocabulary.
        var conventions = CvConventionsLoader.LoadFrom(
            Mutated("\"education\"", "\"education\", \"projekt\""));

        var free = conventions.SectionOrder.Single(e => e.SectionId == "projekt");
        free.TypedKind.ShouldBeNull();
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenVersionIsMissing()
    {
        Should.Throw<InvalidOperationException>(
            () => CvConventionsLoader.LoadFrom(Mutated("\"conventionsVersion\": \"1.0.0\",", "")));
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenSectionOrderIsEmpty()
    {
        // An empty order is not "no opinion" — it is a transform that can never fire, dressed as
        // data. The asset's entire purpose is this list.
        // The message is asserted, not just the throw: coreSections carries its own empty-list guard
        // three lines away, and an unasserted Should.Throw cannot tell which one fired. That is how
        // this suite briefly stopped guarding sectionOrder at all — MinimalValid used to spell
        // coreSections with the SAME three ids, so this whole-document Replace mutated both arrays
        // and the coreSections guard threw in sectionOrder's place. All three sectionOrder guards
        // could then be deleted with the suite green (measured, test-writer 2026-07-25).
        var ex = Should.Throw<InvalidOperationException>(
            () => CvConventionsLoader.LoadFrom(
                Mutated("[\"contact\", \"experience\", \"education\"]", "[]")));

        ex.Message.ShouldContain("sectionOrder");
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenASectionIsOrderedTwice()
    {
        // A section named twice has two positions. The sort would silently take one of them, and
        // the other would be a lie the file tells about itself.
        var ex = Should.Throw<InvalidOperationException>(
            () => CvConventionsLoader.LoadFrom(
                Mutated("\"education\"", "\"education\", \"contact\"")));

        ex.Message.ShouldContain("sectionOrder");
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenASectionIdIsBlank()
    {
        var ex = Should.Throw<InvalidOperationException>(
            () => CvConventionsLoader.LoadFrom(Mutated("\"education\"", "\"\"")));

        ex.Message.ShouldContain("sectionOrder");
    }

    [Fact]
    public void LoadFrom_ShouldNotResolveANumericId_BecauseEnumParseWouldAcceptIt()
    {
        // Enum.TryParse("0") succeeds and yields ParsedSectionKind.Contact. The id-space is
        // NAMES, so a numeric typo must fall through to the free-section arm and be rejected by
        // the provider's lexicon pin — never silently resolve to a real section.
        var conventions = CvConventionsLoader.LoadFrom(Mutated("\"education\"", "\"0\""));

        conventions.SectionOrder.Single(e => e.SectionId == "0").TypedKind.ShouldBeNull();
    }

    // ── fontAllowlist (Fas 4b #891, ADR 0108) ────────────────────────────

    [Fact]
    public void LoadFrom_ShouldMapTheFontAllowlist_WhenTheAssetIsWellFormed()
    {
        var conventions = CvConventionsLoader.LoadFrom(Json(MinimalValid));

        conventions.FontAllowlist.ShouldBe(["Arial", "Calibri"]);
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenFontAllowlistIsEmpty()
    {
        // An empty allowlist is not "no opinion" — it is a D3 rule that can never recognise a
        // standard font, so every measured CV would Warn. The font half exists to carry the list.
        Should.Throw<InvalidOperationException>(
            () => CvConventionsLoader.LoadFrom(Mutated("[\"Arial\", \"Calibri\"]", "[]")));
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenAFontNameIsBlank()
    {
        Should.Throw<InvalidOperationException>(
            () => CvConventionsLoader.LoadFrom(Mutated("\"Calibri\"", "\"\"")));
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenAFontIsListedTwice()
    {
        // A duplicate is a data typo, not a second opinion — fail loud like a duplicate sectionId.
        Should.Throw<InvalidOperationException>(
            () => CvConventionsLoader.LoadFrom(Mutated("\"Calibri\"", "\"Calibri\", \"Arial\"")));
    }

    // ── coreSections (#890) — the ids B1's Fail arm measures burial against ──────────────

    [Fact]
    public void LoadFrom_ShouldMapTheCoreSections_WhenTheAssetIsWellFormed()
    {
        var conventions = CvConventionsLoader.LoadFrom(Json(MinimalValid));

        conventions.CoreSections.ShouldBe(["contact"]);
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenACoreSectionIsMissingFromTheOrder()
    {
        // THE INVARIANT THAT MATTERS, and the reason it cannot be a fallback. An observed section is
        // resolved to a core id THROUGH sectionOrder, so a core id the order does not name can never
        // be matched against a real CV: it stays permanently absent from the observed core set, the
        // lead-in measure's validity precondition can never be satisfied, and B1's Fail arm goes
        // silently unreachable. Every CV would look fine on the very dimension the asset was edited
        // to sharpen. A validation that turns a data typo into a permanently green verdict is worse
        // than no validation.
        var ex = Should.Throw<InvalidOperationException>(() => CvConventionsLoader.LoadFrom(
            Mutated(
                "\"coreSections\": [\"contact\"]",
                "\"coreSections\": [\"contact\", \"languages\"]")));

        ex.Message.ShouldContain("languages");
        ex.Message.ShouldContain("sectionOrder");
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenCoreSectionsIsEmpty()
    {
        // An empty list is not "no opinion" — it is a Fail arm that can never fire, dressed as data.
        var ex = Should.Throw<InvalidOperationException>(() => CvConventionsLoader.LoadFrom(
            Mutated("\"coreSections\": [\"contact\"]", "\"coreSections\": []")));

        ex.Message.ShouldContain("coreSections");
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenACoreSectionIsListedTwice()
    {
        Should.Throw<InvalidOperationException>(() => CvConventionsLoader.LoadFrom(
            Mutated(
                "\"coreSections\": [\"contact\"]",
                "\"coreSections\": [\"contact\", \"contact\"]")));
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenACoreSectionIdIsBlank()
    {
        // Reachable only because MinimalValid's coreSections no longer collides with sectionOrder —
        // before that fix the sectionOrder blank-guard always threw first and this arm was dead.
        var ex = Should.Throw<InvalidOperationException>(() => CvConventionsLoader.LoadFrom(
            Mutated("\"coreSections\": [\"contact\"]", "\"coreSections\": [\" \"]")));

        ex.Message.ShouldContain("coreSections");
    }
}

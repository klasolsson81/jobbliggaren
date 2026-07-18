using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.KnowledgeBank;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.KnowledgeBank;

/// <summary>
/// Fas 4b 8b.4b (ADR 0108) — the committed conventions asset (<c>cv-conventions.v1.json</c>) loads
/// through the real <see cref="CvConventionsProvider"/>, which runs the FULL cross-asset pin
/// against the real parsing lexicon at construction. The asset RECOMMENDS; the lexicon RECOGNISES
/// (ADR 0107 §3) — and an order may only name sections that can actually be recognised in a CV.
/// <para>
/// The load-bearing test is <see cref="ValidateAgainstLexicon_ShouldThrow_WhenTheOrderNamesASectionTheLexiconDoesNotOwn"/>:
/// it drives a SYNTHETIC drifted asset through the REAL check. The shipped asset is, by
/// construction, the one case that passes — a test that could only ever see the good asset would
/// prove nothing.
/// </para>
/// </summary>
public class CvConventionsProviderTests
{
    private static CvParsingLexiconProvider RealLexicon() =>
        new(CvParsingLexiconLoader.Load());

    private static CvConventions LoadConventions() =>
        new CvConventionsProvider(RealLexicon()).GetConventions();

    [Fact]
    public void GetConventions_ShouldLoadVersionedEmbeddedResource_WhenCalled()
    {
        var conventions = LoadConventions();

        conventions.ShouldNotBeNull();
        conventions.Version.ShouldBe("1.1.0");
    }

    [Fact]
    public void GetConventions_ShouldCarryTheRubricsB1Chain_AsMachineReadableData()
    {
        // The rubric's B1 atsPassSignal, verbatim: "Kontakt → (Profil) → Arbetslivserfarenhet →
        // Utbildning → Kompetenser → Språk → Övrigt". The trailing "Övrigt" is the sort's
        // stability, not a data entry — the six named sections are the asset.
        LoadConventions().SectionOrder.Select(e => e.TypedKind).ShouldBe(
        [
            ParsedSectionKind.Contact,
            ParsedSectionKind.Profile,
            ParsedSectionKind.Experience,
            ParsedSectionKind.Education,
            ParsedSectionKind.Skills,
            ParsedSectionKind.Languages,
        ]);
    }

    [Fact]
    public void SectionOrder_ShouldFollowTheRubricsOwnB1Chain_SoTheAssetCannotDriftFromItSilently()
    {
        // The asset's whole premise is "the rubric's B1 chain, made machine-readable". Pinning it
        // against a hand-written list would let the rubric change and the asset stay put — silently,
        // with every test green. DERIVE the expectation from the rubric prose instead: each ordered
        // section must appear in B1's atsPassSignal, and in the same relative order.
        var chain = new RubricProvider().GetRubric().Criteria
            .Single(c => c.Id == "B1").AtsPassSignal
            .ShouldNotBeNull("B1 måste bära sin atsPassSignal — det är kedjan assetet kodifierar.");

        // The Swedish terms the chain names, in the asset's own order. (The chain is prose — this
        // maps each sectionId to the word the rubric uses for it; a rename on either side goes red.)
        var termBySectionId = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["contact"] = "Kontakt",
            ["profile"] = "Profil",
            ["experience"] = "Arbetslivserfarenhet",
            ["education"] = "Utbildning",
            ["skills"] = "Kompetenser",
            ["languages"] = "Språk",
        };

        var positions = LoadConventions().SectionOrder
            .Select(e => chain.IndexOf(termBySectionId[e.SectionId], StringComparison.Ordinal))
            .ToList();

        positions.ShouldAllBe(i => i >= 0,
            "Varje ordnad sektion måste förekomma i rubrikens B1-kedja.");
        positions.ShouldBe(positions.Order().ToList(),
            "Assetets ordning MÅSTE vara rubrikens ordning — annars är premissen "
            + "'rubrikens kedja gjord maskinläsbar' inte längre sann.");
    }

    [Fact]
    public void Ctor_ShouldConstruct_WhenTheShippedAssetAgreesWithTheShippedLexicon()
    {
        // The pin, run against the REAL pair — this is the guarantee the host gets at build.
        Should.NotThrow(() => new CvConventionsProvider(RealLexicon()));
    }

    [Fact]
    public void ValidateAgainstLexicon_ShouldThrow_WhenTheOrderNamesASectionTheLexiconDoesNotOwn()
    {
        // THE MUTATION. An id that is neither a typed section nor a lexicon free section can never
        // be resolved from a real CV's headings — so it would sit in the recommended order matching
        // NOTHING, and the transform would sort against a phantom, silently, forever.
        var drifted = new CvConventions("1.0.0",
        [
            new CvSectionOrderEntry("experience", ParsedSectionKind.Experience),
            new CvSectionOrderEntry("hobbyprojekt-i-tradgarden", TypedKind: null),
        ],
        ["Arial"]);

        var ex = Should.Throw<InvalidOperationException>(
            () => CvConventionsProvider.ValidateAgainstLexicon(drifted, RealLexicon()));

        ex.Message.ShouldContain("hobbyprojekt-i-tradgarden");
    }

    [Fact]
    public void ValidateAgainstLexicon_ShouldAcceptAFreeSectionTheLexiconOwns_SoTheOrderCanGrow()
    {
        // The mirror of the mutation above: a free id the lexicon DOES own is legal, so the
        // convention can one day order free sections explicitly. Without this, the guard above
        // would be indistinguishable from "free sections are forbidden".
        var withFreeSection = new CvConventions("1.0.0",
        [
            new CvSectionOrderEntry("experience", ParsedSectionKind.Experience),
            new CvSectionOrderEntry("projekt", TypedKind: null),
        ],
        ["Arial"]);

        Should.NotThrow(
            () => CvConventionsProvider.ValidateAgainstLexicon(withFreeSection, RealLexicon()));
    }

    [Fact]
    public void ValidateAgainstLexicon_ShouldNotRejectTypedSections_EvenThoughTheFreeSectionPortReturnsNullForThem()
    {
        // A trap worth pinning: ICvParsingLexicon.TryResolveFreeSectionId("skills") returns null BY
        // DESIGN (Kompetenser is typed, not absent). A pin that asked the free-section port about a
        // typed id would therefore reject every typed section — i.e. the whole shipped asset.
        RealLexicon().FreeSectionIds.ShouldNotContain("skills");

        Should.NotThrow(() => CvConventionsProvider.ValidateAgainstLexicon(
            new CvConventions("1.0.0", [new CvSectionOrderEntry("skills", ParsedSectionKind.Skills)], ["Arial"]),
            RealLexicon()));
    }
}

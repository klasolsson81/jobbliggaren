using System.Text;
using Jobbliggaren.Infrastructure.CompanyRegister.Reference;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Parsing;

/// <summary>
/// #2343 (senior-cto-advisor D7): the CV import's place vocabulary, SCB's kommuner and its 2025
/// tätorter, and the one place question every contact call site asks.
/// </summary>
public class CvPlaceLexiconTests
{
    private readonly HeadingDrivenResumeSegmenter _sut = CvParsingLexiconFixture.Segmenter();

    [Theory]
    [InlineData("Åkersberga")]
    [InlineData("Bunkeflostrand")]
    [InlineData("Hjärup")]
    [InlineData("Mölnlycke")]
    [InlineData("Öjersjö")]
    [InlineData("Göteborg")]
    [InlineData("åkersberga")]
    [InlineData(" Åkersberga ")]
    public void TheShippedVocabulary_HoldsKommunerAndTätorter_AsWholeFragments(string place) =>
        CvParsingLexiconFixture.Places().Contains(place).ShouldBeTrue();

    [Theory]
    [InlineData("Åkersbergavägen")]
    [InlineData("Mölnlycke och Öjersjö")]
    [InlineData("")]
    public void ASubstringOrAnUnsplitSCBName_IsNotAPlace(string candidate) =>
        CvParsingLexiconFixture.Places().Contains(candidate).ShouldBeFalse();

    [Fact]
    public void TheShippedVocabulary_NamesItsTwoSources()
    {
        var places = CvParsingLexiconFixture.Places();

        places.TatortVersion.ShouldBe("2025.v1");
        places.KommunVersion.ShouldBe(CriterionReferenceLoader.LoadKommuner().Version);
    }

    [Theory]
    [InlineData("  Alby ", new[] { "Alby" })]
    [InlineData("Kungsängen (preliminär beteckning)", new[] { "Kungsängen" })]
    [InlineData("Bredsand (2015, 2020: Sundsvall)", new[] { "Bredsand" })]
    [InlineData("Mölnlycke och Öjersjö", new[] { "Mölnlycke", "Öjersjö" })]
    [InlineData("Ekskogen, Älgeby och Långsjötorp", new[] { "Ekskogen", "Älgeby", "Långsjötorp" })]
    [InlineData("Ed och ed", new[] { "Ed" })]
    public void PlaceNames_NormaliseAnSCBNameIntoItsPlaces(string scbName, string[] expected) =>
        PlaceNames.Keys(scbName).ShouldBe(expected);

    [Theory]
    [InlineData("""{"tatorter":[{"code":"0114TB101","name":"A","kommunCode":"0114"}]}""", "tatortVersion")]
    [InlineData("""{"tatortVersion":"1","tatorter":[]}""", "inga tätorter")]
    [InlineData("""{"tatortVersion":"1","tatorter":[{"code":"0114XX101","name":"A","kommunCode":"0114"}]}""", "felformad")]
    [InlineData("""{"tatortVersion":"1","tatorter":[{"code":"9999TB101","name":"A","kommunCode":"9999"}]}""", "kommunkod")]
    [InlineData("""{"tatortVersion":"1","tatorter":[{"code":"0114TB101","name":"A","kommunCode":"0115"}]}""", "kommunkod")]
    [InlineData("""{"tatortVersion":"1","tatorter":[{"code":"0114TB101","name":"A","kommunCode":"0114"},{"code":"0114TB101","name":"B","kommunCode":"0114"}]}""", "två gånger")]
    [InlineData("""{"tatortVersion":"1","tatorter":[{"code":"0114TB101","name":"  ","kommunCode":"0114"}]}""", "saknar namn")]
    public void TheLoader_RefusesAMalformedAsset(string json, string expectedReason)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        Should.Throw<InvalidOperationException>(() =>
                CvPlaceLexiconLoader.LoadFrom(CriterionReferenceLoader.LoadKommuner(), stream))
            .Message.ShouldContain(expectedReason);
    }

    [Theory]
    [InlineData("Testa Testsson | testa@example.com | 070-000 00 00 | Åkersberga", "Åkersberga")]
    [InlineData("Testa Testsson | testa@example.com | LinkedIn: testa-testsson | Åkersberga", "Åkersberga")]
    [InlineData("Bunkeflostrand", "Bunkeflostrand")]
    [InlineData("• Hjärup", "Hjärup")]
    public void ATätortInTheContactBlock_IsTheLocation_AndNotLeftAsResidue(string contactLine, string expected)
    {
        var cv = string.Join('\n', "Testa Testsson", "testa@example.com", contactLine, "", "Profil", "Testare.");

        var content = _sut.Segment(cv).Content;

        content.Contact.Location.ShouldBe(expected);
        (content.Preamble ?? string.Empty).ShouldNotContain(expected);
    }

    [Fact]
    public void AnEmployersTätortInAnExperienceEntry_NeverBecomesTheLocation()
    {
        var cv = string.Join('\n',
            "Testa Testsson", "testa@example.com", "",
            "Erfarenhet", "Testare — Exempelbolaget AB, Åkersberga", "2021 – 2024", "Testade betalflöden.");

        _sut.Segment(cv).Content.Contact.Location.ShouldBeNull();
    }
}

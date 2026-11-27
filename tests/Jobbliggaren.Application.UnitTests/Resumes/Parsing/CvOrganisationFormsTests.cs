using System.Text;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Parsing;

/// <summary>
/// #2353 (senior-cto-advisor D8): the organisation question an entry header is read with, asked of
/// the shipped lexicon's v8 forms, and the loader's refusals for a form that could never match.
/// </summary>
public class CvOrganisationFormsTests
{
    private static readonly CvParsingLexiconData Lexicon = CvParsingLexiconFixture.Load();

    [Theory]
    [InlineData("Exempel AB")]
    [InlineData("Exempel AB (publ)")]
    [InlineData("Cajin Aktiebolag")]
    [InlineData("Exempel ek. för.")]
    [InlineData("Lerums kommun")]
    [InlineData("Göteborgs Stad")]
    [InlineData("Beta Ltd")]
    [InlineData("Exempel A/S")]
    [InlineData("Region Skåne")]
    [InlineData("AB Volvo")]
    [InlineData("Handelsbolaget Cajin")]
    [InlineData("Exempel AB, Göteborg")]
    [InlineData(" Exempel AB · ")]
    public void IsOrganisation_AFieldEndingInOrOpeningWithAForm_NamesAnOrganisation(string field) =>
        Lexicon.IsOrganisation(field).ShouldBeTrue();

    [Theory]
    [InlineData("Karlstad")]
    [InlineData("Projektledare i Halmstad")]
    [InlineData("AB")]
    [InlineData("ekonomisk förening")]
    [InlineData("SE")]
    [InlineData("Stockholm, SE")]
    [InlineData("Region")]
    [InlineData("Exempel ab")]
    [InlineData("Exempel as")]
    [InlineData("Testare")]
    [InlineData("Volvo Cars")]
    [InlineData("")]
    public void IsOrganisation_AFieldWithoutAWholeFormAndAName_NamesNone(string field) =>
        Lexicon.IsOrganisation(field).ShouldBeFalse();

    private static MemoryStream Json(string organisationForms) => new(Encoding.UTF8.GetBytes(
        $$"""
        { "version": 9,
          "headings": { "experience": ["erfarenhet"] },
          "languageHints": { "sv": ["och"] },
          "freeSections": { "projekt": ["projekt"] },
          "organisationForms": {{organisationForms}} }
        """));

    [Theory]
    [InlineData("""{ "suffixes": ["-"] }""", "can never match")]
    [InlineData("""{ "suffixes": ["A|S"] }""", "can never match")]
    [InlineData("""{ "suffixes": ["AB, Göteborg"] }""", "can never match")]
    [InlineData("""{ "prefixes": ["Region Stockholm"] }""", "can never match")]
    [InlineData("""{ "suffixes": ["AB", " AB "] }""", "listed twice")]
    [InlineData("""{ "prefixes": ["Region", "Region"] }""", "listed twice")]
    public void Load_RefusesAnOrganisationFormThatCouldNeverMatch(string organisationForms, string reason) =>
        Should.Throw<InvalidOperationException>(() => CvParsingLexiconLoader.LoadFrom(Json(organisationForms)))
            .Message.ShouldContain(reason);

    [Fact]
    public void Load_SkipsAVisiblyEmptyForm_AndReadsTheRest()
    {
        var lexicon = CvParsingLexiconLoader.LoadFrom(Json("""{ "suffixes": ["", "AB"], "prefixes": ["  "] }"""));

        lexicon.OrganisationSuffixes.ShouldBe(["AB"]);
        lexicon.OrganisationPrefixes.ShouldBeEmpty();
    }

    [Fact]
    public void Load_ReadsNoOrganisation_WhenTheBlockIsAbsent()
    {
        var lexicon = CvParsingLexiconLoader.LoadFrom(new MemoryStream(Encoding.UTF8.GetBytes(
            """
            { "version": 9,
              "headings": { "experience": ["erfarenhet"] },
              "languageHints": { "sv": ["och"] },
              "freeSections": { "projekt": ["projekt"] } }
            """)));

        lexicon.IsOrganisation("Exempel AB").ShouldBeFalse();
    }
}
